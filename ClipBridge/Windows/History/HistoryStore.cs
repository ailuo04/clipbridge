using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBridge.Windows.History;

internal sealed record HistoryStoreSnapshot(
    [property: JsonPropertyName("entries")] IReadOnlyList<HistorySyncEvent> Entries,
    [property: JsonPropertyName("tombstones")] IReadOnlyList<HistoryTombstone> Tombstones);

internal sealed class HistoryStore
{
    private const int LogSchemaVersion = 1;
    private const string UpsertOperation = "upsert";
    private const string TombstoneOperation = "tombstone";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Coordinates instances in this process only; it is not a cross-process lock.
    private static readonly ConcurrentDictionary<string, object> PathGates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _logPath;
    private readonly object _pathGate;
    private readonly Action<string, string> _replaceAtomically;
    private readonly Func<string> _tempPathFactory;
    private Dictionary<(string PeerId, string EventId), HistoryLocalRecord> _events = new();
    private readonly Dictionary<(string PeerId, string EventId), HistoryTombstone> _tombstones = new();

    public HistoryStore(string? logPath = null, Action<string, string>? replaceAtomically = null, Func<string>? tempPathFactory = null)
    {
        _logPath = string.IsNullOrWhiteSpace(logPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "history", "events-v1.jsonl")
            : Path.GetFullPath(logPath);
        _pathGate = PathGates.GetOrAdd(_logPath, static _ => new object());
        _replaceAtomically = replaceAtomically ?? ReplaceAtomically;
        _tempPathFactory = tempPathFactory ?? (() => _logPath + ".tmp." + Guid.NewGuid().ToString("N"));
        lock (_pathGate) ReloadFromDisk();
    }

    public void Upsert(HistoryLocalRecord entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        HistoryLocalRecord incoming = CopyRecord(entry);
        lock (_pathGate)
        {
            ReloadFromDisk();
            HistoryPolicy.ValidateEvent(incoming.Event, incoming.Event.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var key = (incoming.Event.PeerId, incoming.Event.EventId);
            _events.TryGetValue(key, out HistoryLocalRecord? current);
            if (current is not null && !EventsEqual(current.Event, incoming.Event))
                throw new InvalidDataException("History event IDs are immutable; conflicting payload received.");
            if (_tombstones.ContainsKey(key)) return;
            if (current is not null &&
                EventsEqual(current.Event, incoming.Event) &&
                current.LocalFileReferences.Count == incoming.LocalFileReferences.Count &&
                current.LocalFileReferences.All(pair => incoming.LocalFileReferences.TryGetValue(pair.Key, out string? value) && value == pair.Value))
                return;

            HistoryLocalRecord merged = MergeLocalReferences(current, incoming);
            Append(CreateUpsertOperation(merged));
            _events[key] = merged;
        }
    }

    /// <summary>Returns visible entries newest-first, with event ID as the stable tie-breaker.</summary>
    public IReadOnlyList<HistoryLocalRecord> GetRecent(string peerId, long nowUtc)
    {
        RequirePeerId(peerId);
        lock (_pathGate)
        {
            ReloadFromDisk();
            return _events.Values
                .Where(record => record.Event.PeerId == peerId && HistoryPolicy.IsVisible(record.Event.CreatedUtc, nowUtc))
                .OrderByDescending(record => record.Event.CreatedUtc)
                .ThenBy(record => record.Event.EventId, StringComparer.Ordinal)
                .Select(CopyRecord)
                .ToArray();
        }
    }

    public void DeleteRecord(string peerId, string eventId, long nowUtc)
    {
        RequirePeerId(peerId);
        RequireEventId(eventId);
        lock (_pathGate)
        {
            ReloadFromDisk();
            var tombstone = new HistoryTombstone(eventId, peerId, nowUtc);
            var key = (peerId, eventId);
            if (_tombstones.TryGetValue(key, out HistoryTombstone? current) && current.DeletedUtc >= nowUtc)
            {
                _events.Remove(key);
                return;
            }

            Append(CreateTombstoneOperation(tombstone));
            _events.Remove(key);
            _tombstones[key] = tombstone;
        }
    }

    public void Merge(string peerId, IEnumerable<HistorySyncEvent> entries, IEnumerable<HistoryTombstone> tombstones, long nowUtc)
    {
        RequirePeerId(peerId);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tombstones);
        lock (_pathGate)
        {
            ReloadFromDisk();
            HistoryTombstone[] incomingTombstones = tombstones.ToArray();
            HistorySyncEvent[] incomingEntries = entries.ToArray();
            foreach (HistoryTombstone tombstone in incomingTombstones)
                HistoryPolicy.ValidateTombstone(tombstone, peerId);
            foreach (HistorySyncEvent entry in incomingEntries)
                HistoryPolicy.ValidateEvent(entry, peerId, nowUtc);

            var batchById = new Dictionary<(string PeerId, string EventId), HistorySyncEvent>();
            foreach (HistorySyncEvent entry in incomingEntries)
            {
                var key = (entry.PeerId, entry.EventId);
                if (_events.TryGetValue(key, out HistoryLocalRecord? stored) && !EventsEqual(stored.Event, entry))
                    throw new InvalidDataException("History event IDs are immutable; conflicting payload received.");
                if (batchById.TryGetValue(key, out HistorySyncEvent? prior) && !EventsEqual(prior, entry))
                    throw new InvalidDataException("History event IDs are immutable; conflicting payloads in merge batch.");
                batchById[key] = entry;
            }

            foreach (HistoryTombstone tombstone in incomingTombstones)
                MergeTombstone(tombstone);
            foreach (HistorySyncEvent entry in incomingEntries)
            {
                var key = (entry.PeerId, entry.EventId);
                if (_tombstones.ContainsKey(key)) continue;
                if (_events.TryGetValue(key, out HistoryLocalRecord? current) && EventsEqual(current.Event, entry)) continue;
                HistoryLocalRecord merged = MergeLocalReferences(current, new HistoryLocalRecord(entry, new Dictionary<string, string>()));
                Append(CreateUpsertOperation(merged));
                _events[key] = merged;
            }
        }
    }

