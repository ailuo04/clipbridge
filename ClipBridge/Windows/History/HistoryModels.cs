using System.Text.Json.Serialization;

namespace ClipBridge.Windows.History;

internal sealed record HistorySyncEvent(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("peerId")] string PeerId,
    [property: JsonPropertyName("senderDeviceId")] string SenderDeviceId,
    [property: JsonPropertyName("createdUtc")] long CreatedUtc,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("files")] HistoryFileMetadata[] Files,
    [property: JsonPropertyName("state")] string State);

internal sealed record HistoryFileMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record HistoryTombstone(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("peerId")] string PeerId,
    [property: JsonPropertyName("deletedUtc")] long DeletedUtc);

internal sealed record HistorySyncPage(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("peerId")] string PeerId,
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("entries")] HistorySyncEvent[] Entries,
    [property: JsonPropertyName("tombstones")] HistoryTombstone[] Tombstones);

// Local file references are deliberately kept outside the synchronized event DTO.
internal sealed record HistoryLocalRecord(
    HistorySyncEvent Event,
    IReadOnlyDictionary<string, string> LocalFileReferences);