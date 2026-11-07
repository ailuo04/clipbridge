using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows;
using ClipBridge.Windows.History;
internal static class ExperienceInterop
{
    private sealed class Store : IDeviceStore
    {
        public string DeviceId { get; }=new string('b',32);
        public string Name=>"Interop Windows";
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string,DeviceTrust> devices=new();
        public DeviceTrust[] Records()=>devices.Values.ToArray();
        public void Save(DeviceTrust record)=>devices[record.Id]=record;
        public void Remove(string id)=>devices.TryRemove(id,out _);
    }
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var store=new Store(); byte[] legacy=RandomNumberGenerator.GetBytes(32);
        using var hub=new DeviceHub(store,legacy,0,false);
        string androidId=new string('a',32);
        string historyRoot=Path.Combine(directory,"windows-history"); Directory.CreateDirectory(historyRoot);
        var history=new HistoryStore(Path.Combine(historyRoot,"history","events-v1.jsonl"));
        string androidLocalPath=Path.Combine(historyRoot,"android-local.bin"); await File.WriteAllTextAsync(androidLocalPath,"keep Android local bytes");
        string kotlinDeletedPath=Path.Combine(historyRoot,"kotlin-deleted.bin"); await File.WriteAllTextAsync(kotlinDeletedPath,"keep deleted Kotlin bytes");
        string windowsDeletedPath=Path.Combine(historyRoot,"windows-deleted.bin"); await File.WriteAllTextAsync(windowsDeletedPath,"keep deleted Windows bytes");
        long seededUtc=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seededFiles=new[]{new HistoryFileMetadata("shared.bin",23,new string('a',64))};
        history.Upsert(new HistoryLocalRecord(new HistorySyncEvent(1,"android-local",androidId,androidId,seededUtc,"file",null,seededFiles,"sent"),new System.Collections.Generic.Dictionary<string,string>{{"0",androidLocalPath}}));
        history.Upsert(new HistoryLocalRecord(new HistorySyncEvent(1,"kotlin-deleted",androidId,androidId,seededUtc,"file",null,seededFiles,"sent"),new System.Collections.Generic.Dictionary<string,string>{{"0",kotlinDeletedPath}}));
        history.Upsert(new HistoryLocalRecord(new HistorySyncEvent(1,"windows-deleted",androidId,store.DeviceId,seededUtc,"file",null,seededFiles,"sent"),new System.Collections.Generic.Dictionary<string,string>{{"0",windowsDeletedPath}}));
        history.DeleteRecord(androidId,"windows-deleted",seededUtc);
        using var text=new SyncNode(legacy,a=>a(),0,false,hub,history);
        string received=Path.Combine(directory,"received");
        using var files=new FileTransferNode(legacy,received,(_,_,_)=>Task.FromResult(true),0,hub,history);
        var textReceived=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); text.RemoteTextReceived+=value=>textReceived.TrySetResult(value);
        var control=new TcpListener(IPAddress.Loopback,0); control.Start();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"experience.json"),JsonSerializer.Serialize(new { id=store.DeviceId,pairPort=hub.PairingPort,textPort=text.Port,filePort=files.Port,controlPort=((IPEndPoint)control.LocalEndpoint).Port,historySeedUtc=seededUtc }));
            Console.WriteLine("EXPERIENCE INTEROP READY");
            var approval=Task.Run(async()=> {
                while(hub.Pending.IsEmpty) await Task.Delay(20,timeout.Token);
                var pending=hub.Pending.Values.Single();
                Program.Check(store.Records().Length==0,"interop trust waits for explicit confirmation");
                await File.WriteAllTextAsync(Path.Combine(directory,"pair-code.txt"),pending.Code,timeout.Token);
                while(!File.Exists(Path.Combine(directory,"approve-code"))) await Task.Delay(20,timeout.Token);
                hub.Decide(pending,true);
            });
            using var client=await control.AcceptTcpClientAsync(timeout.Token);
            byte[] ports=new byte[8]; await client.GetStream().ReadExactlyAsync(ports,timeout.Token);
            await approval;
            Program.Check(await textReceived.Task.WaitAsync(timeout.Token)=="JVM → Windows v2 中文🙂","production C# receives Kotlin per-device text");
            for(int n=0;n<200 && !File.Exists(Path.Combine(received,"来自手机v2.txt"));n++) await Task.Delay(20,timeout.Token);
            Program.Check(File.ReadAllText(Path.Combine(received,"来自手机v2.txt"))=="JVM v2 file 中文🙂","production C# receives Kotlin per-device file");
            var trust=store.Records().Single(t=>t.Active);
            hub.Nearby[trust.Id]=new(trust.Id,trust.Name,"android","127.0.0.1",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),true,DiscoveryProtocol.SupportedHistorySyncVersion,1);
            int textPort=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(ports.AsSpan(0,4));
            int filePort=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(ports.AsSpan(4,4));
            Program.Check(await text.SendTextAsync(IPAddress.Loopback,"Windows → JVM v2 中文🙂",timeout.Token,textPort),"production C# sends Kotlin per-device text");
            string source=Path.Combine(directory,"from-windows.txt"); await File.WriteAllTextAsync(source,"Windows v2 file 中文🙂",timeout.Token);
            await files.SendAsync(IPAddress.Loopback,new[]{source},timeout.Token,filePort);
            Program.Check(await text.SyncHistoryAsync(IPAddress.Loopback,trust.Id,textPort,timeout.Token),"C# synchronizes history over the authenticated Kotlin text route");
            long syncNow=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Program.Check(history.GetRecent(trust.Id,syncNow).Any(entry=>entry.Event.EventId=="android-local" && entry.LocalFileReferences.TryGetValue("0",out string? path) && path==androidLocalPath),"cross-language history sync preserves Windows-local paths");
            Program.Check(history.GetRecent(trust.Id,syncNow).All(entry=>entry.Event.EventId!="kotlin-deleted" && entry.Event.EventId!="windows-deleted"),"cross-language tombstones remove matching records");
            Program.Check(File.ReadAllText(kotlinDeletedPath)=="keep deleted Kotlin bytes" && File.ReadAllText(windowsDeletedPath)=="keep deleted Windows bytes","cross-language history deletion preserves local file bytes");
            Console.WriteLine("HISTORY SYNC INTEROP PASSED");
            Console.WriteLine("PAIRING AND V2 BIDIRECTIONAL INTEROP PASSED");
        }
        finally { control.Stop(); }
    }
}