    public HistoryStoreSnapshot GetSyncSnapshot(string peerId, long nowUtc)
    {
        RequirePeerId(peerId);
        lock (_pathGate)
        {
            ReloadFromDisk();
            IReadOnlyList<HistorySyncEvent> entries = _events.Values
                .Where(record => record.Event.PeerId == peerId && HistoryPolicy.IsVisible(record.Event.CreatedUtc, nowUtc))
                .Select(record => record.Event)
                .OrderByDescending(entry => entry.CreatedUtc)
                .ThenBy(entry => entry.EventId, StringComparer.Ordinal)
                .ToArray();
            IReadOnlyList<HistoryTombstone> tombstones = _tombstones.Values
                .Where(tombstone => tombstone.PeerId == peerId)
                .OrderByDescending(tombstone => tombstone.DeletedUtc)
                .ThenBy(tombstone => tombstone.EventId, StringComparer.Ordinal)
                .ToArray();
            return new HistoryStoreSnapshot(entries, tombstones);
        }
    }

    public void Prune(long nowUtc)
    {
        lock (_pathGate)
        {
            ReloadFromDisk();
            var retainedEvents = _events
                .Where(pair => pair.Value.Event.CreatedUtc > nowUtc - HistoryPolicy.SevenDaysMilliseconds)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (retainedEvents.Count == _events.Count) return;
            Compact(retainedEvents, _tombstones);
            _events = retainedEvents;
        }
    }

