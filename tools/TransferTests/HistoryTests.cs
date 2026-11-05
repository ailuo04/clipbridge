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
        Console.WriteLine("HISTORY TESTS PASSED");
    }

    private static void RetentionUsesUtcMilliseconds()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(FixedNowUtc).ToUnixTimeMilliseconds();
        var sixDaysTwentyThreeHoursFiftyNineMinutesAgo = now - TimeSpan.FromDays(6).Ticks / TimeSpan.TicksPerMillisecond - TimeSpan.FromHours(23).Ticks / TimeSpan.TicksPerMillisecond - TimeSpan.FromMinutes(59).Ticks / TimeSpan.TicksPerMillisecond;
        Program.Check(HistoryPolicy.IsVisible(sixDaysTwentyThreeHoursFiftyNineMinutesAgo, now), "six-day twenty-three-hour event visible");
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
    }
    private static void SyncContractUsesStableJsonNamesAndExcludesLocalPaths()
    {
        JsonElement entry = JsonSerializer.SerializeToElement(Event());
        foreach (string name in new[] { "schemaVersion", "eventId", "peerId", "senderDeviceId", "createdUtc", "contentType", "text", "files", "state" })
            Program.Check(entry.TryGetProperty(name, out _), "event JSON contains " + name);
        Program.Check(!entry.TryGetProperty("localFilePath", out _) && !entry.TryGetProperty("localFileUri", out _), "event JSON excludes local file paths and URIs");

        JsonElement file = JsonSerializer.SerializeToElement(new HistoryFileMetadata("demo.txt", 3, new string('a', 64)));
        foreach (string name in new[] { "name", "size", "sha256" })
            Program.Check(file.TryGetProperty(name, out _), "file JSON contains " + name);

        JsonElement tombstone = JsonSerializer.SerializeToElement(new HistoryTombstone("event-1", PeerId, FixedNowUtc));
        foreach (string name in new[] { "eventId", "peerId", "deletedUtc" })
            Program.Check(tombstone.TryGetProperty(name, out _), "tombstone JSON contains " + name);

        var page = new HistorySyncPage(1, "request-1", PeerId, 0, new[] { Event() }, new[] { new HistoryTombstone("event-1", PeerId, FixedNowUtc) });
        JsonElement pageJson = JsonSerializer.SerializeToElement(page);
        foreach (string name in new[] { "schemaVersion", "requestId", "peerId", "pageNumber", "entries", "tombstones" })
            Program.Check(pageJson.TryGetProperty(name, out _), "sync page JSON contains " + name);
        Program.Check(page.SchemaVersion == 1, "version-one sync page contract");
    }

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