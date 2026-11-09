using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClipBridge.Windows;

internal static class ClipboardSyncTests
{
    private sealed class Store(string id) : IDeviceStore
    {
        public string DeviceId => id;
        public string Name => "Synthetic clipboard test";
        private readonly List<DeviceTrust> records = new();
        public DeviceTrust[] Records() => records.ToArray();
        public void Save(DeviceTrust record) { records.RemoveAll(x => x.Id == record.Id); records.Add(record); }
        public void Remove(string id) => records.RemoveAll(x => x.Id == id);
    }
    public static async Task RunAsync()
    {
        var state = new ClipboardSyncState();
        Program.Check(!state.ShouldSend("local"), "automatic sync is off by default");
        state.Enable("peer-a");
        Program.Check(state.ShouldSend("local"), "opt-in permits the current text");
        Program.Check(!state.ShouldSend("local"), "duplicate notifications do not resend text");
        state.SuppressIncoming("received");
        Program.Check(state.IsSuppressed("received"), "remote clipboard update is consumed without echo");
        Program.Check(state.IsSuppressed("received"), "duplicate clipboard change callbacks remain suppressed");
        Program.Check(!state.IsSuppressed("different"), "a different clipboard value clears echo suppression");
        Program.Check(state.ShouldSend("other") && state.ShouldSend("local"), "a later copy of earlier text is a fresh change");
        Program.Check(!state.ShouldSend("") && !state.ShouldSend(new string('界', 100000)), "empty and oversized text do not sync");
        state.Disable();
        Program.Check(!state.ShouldSend("next") && state.TargetPeerId == null, "turning off clears the target and stops transmission");
        state.Enable("peer-b");
        Program.Check(state.ShouldSend("received"), "new explicit session does not inherit stale suppression");

        const string aId="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", bId="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var a=new Store(aId); var b=new Store(bId);
        byte[] key=RandomNumberGenerator.GetBytes(32), legacy=RandomNumberGenerator.GetBytes(32);
        a.Save(new(bId,b.Name,"android",key,"127.0.0.1")); b.Save(new(aId,a.Name,"windows",key,"127.0.0.1"));
        using var ah=new DeviceHub(a,legacy,0,false); using var bh=new DeviceHub(b,legacy,0,false);
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ah.Nearby[bId]=new(bId,b.Name,"android","127.0.0.1",now,true);
        bh.Nearby[aId]=new(aId,a.Name,"windows","127.0.0.1",now,true);
        using var first=new SyncNode(legacy, action=>action(),0,false,ah);
        using var second=new SyncNode(legacy, action=>action(),0,false,bh);
        var foreground=await first.RequestClipboardAsync(IPAddress.Loopback,bId,CancellationToken.None,second.Port);
        Program.Check(foreground.Status=="foreground", "clipboard pull requires an active foreground responder");
        Action<string, TaskCompletionSource<ClipboardReadResult>> phoneReader=(peer,answer)=>answer.TrySetResult(new ClipboardReadResult("ok","phone 中文🙂"));
        second.ClipboardRequested+=phoneReader;
        var phone=await first.RequestClipboardAsync(IPAddress.Loopback,bId,CancellationToken.None,second.Port);
        Program.Check(phone.Status=="ok" && phone.Text=="phone 中文🙂", "sync pulls peer text through the paired encrypted channel");
        second.ClipboardRequested-=phoneReader;
        Action<string, TaskCompletionSource<ClipboardReadResult>> desktopReader=(peer,answer)=>answer.TrySetResult(new ClipboardReadResult("ok","desktop"));
        first.ClipboardRequested+=desktopReader;
        var desktop=await second.RequestClipboardAsync(IPAddress.Loopback,aId,CancellationToken.None,first.Port);
        Program.Check(desktop.Text=="desktop", "clipboard requests work in both directions");
        first.ClipboardRequested-=desktopReader;
        Action<string, TaskCompletionSource<ClipboardReadResult>> emptyReader=(peer,answer)=>answer.TrySetResult(new ClipboardReadResult("empty"));
        second.ClipboardRequested+=emptyReader;
        Program.Check((await first.RequestClipboardAsync(IPAddress.Loopback,bId,CancellationToken.None,second.Port)).Status=="empty", "empty clipboard remains an error, not a clearing operation");
        using var response=JsonDocument.Parse(JsonSerializer.Serialize(ClipboardSyncProtocol.Response("request-1",bId,new ClipboardReadResult("ok","中文🙂"),now)));
        var parsedResponse = ClipboardSyncProtocol.ReadResponse(response,"request-1",bId,now);
        Program.Check(parsedResponse.Text=="中文🙂" && parsedResponse.MessageId=="request-1" && parsedResponse.TimestampUtc==now, "matching paired response carries text identity and event timestamp");
        Program.Rejects<InvalidDataException>(()=>ClipboardSyncProtocol.ReadResponse(response,"other-request",bId,now), "response for another request is rejected");
        Console.WriteLine("ALL CLIPBOARD SYNC TESTS PASSED");
    }
}