    private void ReloadFromDisk()
    {
        _events.Clear();
        _tombstones.Clear();
        if (!File.Exists(_logPath)) return;
        byte[] bytes = File.ReadAllBytes(_logPath);
        int cursor = 0;
        while (cursor < bytes.Length)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', cursor);
            bool hasNewline = newline >= 0;
            int end = hasNewline ? newline : bytes.Length;
            bool valid = false;
            try
            {
                string line = StrictUtf8.GetString(bytes, cursor, end - cursor).TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("History log line is empty.");
                HistoryOperation operation = JsonSerializer.Deserialize<HistoryOperation>(line, JsonOptions)
                    ?? throw new InvalidDataException("History operation is required.");
                ApplyLoadedOperation(operation);
                valid = true;
            }
            catch (Exception exception) when (IsLogDataError(exception))
            {
                bool isTrailingLine = !hasNewline || newline == bytes.Length - 1;
                if (isTrailingLine)
                {
                    TruncateLog(cursor);
                    return;
                }
            }

            if (!hasNewline)
            {
                if (valid) AppendLineTerminator();
                return;
            }
            cursor = newline + 1;
        }
    }
    private static bool IsLogDataError(Exception exception) => exception is
        JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException or
        KeyNotFoundException or IndexOutOfRangeException or NullReferenceException or System.Text.DecoderFallbackException;

    private void AppendLineTerminator()
    {
        using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.WriteByte((byte)'\n');
        stream.Flush(true);
    }

    private void ApplyLoadedOperation(HistoryOperation operation)
    {
        if (operation.SchemaVersion != LogSchemaVersion) throw new InvalidDataException("Unsupported history log schema.");
        switch (operation.Operation)
        {
            case UpsertOperation when operation.Record is not null:
                HistoryPolicy.ValidateEvent(operation.Record.Event, operation.Record.Event.PeerId, long.MaxValue);
                ApplyUpsert(new HistoryLocalRecord(operation.Record.Event, operation.Record.LocalFileReferences ?? new Dictionary<string, string>()));
                break;
            case TombstoneOperation when operation.Tombstone is not null:
                HistoryPolicy.ValidateTombstone(operation.Tombstone, operation.Tombstone.PeerId);
                ApplyTombstone(operation.Tombstone);
                break;
            default:
                throw new InvalidDataException("Invalid history operation.");
        }
    }

    private void ApplyUpsert(HistoryLocalRecord entry)
    {
        var key = (entry.Event.PeerId, entry.Event.EventId);
        if (_tombstones.ContainsKey(key)) return;
        if (_events.TryGetValue(key, out HistoryLocalRecord? current))
        {
            if (!EventsEqual(current.Event, entry.Event))
                throw new InvalidDataException("History event IDs are immutable; conflicting payload found in the log.");
            _events[key] = MergeLocalReferences(current, entry);
            return;
        }
        _events.Add(key, CopyRecord(entry));
    }

    private void MergeTombstone(HistoryTombstone tombstone)
    {
        var key = (tombstone.PeerId, tombstone.EventId);
        if (_tombstones.TryGetValue(key, out HistoryTombstone? current) && current.DeletedUtc >= tombstone.DeletedUtc)
        {
            _events.Remove(key);
            return;
        }
        Append(CreateTombstoneOperation(tombstone));
        _events.Remove(key);
        _tombstones[key] = tombstone;
    }
    private void ApplyTombstone(HistoryTombstone tombstone)
    {
        var key = (tombstone.PeerId, tombstone.EventId);
        if (_tombstones.TryGetValue(key, out HistoryTombstone? current) && current.DeletedUtc >= tombstone.DeletedUtc)
        {
            _events.Remove(key);
            return;
        }
        _events.Remove(key);
        _tombstones[key] = tombstone;
    }

    private void Append(HistoryOperation operation)
    {
        string? directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(operation, JsonOptions);
        using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private void Compact(IReadOnlyDictionary<(string PeerId, string EventId), HistoryLocalRecord> retainedEvents, IReadOnlyDictionary<(string PeerId, string EventId), HistoryTombstone> retainedTombstones)
    {
        string? directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        (string temporaryPath, FileStream temporaryStream) = CreateCompactionTempFile();
        try
        {
            using (temporaryStream)
            {
                foreach (HistoryLocalRecord entry in retainedEvents.Values.OrderBy(item => item.Event.PeerId, StringComparer.Ordinal).ThenBy(item => item.Event.EventId, StringComparer.Ordinal))
                    WriteLine(temporaryStream, CreateUpsertOperation(entry));
                foreach (HistoryTombstone tombstone in retainedTombstones.Values.OrderBy(item => item.PeerId, StringComparer.Ordinal).ThenBy(item => item.EventId, StringComparer.Ordinal))
                    WriteLine(temporaryStream, CreateTombstoneOperation(tombstone));
                temporaryStream.Flush(true);
            }

            _replaceAtomically(temporaryPath, _logPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private (string Path, FileStream Stream) CreateCompactionTempFile()
    {
        while (true)
        {
            string candidate = _tempPathFactory();
            if (!string.Equals(Path.GetDirectoryName(candidate), Path.GetDirectoryName(_logPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Compaction temporary file must be a sibling of the history log.");
            try
            {
                return (candidate, new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None));
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // A collision was not created by this call; retry without opening, truncating, or deleting it.
            }
        }
    }
    private static void ReplaceAtomically(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
        else File.Move(temporaryPath, destinationPath);
    }

    private static void WriteLine(FileStream stream, HistoryOperation operation)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(operation, JsonOptions) + "\n");
        stream.Write(bytes, 0, bytes.Length);
    }

    private void TruncateLog(int length)
    {
        using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.SetLength(length);
        stream.Flush(true);
    }

    private static bool EventsEqual(HistorySyncEvent left, HistorySyncEvent right) =>
        left.SchemaVersion == right.SchemaVersion &&
        left.EventId == right.EventId &&
        left.PeerId == right.PeerId &&
        left.SenderDeviceId == right.SenderDeviceId &&
        left.CreatedUtc == right.CreatedUtc &&
        left.ContentType == right.ContentType &&
        left.Text == right.Text &&
        left.State == right.State &&
        left.Files.SequenceEqual(right.Files);

    private static HistoryLocalRecord MergeLocalReferences(HistoryLocalRecord? current, HistoryLocalRecord incoming)
    {
        if (current is null || current.LocalFileReferences.Count == 0) return CopyRecord(incoming);
        if (incoming.LocalFileReferences.Count == 0) return new HistoryLocalRecord(incoming.Event, new Dictionary<string, string>(current.LocalFileReferences, StringComparer.Ordinal));
        var references = new Dictionary<string, string>(current.LocalFileReferences, StringComparer.Ordinal);
        foreach (var pair in incoming.LocalFileReferences) references[pair.Key] = pair.Value;
        return new HistoryLocalRecord(incoming.Event, references);
    }

    private static HistoryLocalRecord CopyRecord(HistoryLocalRecord record) =>
        new(record.Event, new Dictionary<string, string>(record.LocalFileReferences, StringComparer.Ordinal));

    private static HistoryOperation CreateUpsertOperation(HistoryLocalRecord entry) =>
        new(LogSchemaVersion, UpsertOperation, new HistoryStoredRecord(entry.Event, new Dictionary<string, string>(entry.LocalFileReferences)), null);

    private static HistoryOperation CreateTombstoneOperation(HistoryTombstone tombstone) =>
        new(LogSchemaVersion, TombstoneOperation, null, tombstone);

    private static void RequirePeerId(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId)) throw new ArgumentException("History peer ID is required.", nameof(peerId));
    }

    private static void RequireEventId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("History event ID is required.", nameof(eventId));
    }

    private sealed record HistoryOperation(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("operation")] string Operation,
        [property: JsonPropertyName("record")] HistoryStoredRecord? Record,
        [property: JsonPropertyName("tombstone")] HistoryTombstone? Tombstone);

    private sealed record HistoryStoredRecord(
        [property: JsonPropertyName("event")] HistorySyncEvent Event,
        [property: JsonPropertyName("localFileReferences")] Dictionary<string, string>? LocalFileReferences);
}