using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBridge.Windows.History;

internal sealed record HistorySyncMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("peerId")] string PeerId,
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("entries"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HistorySyncEvent[]? Entries,
    [property: JsonPropertyName("tombstones"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HistoryTombstone[]? Tombstones);

internal static class HistorySyncProtocol
{
    public const int Version = 1;
    public const int MaxPayloadBytes = SharedProtocol.MaxFrameBytes - 28;
    public const int MaxPagesPerConnection = 64;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };
    private static readonly string[] MessageTypes = { "historySyncHello", "historySyncRequest", "historySyncPage", "historySyncAck" };

    public static byte[] Encode(HistorySyncMessage message, string authenticatedPeerId, long nowUtc, int maxPayloadBytes = MaxPayloadBytes)
    {
        Validate(message, authenticatedPeerId, nowUtc);
        if (maxPayloadBytes < 1 || maxPayloadBytes > MaxPayloadBytes) throw new InvalidDataException("Invalid history sync payload limit.");
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (encoded.Length > maxPayloadBytes) throw new InvalidDataException("History sync payload is too large.");
        return encoded;
    }

    public static HistorySyncMessage Decode(byte[] payload, string authenticatedPeerId, long nowUtc, int maxPayloadBytes = MaxPayloadBytes)
    {
        if (maxPayloadBytes < 1 || maxPayloadBytes > MaxPayloadBytes || payload is null || payload.Length > maxPayloadBytes) throw new InvalidDataException("History sync payload is too large.");
        HistorySyncMessage message;
        try { message = JsonSerializer.Deserialize<HistorySyncMessage>(payload, JsonOptions) ?? throw new InvalidDataException("History sync message is required."); }
        catch (JsonException exception) { throw new InvalidDataException("Invalid history sync message.", exception); }
        Validate(message, authenticatedPeerId, nowUtc);
        return message;
    }

    public static IReadOnlyList<HistorySyncMessage> CreatePages(
        string requestId,
        string peerId,
        HistoryStoreSnapshot snapshot,
        int maxPayloadBytes = MaxPayloadBytes,
        int maxPages = MaxPagesPerConnection,
        long nowUtc = 0)
    {
        if (nowUtc == 0) nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!PairingProtocol.ValidId(requestId) || !PairingProtocol.ValidId(peerId)) throw new InvalidDataException("Invalid history sync identity.");
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (maxPages < 1 || maxPages > MaxPagesPerConnection) throw new InvalidDataException("Invalid history sync page limit.");
        var entries = snapshot.Entries.Select(entry => entry with { PeerId = peerId }).OrderByDescending(entry => entry.CreatedUtc).ThenBy(entry => entry.EventId, StringComparer.Ordinal).ToArray();
        var tombstones = snapshot.Tombstones.Select(tombstone => tombstone with { PeerId = peerId }).OrderByDescending(tombstone => tombstone.DeletedUtc).ThenBy(tombstone => tombstone.EventId, StringComparer.Ordinal).ToArray();
        if (maxPayloadBytes < 1 || maxPayloadBytes > MaxPayloadBytes) throw new InvalidDataException("Invalid history sync payload limit.");
        var pages = new List<HistorySyncMessage>();
        var currentEntries = new List<HistorySyncEvent>();
        var currentTombstones = new List<HistoryTombstone>();
        int pageNumber = 0;
        HistorySyncMessage CreatePage(int number, HistorySyncEvent[] pageEntries, HistoryTombstone[] pageTombstones) =>
            new("historySyncPage", Version, requestId, peerId, number, pageEntries, pageTombstones);
        int MeasureEmptyPage() => Encode(CreatePage(pageNumber, Array.Empty<HistorySyncEvent>(), Array.Empty<HistoryTombstone>()), peerId, nowUtc, maxPayloadBytes).Length;
        int currentPageBytes = MeasureEmptyPage();

        foreach (HistorySyncEvent entry in entries)
        {
            int itemBytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions).Length;
            AddItem(entry, null, itemBytes, isEntry: true);
        }
        foreach (HistoryTombstone tombstone in tombstones)
        {
            int itemBytes = JsonSerializer.SerializeToUtf8Bytes(tombstone, JsonOptions).Length;
            AddItem(null, tombstone, itemBytes, isEntry: false);
        }
        if (currentEntries.Count != 0 || currentTombstones.Count != 0) Flush();
        if (pages.Count == 0) pages.Add(CreatePage(0, Array.Empty<HistorySyncEvent>(), Array.Empty<HistoryTombstone>()));
        return pages;

        void AddItem(HistorySyncEvent? entry, HistoryTombstone? tombstone, int itemBytes, bool isEntry)
        {
            int sameCollectionCount = isEntry ? currentEntries.Count : currentTombstones.Count;
            int additionalBytes = itemBytes + (sameCollectionCount > 0 ? 1 : 0);
            if (currentPageBytes + additionalBytes > maxPayloadBytes)
            {
                if (currentEntries.Count == 0 && currentTombstones.Count == 0) throw new InvalidDataException("A history record cannot fit in a sync page.");
                Flush();
                additionalBytes = itemBytes;
                if (currentPageBytes + additionalBytes > maxPayloadBytes) throw new InvalidDataException("A history record cannot fit in a sync page.");
            }
            if (entry is not null) currentEntries.Add(entry); else currentTombstones.Add(tombstone!);
            currentPageBytes += additionalBytes;
        }

        void Flush()
        {
            if (pages.Count >= maxPages) throw new InvalidDataException("History sync page count exceeds the connection limit.");
            HistorySyncMessage page = CreatePage(pageNumber, currentEntries.ToArray(), currentTombstones.ToArray());
            if (Encode(page, peerId, nowUtc, maxPayloadBytes).Length > maxPayloadBytes) throw new InvalidDataException("History sync payload is too large.");
            pages.Add(page);
            pageNumber++;
            currentEntries.Clear();
            currentTombstones.Clear();
            currentPageBytes = MeasureEmptyPage();
        }
    }

    private static void Validate(HistorySyncMessage message, string authenticatedPeerId, long nowUtc)
    {
        if (message is null) throw new InvalidDataException("History sync message is required.");
        if (!MessageTypes.Contains(message.Type, StringComparer.Ordinal)) throw new InvalidDataException("Unknown history sync message type.");
        if (message.SchemaVersion != Version) throw new InvalidDataException("Unsupported history sync version.");
        if (!PairingProtocol.ValidId(message.RequestId)) throw new InvalidDataException("History sync requestId is invalid.");
        if (!PairingProtocol.ValidId(message.PeerId)) throw new InvalidDataException("History sync peerId is invalid.");
        if (!PairingProtocol.ValidId(authenticatedPeerId)) throw new InvalidDataException("Authenticated history peerId is invalid.");
        if (message.PeerId != authenticatedPeerId) throw new InvalidDataException("History sync peer does not match the authenticated route.");
        if (message.PageNumber < 0) throw new InvalidDataException("History sync page number cannot be negative.");
        bool isPage = message.Type == "historySyncPage";
        if (isPage && (message.PageNumber >= MaxPagesPerConnection || message.Entries is null || message.Tombstones is null)) throw new InvalidDataException("History sync page collections or number are invalid.");
        if (message.Type == "historySyncHello" && message.PageNumber is < 1 or > MaxPagesPerConnection) throw new InvalidDataException("History sync hello page count is invalid.");
        if ((message.Type == "historySyncRequest" || message.Type == "historySyncAck") && message.PageNumber >= MaxPagesPerConnection) throw new InvalidDataException("History sync page number exceeds the connection limit.");
        if (!isPage && ((message.Entries?.Length ?? 0) != 0 || (message.Tombstones?.Length ?? 0) != 0)) throw new InvalidDataException("Non-page history sync messages cannot carry records.");
        if (message.Entries is not null)
            foreach (HistorySyncEvent entry in message.Entries) HistoryPolicy.ValidateEvent(entry, authenticatedPeerId, nowUtc);
        if (message.Tombstones is not null)
            foreach (HistoryTombstone tombstone in message.Tombstones) HistoryPolicy.ValidateTombstone(tombstone, authenticatedPeerId);
    }

    public static HistorySyncMessage CreateHello(string requestId, string peerId, int pageCount) =>
        new("historySyncHello", Version, requestId, peerId, pageCount, null, null);

    public static HistorySyncMessage CreateRequest(string requestId, string peerId, int pageNumber) =>
        new("historySyncRequest", Version, requestId, peerId, pageNumber, null, null);

    public static HistorySyncMessage CreateAck(string requestId, string peerId, int pageNumber) =>
        new("historySyncAck", Version, requestId, peerId, pageNumber, null, null);
}
