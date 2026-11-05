using System.Text;
using System.Text.Json;
using ClipBridge.Windows;
using ClipBridge.Windows.History;

internal static class HistoryTests
{
    private const long FixedNowUtc = 1_800_000_000_000;
    private const string PeerId = "peer-a";

    public static void Run()
    {
        RetentionUsesUtcMilliseconds();
        ValidationRejectsInvalidEvents();
        RouteValidationBindsEventsToPeer();
        SyncContractUsesStableJsonNamesAndExcludesLocalPaths();
        PageEnvelopeValidationRejectsInvalidFields();
        StoreReloadsPerPeerAndKeepsFilesWhenDeleting();
        StorePrunesExpiredRecordsAndRecoversTruncatedTail();
        Console.WriteLine("HISTORY TESTS PASSED");
    }

    private static void RetentionUsesUtcMilliseconds()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(FixedNowUtc).ToUnixTimeMilliseconds();
        var sixDaysTwentyThreeHoursFiftyNineMinutesAgo = now - TimeSpan.FromDays(6).Ticks / TimeSpan.TicksPerMillisecond - TimeSpan.FromHours(23).Ticks / TimeSpan.TicksPerMillisecond - TimeSpan.FromMinutes(59).Ticks / TimeSpan.TicksPerMillisecond;
        Program.Check(HistoryPolicy.IsVisible(sixDaysTwentyThreeHoursFiftyNineMinutesAgo, now), "six-day twenty-three-hour event visible");
        Program.Check(HistoryPolicy.IsVisible(now - HistoryPolicy.SevenDaysMilliseconds + 1, now), "event one millisecond inside seven-day window visible");
        Program.Check(!HistoryPolicy.IsVisible(now - (long)TimeSpan.FromDays(7).TotalMilliseconds, now), "seven-day boundary expired");
        Program.Check(!HistoryPolicy.IsVisible(now + 1, now), "future event is not visible");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(createdUtc: now + 1), PeerId, now), "future timestamp rejected");
    }

    private static void ValidationRejectsInvalidEvents()
    {
        HistoryPolicy.ValidateEvent(Event(), PeerId, FixedNowUtc);
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(eventId: " "), PeerId, FixedNowUtc), "empty event ID rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(schemaVersion: 2), PeerId, FixedNowUtc), "unknown event schema rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(files: new[] { new HistoryFileMetadata("demo.txt", -1, new string('a', 64)) }), PeerId, FixedNowUtc), "negative file size rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(files: new[] { new HistoryFileMetadata("demo.txt", 1, "not-a-sha256") }), PeerId, FixedNowUtc), "malformed SHA-256 rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(files: new[] { new HistoryFileMetadata("../unsafe.txt", 1, new string('a', 64)) }), PeerId, FixedNowUtc), "unsafe file name rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(text: new string('界', SharedProtocol.MaxTextBytes)), PeerId, FixedNowUtc), "oversized UTF-8 text rejected");
        HistoryPolicy.ValidateEvent(Event(text: new string('a', SharedProtocol.MaxTextBytes)), PeerId, FixedNowUtc);
    }

    private static void RouteValidationBindsEventsToPeer()
    {
        HistoryPolicy.ValidateEvent(Event(peerId: PeerId), PeerId, FixedNowUtc);
        RejectsInvalid(() => HistoryPolicy.ValidateEvent(Event(peerId: "peer-b"), PeerId, FixedNowUtc), "event for another authenticated peer rejected");
        RejectsInvalid(() => HistoryPolicy.ValidateTombstone(new HistoryTombstone("event-1", "peer-b", FixedNowUtc), PeerId), "tombstone for another authenticated peer rejected");
    }

    private static void PageEnvelopeValidationRejectsInvalidFields()
    {
        var page = new HistorySyncPage(1, "request-1", PeerId, 0, new[] { Event() }, Array.Empty<HistoryTombstone>());
        HistoryPolicy.ValidatePageEnvelope(page, PeerId);
        Program.Check(true, "valid page envelope accepted");
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(page with { RequestId = "" }, PeerId), "empty page requestId rejected");
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(page with { SchemaVersion = 2 }, PeerId), "unsupported page schema rejected");
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(page with { PeerId = "peer-b" }, PeerId), "page for another authenticated peer rejected");
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(page with { PageNumber = -1 }, PeerId), "negative page number rejected");
        var missingEntries = JsonSerializer.Deserialize<HistorySyncPage>("""{"schemaVersion":1,"requestId":"request-1","peerId":"peer-a","pageNumber":0,"tombstones":[]}""")!;
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(missingEntries, PeerId), "missing page entries rejected");
        var missingTombstones = JsonSerializer.Deserialize<HistorySyncPage>("""{"schemaVersion":1,"requestId":"request-1","peerId":"peer-a","pageNumber":0,"entries":[]}""")!;
        RejectsInvalid(() => HistoryPolicy.ValidatePageEnvelope(missingTombstones, PeerId), "missing page tombstones rejected");
    }
    private static void StoreReloadsPerPeerAndKeepsFilesWhenDeleting()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            string localFile = Path.Combine(root, "received.txt");
            File.WriteAllText(localFile, "keep these bytes");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            HistorySyncEvent first = Event(eventId: "first", createdUtc: now);
            HistorySyncEvent otherPeer = Event(eventId: "other-peer", peerId: "peer-b", createdUtc: now);
            var references = new Dictionary<string, string> { [first.EventId] = localFile };

            var initial = new HistoryStore(logPath);
            initial.Upsert(new HistoryLocalRecord(first, references));
            initial.Upsert(new HistoryLocalRecord(first, references));
            initial.Upsert(new HistoryLocalRecord(otherPeer, new Dictionary<string, string>()));

            var reopened = new HistoryStore(logPath);
            var recent = reopened.GetRecent(PeerId, now);
            Program.Check(recent.Count == 1 && recent[0].Event.EventId == "first", "history reload is idempotent and indexed per peer");
            Program.Check(recent[0].LocalFileReferences[first.EventId] == localFile, "local file reference survives history reload");
            Program.Check(reopened.GetRecent("peer-b", now).Single().Event.EventId == "other-peer", "peer query returns only that peer's history");
            HistoryStoreSnapshot beforeDeleteSnapshot = reopened.GetSyncSnapshot(PeerId, now);
            Program.Check(beforeDeleteSnapshot.Entries.Select(item => item.EventId).SequenceEqual(new[] { "first" }), "sync snapshot contains only the requested peer entries");
            string snapshotJson = JsonSerializer.Serialize(beforeDeleteSnapshot);
            Program.Check(!snapshotJson.Contains(localFile, StringComparison.Ordinal), "sync snapshot excludes local file references");

            reopened.DeleteRecord(PeerId, first.EventId, now);
            Program.Check(reopened.GetRecent(PeerId, now).Count == 0, "manual deletion removes visible history record");
            Program.Check(File.Exists(localFile) && File.ReadAllText(localFile) == "keep these bytes", "manual history deletion preserves the local file");
            Program.Check(reopened.GetSyncSnapshot(PeerId, now).Tombstones.Any(item => item.EventId == first.EventId), "manual deletion creates a sync tombstone");
            reopened.Upsert(new HistoryLocalRecord(first, references));
            Program.Check(reopened.GetRecent(PeerId, now).Count == 0, "tombstone prevents a later upsert from resurrecting history");

            HistorySyncEvent remoteDeleted = Event(eventId: "remote-deleted", createdUtc: now);
            reopened.Upsert(new HistoryLocalRecord(remoteDeleted, new Dictionary<string, string> { [remoteDeleted.EventId] = localFile }));
            reopened.Merge(PeerId, new[] { remoteDeleted }, new[] { new HistoryTombstone(remoteDeleted.EventId, PeerId, now) }, now);
            Program.Check(reopened.GetRecent(PeerId, now).Count == 0, "remote tombstone dominates matching event during merge");
            Program.Check(File.Exists(localFile), "remote tombstone merge preserves referenced local file");

            reopened.Upsert(new HistoryLocalRecord(Event(eventId: "expired-after-tombstones", createdUtc: now), new Dictionary<string, string>()));
            reopened.Prune(now + HistoryPolicy.SevenDaysMilliseconds);

            var afterDeleteReload = new HistoryStore(logPath);
            HistoryStoreSnapshot snapshot = afterDeleteReload.GetSyncSnapshot(PeerId, now);
            Program.Check(snapshot.Tombstones.Select(item => item.EventId).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(new[] { "first", "remote-deleted" }), "manual and remote tombstones survive restart");
            Program.Check(afterDeleteReload.GetRecent(PeerId, now).Count == 0, "deleted history does not reappear after restart");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void StorePrunesExpiredRecordsAndRecoversTruncatedTail()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            string localFile = Path.Combine(root, "received.bin");
            File.WriteAllText(localFile, "keep after prune");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            HistorySyncEvent expired = Event(eventId: "expired", createdUtc: now - HistoryPolicy.SevenDaysMilliseconds);
            HistorySyncEvent visible = Event(eventId: "visible", createdUtc: now - HistoryPolicy.SevenDaysMilliseconds + 1);
            var store = new HistoryStore(logPath);
            store.Upsert(new HistoryLocalRecord(expired, new Dictionary<string, string> { [expired.EventId] = localFile }));
            store.Upsert(new HistoryLocalRecord(visible, new Dictionary<string, string> { [visible.EventId] = localFile }));
            store.Prune(now);
            Program.Check(store.GetRecent(PeerId, now).Select(item => item.Event.EventId).SequenceEqual(new[] { "visible" }), "prune expires seven-day boundary and retains one-millisecond-inside event");
            Program.Check(File.Exists(localFile) && File.ReadAllText(localFile) == "keep after prune", "seven-day pruning preserves referenced local files");
            Program.Check(new HistoryStore(logPath).GetRecent(PeerId, now).Single().Event.EventId == "visible", "pruned records stay absent after restart");

            byte[] validLog = File.ReadAllBytes(logPath);
            File.AppendAllText(logPath, "{\"schemaVersion\":1,\"operation\":\"upsert\",\"record\":", Encoding.UTF8);
            var recovered = new HistoryStore(logPath);
            Program.Check(recovered.GetRecent(PeerId, now).Single().Event.EventId == "visible", "truncated final JSONL line is ignored");
            Program.Check(File.ReadAllBytes(logPath).SequenceEqual(validLog), "truncated final JSONL line is repaired without losing valid history");
            recovered.Upsert(new HistoryLocalRecord(Event(eventId: "after-recovery", createdUtc: now), new Dictionary<string, string>()));
            Program.Check(new HistoryStore(logPath).GetRecent(PeerId, now).Count == 2, "store remains writable after repairing truncated tail");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void SyncContractUsesStableJsonNamesAndExcludesLocalPaths()
    {
        JsonElement entry = JsonSerializer.SerializeToElement(Event());
        Program.Check(HasExactJsonProperties(entry, "schemaVersion", "eventId", "peerId", "senderDeviceId", "createdUtc", "contentType", "text", "files", "state"), "event JSON has exactly the contract keys");

        Program.Check(!entry.TryGetProperty("localFilePath", out _) && !entry.TryGetProperty("localFileUri", out _), "event JSON excludes local file paths and URIs");

        JsonElement file = JsonSerializer.SerializeToElement(new HistoryFileMetadata("demo.txt", 3, new string('a', 64)));
        Program.Check(HasExactJsonProperties(file, "name", "size", "sha256"), "file JSON has exactly the contract keys");


        JsonElement tombstone = JsonSerializer.SerializeToElement(new HistoryTombstone("event-1", PeerId, FixedNowUtc));
        Program.Check(HasExactJsonProperties(tombstone, "eventId", "peerId", "deletedUtc"), "tombstone JSON has exactly the contract keys");


        var page = new HistorySyncPage(1, "request-1", PeerId, 0, new[] { Event() }, new[] { new HistoryTombstone("event-1", PeerId, FixedNowUtc) });
        JsonElement pageJson = JsonSerializer.SerializeToElement(page);
        Program.Check(HasExactJsonProperties(pageJson, "schemaVersion", "requestId", "peerId", "pageNumber", "entries", "tombstones"), "sync page JSON has exactly the contract keys");

        Program.Check(page.SchemaVersion == 1, "version-one sync page contract");
    }

    private static bool HasExactJsonProperties(JsonElement value, params string[] expected) =>
        value.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal));
    private static HistorySyncEvent Event(
        int schemaVersion = 1,
        string eventId = "event-1",
        string peerId = PeerId,
        long createdUtc = FixedNowUtc,
        string? text = "hello",
        HistoryFileMetadata[]? files = null) =>
        new(schemaVersion, eventId, peerId, "device-a", createdUtc, "text", text, files ?? Array.Empty<HistoryFileMetadata>(), "sent");

    private static void RejectsInvalid(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { Program.Check(true, name); return; }
        throw new Exception("FAIL " + name);
    }
}