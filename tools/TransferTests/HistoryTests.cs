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
        UpsertTreatsEquivalentFileArraysAsSameEvent();
        MergeRejectsConflictingEventIdsAtomically();
        FailedCompactionPreservesMemoryAndDurableState();
        StoreInstancesReloadBeforeCompaction();
        StoreReloadsPerPeerAndKeepsFilesWhenDeleting();
        StorePrunesExpiredRecordsAndRecoversTruncatedTail();
        StoreRecoversNoncanonicalTrailingJsonLine();
        StoreReplayRejectsConflictingEventPayload();
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
    private static void UpsertTreatsEquivalentFileArraysAsSameEvent()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-equality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            HistorySyncEvent first = Event(
                eventId: "same-event",
                createdUtc: now,
                files: new[] { new HistoryFileMetadata("report.txt", 12, new string('a', 64)) });
            HistorySyncEvent equivalent = Event(
                eventId: "same-event",
                createdUtc: now,
                files: new[] { new HistoryFileMetadata("report.txt", 12, new string('a', 64)) });
            Program.Check(!ReferenceEquals(first.Files, equivalent.Files), "equivalent events use distinct file arrays");

            var store = new HistoryStore(logPath);
            store.Upsert(new HistoryLocalRecord(first, new Dictionary<string, string>()));
            store.Upsert(new HistoryLocalRecord(equivalent, new Dictionary<string, string>()));

            Program.Check(File.ReadAllLines(logPath).Length == 1, "structurally identical event upsert does not append a duplicate operation");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void MergeRejectsConflictingEventIdsAtomically()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int order = 0; order < 2; order++)
        {
            string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-conflict-order-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string logPath = Path.Combine(root, "history", "events-v1.jsonl");
                var store = new HistoryStore(logPath);
                HistorySyncEvent existing = Event(eventId: "existing", createdUtc: now);
                store.Upsert(new HistoryLocalRecord(existing, new Dictionary<string, string>()));
                HistorySyncEvent first = Event(eventId: "duplicate", createdUtc: now, text: "first payload");
                HistorySyncEvent second = Event(eventId: "duplicate", createdUtc: now, text: "second payload");
                HistorySyncEvent[] batch = order == 0 ? new[] { first, second } : new[] { second, first };

                Program.Rejects<InvalidDataException>(() => store.Merge(PeerId, batch, new[] { new HistoryTombstone("existing", PeerId, now) }, now), "conflicting event batch rejected regardless of input order");
                Program.Check(store.GetRecent(PeerId, now).Select(item => item.Event.EventId).SequenceEqual(new[] { "existing" }), "rejected batch leaves existing entries unchanged");
                Program.Check(store.GetSyncSnapshot(PeerId, now).Tombstones.Count == 0, "rejected batch does not partially apply tombstones");
            }
            finally { Directory.Delete(root, true); }
        }

        string existingRoot = Path.Combine(Path.GetTempPath(), "clipbridge-history-conflict-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(existingRoot);
        try
        {
            string logPath = Path.Combine(existingRoot, "history", "events-v1.jsonl");
            var store = new HistoryStore(logPath);
            HistorySyncEvent original = Event(eventId: "stored", createdUtc: now, text: "original payload");
            store.Upsert(new HistoryLocalRecord(original, new Dictionary<string, string>()));
            HistorySyncEvent conflicting = Event(eventId: "stored", createdUtc: now, text: "changed payload");
            Program.Rejects<InvalidDataException>(() => store.Merge(PeerId,
                new[] { Event(eventId: "must-not-append", createdUtc: now), conflicting },
                new[] { new HistoryTombstone("stored", PeerId, now) }, now), "merge rejects conflict with a stored event before applying any operations");
            Program.Check(store.GetRecent(PeerId, now).Select(item => item.Event.EventId).SequenceEqual(new[] { "stored" }) && store.GetRecent(PeerId, now).Single().Event.Text == "original payload", "stored event and batch state remain unchanged after conflict rejection");
            Program.Check(store.GetSyncSnapshot(PeerId, now).Tombstones.Count == 0, "stored conflict rejection leaves tombstones unchanged");

            Program.Rejects<InvalidDataException>(() => store.Upsert(new HistoryLocalRecord(conflicting, new Dictionary<string, string>())), "local upsert rejects a conflicting payload for an immutable event ID");
            Program.Check(store.GetRecent(PeerId, now).Single().Event.Text == "original payload", "conflicting upsert preserves the original event payload");
        }
        finally { Directory.Delete(existingRoot, true); }
    }
    private static void FailedCompactionPreservesMemoryAndDurableState()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-failed-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var normal = new HistoryStore(logPath);
            normal.Upsert(new HistoryLocalRecord(Event(eventId: "will-expire", createdUtc: now), new Dictionary<string, string>()));
            var failing = new HistoryStore(logPath, (_, _) => throw new IOException("injected atomic replace failure"));

            Program.Rejects<IOException>(() => failing.Prune(now + HistoryPolicy.SevenDaysMilliseconds + 1), "failed compaction reports atomic replace failure");
            Program.Check(failing.GetRecent(PeerId, now).Any(item => item.Event.EventId == "will-expire"), "failed compaction leaves the pre-prune in-memory record visible");
            Program.Check(new HistoryStore(logPath).GetRecent(PeerId, now).Any(item => item.Event.EventId == "will-expire"), "failed compaction leaves durable log unchanged");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void StoreInstancesReloadBeforeCompaction()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-instances-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var first = new HistoryStore(logPath);
            var second = new HistoryStore(logPath);
            first.Upsert(new HistoryLocalRecord(Event(eventId: "a-survivor", createdUtc: now), new Dictionary<string, string>()));
            first.Upsert(new HistoryLocalRecord(Event(eventId: "expired", createdUtc: now - HistoryPolicy.SevenDaysMilliseconds - 1), new Dictionary<string, string>()));
            second.Upsert(new HistoryLocalRecord(Event(eventId: "b-survivor", createdUtc: now), new Dictionary<string, string>()));

            first.Prune(now);
            var reopened = new HistoryStore(logPath);
            Program.Check(reopened.GetRecent(PeerId, now).Select(item => item.Event.EventId).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(new[] { "a-survivor", "b-survivor" }), "compaction from one instance preserves entries appended by another instance");
        }
        finally { Directory.Delete(root, true); }
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
    private static void StoreRecoversNoncanonicalTrailingJsonLine()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-case-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            new HistoryStore(logPath).Upsert(new HistoryLocalRecord(Event(eventId: "canonical", createdUtc: now), new Dictionary<string, string>()));
            byte[] canonicalBytes = File.ReadAllBytes(logPath);
            string noncanonicalLine = Encoding.UTF8.GetString(canonicalBytes).TrimEnd('\n').Replace("\"schemaVersion\"", "\"SchemaVersion\"", StringComparison.Ordinal);
            File.AppendAllText(logPath, noncanonicalLine, Encoding.UTF8);

            var recovered = new HistoryStore(logPath);
            Program.Check(File.ReadAllBytes(logPath).SequenceEqual(canonicalBytes), "noncanonical trailing JSON casing is recovered and truncated");
            Program.Check(recovered.GetRecent(PeerId, now).Single().Event.EventId == "canonical", "canonical record remains after noncanonical tail recovery");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void StoreReplayRejectsConflictingEventPayload()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-history-replay-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string logPath = Path.Combine(root, "history", "events-v1.jsonl");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            new HistoryStore(logPath).Upsert(new HistoryLocalRecord(Event(eventId: "immutable", createdUtc: now, text: "original"), new Dictionary<string, string>()));
            byte[] validLog = File.ReadAllBytes(logPath);
            string conflictingOperation = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["operation"] = "upsert",
                ["record"] = new Dictionary<string, object?>
                {
                    ["event"] = new Dictionary<string, object?>
                    {
                        ["schemaVersion"] = 1,
                        ["eventId"] = "immutable",
                        ["peerId"] = PeerId,
                        ["senderDeviceId"] = "device-a",
                        ["createdUtc"] = now,
                        ["contentType"] = "text",
                        ["text"] = "conflicting",
                        ["files"] = Array.Empty<object>(),
                        ["state"] = "sent"
                    },
                    ["localFileReferences"] = new Dictionary<string, string>()
                },
                ["tombstone"] = null
            });
            File.AppendAllText(logPath, conflictingOperation + "\n", Encoding.UTF8);

            var reopened = new HistoryStore(logPath);
            HistorySyncEvent recovered = reopened.GetRecent(PeerId, now).Single().Event;
            Program.Check(recovered.Text == "original", "replay preserves original immutable event payload");
            Program.Check(File.ReadAllBytes(logPath).SequenceEqual(validLog), "conflicting trailing upsert is rejected and truncated");
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