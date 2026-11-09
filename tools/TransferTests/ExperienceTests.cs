using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClipBridge.Windows;
using ClipBridge.Windows.History;

internal static class ExperienceTests
{
    private sealed class Store : IDeviceStore
    {
        public string DeviceId { get; } = Guid.NewGuid().ToString("N");
        public string Name => "Synthetic test device";
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string,DeviceTrust> records=new();
        public Action? BeforeRecords { get; set; }
        public DeviceTrust[] Records() { BeforeRecords?.Invoke(); return records.Values.ToArray(); }
        public void Save(DeviceTrust record)=>records[record.Id]=record;
        public void Remove(string id)=>records.TryRemove(id,out _);
    }
    public static async System.Threading.Tasks.Task PairingRoundtripAsync()
    {
        var a=new Store(); var b=new Store(); byte[] legacy=RandomNumberGenerator.GetBytes(32);
        using var ah=new DeviceHub(a,legacy,0,false); using var bh=new DeviceHub(b,legacy,0,false);
        int aNotified=0,bNotified=0; ah.PairingRequested+=()=>System.Threading.Interlocked.Increment(ref aNotified); bh.PairingRequested+=()=>System.Threading.Interlocked.Increment(ref bNotified);
        var peer=new NearbyDevice(b.DeviceId,b.Name,"windows","127.0.0.1",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),false);
        var task=ah.PairAsync(peer,bh.PairingPort);
        for(int n=0;n<250 && (ah.Pending.IsEmpty || bh.Pending.IsEmpty);n++) await System.Threading.Tasks.Task.Delay(20);
        Program.Check(ah.Pending.Count==1 && bh.Pending.Count==1,"both pairing coordinators expose explicit pending decisions");
        Program.Check(aNotified==1 && bNotified==1,"both devices notify the UI when a confirmation becomes available");
        var ar=ah.Pending.Values.Single(); var br=bh.Pending.Values.Single();
        Program.Check(ar.Code==br.Code && a.Records().Length==0 && b.Records().Length==0,"no trust before user verification on either endpoint");
        ah.Decide(ar,true); await System.Threading.Tasks.Task.Delay(80);
        Program.Check(a.Records().Length==0 && b.Records().Length==0,"one user confirmation cannot establish trust");
        bh.Decide(br,true); await task.WaitAsync(TimeSpan.FromSeconds(5));
        for(int n=0;n<100 && !b.Records().Any(t=>t.Active);n++) await System.Threading.Tasks.Task.Delay(10);
        Program.Check(a.Records().Single().Active && b.Records().Single().Active && a.Records().Single().Key.SequenceEqual(b.Records().Single().Key),"two explicit confirmations commit the same independent device key");
        ah.Nearby[b.DeviceId]=peer with { Authenticated=true,Seen=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        bh.Nearby[a.DeviceId]=new(a.DeviceId,a.Name,"windows","127.0.0.1",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),true);

        string historyRoot=Path.Combine(Path.GetTempPath(),"clipbridge-text-history-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(historyRoot);
        try
        {
            var aHistory=new HistoryStore(Path.Combine(historyRoot,"a","history","events-v1.jsonl"));
            var bHistory=new HistoryStore(Path.Combine(historyRoot,"b","history","events-v1.jsonl"));

            using var receiver=new SyncNode(legacy,x=>x(),0,false,bh,bHistory); using var sender=new SyncNode(legacy,x=>x(),0,false,ah,aHistory);
            HistoryTests.AssertNoTextHistoryBeforeExplicitAction(aHistory,b.DeviceId,bHistory,a.DeviceId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Program.Check(!await sender.SendTextAsync(System.Net.IPAddress.Loopback,"failed-send-must-not-be-recorded",System.Threading.CancellationToken.None,0),
                "failed text send reports failure");
            Program.Check(aHistory.GetRecent(b.DeviceId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Count == 0 &&
                bHistory.GetRecent(a.DeviceId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Count == 0,
                "failed text send creates no sender or receiver history row");
            string? received=null; receiver.RemoteTextReceived+=value=>received=value;
            const string sentText="独立设备密钥文字";
            long sendStarted=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await sender.SendTextAsync(System.Net.IPAddress.Loopback,sentText,System.Threading.CancellationToken.None,receiver.Port);
            long sendCompleted=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for(int n=0;n<100 && (received==null || aHistory.GetRecent(b.DeviceId,sendCompleted).Count==0 || bHistory.GetRecent(a.DeviceId,sendCompleted).Count==0);n++) await System.Threading.Tasks.Task.Delay(10);
            Program.Check(received==sentText,"production text channel uses authenticated per-device routing");
            HistoryTests.AssertPairedTextHistory(aHistory,a.DeviceId,b.DeviceId,bHistory,sentText,sendStarted,sendCompleted);
            await HistorySyncRoundtripAsync(ah,bh,aHistory,bHistory,sender,receiver,a.DeviceId,b.DeviceId,legacy,historyRoot);
            await FileHistoryRoundtripsAsync(ah,bh,a.DeviceId,b.DeviceId,legacy,historyRoot);
        }
        finally { if(Directory.Exists(historyRoot) && Path.GetFullPath(historyRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(historyRoot,true); }
        await SenderHistoryFollowsAuthenticatedRouteAsync(ah,a,b,peer,legacy);
        string legacyHistoryRoot=Path.Combine(Path.GetTempPath(),"clipbridge-legacy-history-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(legacyHistoryRoot);
        try
        {
            var legacySenderStore=new Store(); var legacyReceiverStore=new Store();
            using var legacySenderHub=new DeviceHub(legacySenderStore,legacy,0,false);
            using var legacyReceiverHub=new DeviceHub(legacyReceiverStore,legacy,0,false);
            string legacySenderLog=Path.Combine(legacyHistoryRoot,"sender","history","events-v1.jsonl");
            string legacyReceiverLog=Path.Combine(legacyHistoryRoot,"receiver","history","events-v1.jsonl");
            var legacySenderHistory=new HistoryStore(legacySenderLog); var legacyReceiverHistory=new HistoryStore(legacyReceiverLog);
            using var legacyReceiver=new SyncNode(legacy,x=>x(),0,false,legacyReceiverHub,legacyReceiverHistory);
            using var legacySender=new SyncNode(legacy,x=>x(),0,false,legacySenderHub,legacySenderHistory);
            string? legacyReceived=null; legacyReceiver.RemoteTextReceived+=value=>legacyReceived=value;
            Program.Check(await legacySender.SendTextAsync(System.Net.IPAddress.Loopback,"legacy-no-history",System.Threading.CancellationToken.None,legacyReceiver.Port),
                "unpaired legacy route continues to send text");
            for(int n=0;n<100 && legacyReceived==null;n++) await System.Threading.Tasks.Task.Delay(10);
            Program.Check(legacyReceived=="legacy-no-history","unpaired legacy route receives text");
            Program.Check(!File.Exists(legacySenderLog) && !File.Exists(legacyReceiverLog),
                "unpaired legacy route creates no sender or receiver history row");
        }
        finally { if(Directory.Exists(legacyHistoryRoot) && Path.GetFullPath(legacyHistoryRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(legacyHistoryRoot,true); }
        string sandbox=Path.Combine(Path.GetTempPath(),"clipbridge-preface-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sandbox);
        try
        {
            using var filesIn=new FileTransferNode(legacy,Path.Combine(sandbox,"received"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,bh);
            using var filesOut=new FileTransferNode(legacy,Path.Combine(sandbox,"unused"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,ah);
            using var stalled1=new System.Net.Sockets.TcpClient(); using var stalled2=new System.Net.Sockets.TcpClient();
            await stalled1.ConnectAsync(System.Net.IPAddress.Loopback,filesIn.Port); await stalled2.ConnectAsync(System.Net.IPAddress.Loopback,filesIn.Port);
            await System.Threading.Tasks.Task.Delay(6000);
            string source=Path.Combine(sandbox,"source.txt"); await File.WriteAllTextAsync(source,"bounded handshake");
            using var limit=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            await filesOut.SendAsync(System.Net.IPAddress.Loopback,new[]{source},limit.Token,filesIn.Port);
            Program.Check(File.ReadAllText(Path.Combine(sandbox,"received","source.txt"))=="bounded handshake","stalled routing prefaces release file receive slots before starving valid peers");
        }
        finally { if(Path.GetFullPath(sandbox).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(sandbox,true); }
        using var wrong=new System.IO.MemoryStream();
        var raw=TrustedChannel.Header(a.DeviceId,b.DeviceId,1,a.Records().Single().Key); raw[4]=2;
        Program.Rejects<CryptographicException>(()=>TrustedChannel.Open(raw,b.DeviceId,1,a.Records().Single().Key),"route kind tampering rejected");
        ah.Revoke(b.DeviceId);
        Program.Check(a.Records().Length==0,"revocation deletes only the selected trust record");
        // Refusal is exercised on a separate coordinator pair, using no UI or real clipboard.
        var c=new Store(); var d=new Store(); using var ch=new DeviceHub(c,legacy,0,false); using var dh=new DeviceHub(d,legacy,0,false);
        var refused=ch.PairAsync(new(d.DeviceId,d.Name,"windows","127.0.0.1",0,false),dh.PairingPort);
        for(int n=0;n<250 && (ch.Pending.IsEmpty || dh.Pending.IsEmpty);n++) await System.Threading.Tasks.Task.Delay(20);
        Program.Check(!ch.Pending.IsEmpty && !dh.Pending.IsEmpty,"refusal session reaches confirmation step");
        ch.Decide(ch.Pending.Values.Single(),true); dh.Decide(dh.Pending.Values.Single(),false);
        await refused.WaitAsync(TimeSpan.FromSeconds(5));
        Program.Check(c.Records().Length==0 && d.Records().Length==0,"a refused pairing cannot persist trust");
    }
    private static async System.Threading.Tasks.Task HistorySyncRoundtripAsync(DeviceHub senderHub, DeviceHub receiverHub, HistoryStore senderHistory, HistoryStore receiverHistory, SyncNode sender, SyncNode receiver, string senderDeviceId, string receiverDeviceId, byte[] legacy, string root)
    {
        senderHub.Nearby[receiverDeviceId]=senderHub.Nearby[receiverDeviceId] with { HistorySyncVersion=1,ConnectionEpoch=1 };
        receiverHub.Nearby[senderDeviceId]=receiverHub.Nearby[senderDeviceId] with { HistorySyncVersion=1,ConnectionEpoch=1 };
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string senderLocalFile=Path.Combine(root,"history-sync-sender-local.bin"); await File.WriteAllTextAsync(senderLocalFile,"sender local bytes");
        string receiverLocalFile=Path.Combine(root,"history-sync-receiver-local.bin"); await File.WriteAllTextAsync(receiverLocalFile,"receiver local bytes");
        var fileMetadata=new[]{new HistoryFileMetadata("sync.txt",17,new string('a',64))};
        var senderFileEvent=new HistorySyncEvent(1,"sync-a-file",receiverDeviceId,senderDeviceId,now,"file",null,fileMetadata,"sent");
        var receiverFileEvent=new HistorySyncEvent(1,"sync-b-file",senderDeviceId,receiverDeviceId,now,"file",null,fileMetadata,"sent");
        senderHistory.Upsert(new HistoryLocalRecord(senderFileEvent,new System.Collections.Generic.Dictionary<string,string>{{"0",senderLocalFile}}));
        receiverHistory.Upsert(new HistoryLocalRecord(receiverFileEvent,new System.Collections.Generic.Dictionary<string,string>{{"0",receiverLocalFile}}));
        var deletedEvent=new HistorySyncEvent(1,"sync-deleted",receiverDeviceId,senderDeviceId,now,"file",null,fileMetadata,"sent");
        string deletedLocalFile=Path.Combine(root,"history-sync-deleted-local.bin"); await File.WriteAllTextAsync(deletedLocalFile,"preserve deleted history bytes");
        senderHistory.Upsert(new HistoryLocalRecord(deletedEvent,new System.Collections.Generic.Dictionary<string,string>{{"0",deletedLocalFile}}));
        receiverHistory.Upsert(new HistoryLocalRecord(deletedEvent with { PeerId=senderDeviceId },new System.Collections.Generic.Dictionary<string,string>{{"0",deletedLocalFile}}));
        senderHistory.DeleteRecord(receiverDeviceId,deletedEvent.EventId,now);

        Program.Check(await sender.SyncHistoryAsync(System.Net.IPAddress.Loopback,receiverDeviceId,receiver.Port,System.Threading.CancellationToken.None),"authenticated history synchronization completes in both directions");
        HistoryLocalRecord receivedAtReceiver=receiverHistory.GetRecent(senderDeviceId,now).Single(record=>record.Event.EventId==senderFileEvent.EventId);
        HistoryLocalRecord receivedAtSender=senderHistory.GetRecent(receiverDeviceId,now).Single(record=>record.Event.EventId==receiverFileEvent.EventId);
        Program.Check(receivedAtReceiver.Event.Files.SequenceEqual(fileMetadata) && receivedAtSender.Event.Files.SequenceEqual(fileMetadata),"bidirectional sync merges file metadata");
        HistoryLocalRecord senderLocalRecord=senderHistory.GetRecent(receiverDeviceId,now).Single(record=>record.Event.EventId==senderFileEvent.EventId);
        Program.Check(receivedAtReceiver.LocalFileReferences.Count==0 && receivedAtSender.LocalFileReferences.Count==0 && senderLocalRecord.LocalFileReferences.TryGetValue("0",out string? retainedPath) && retainedPath==senderLocalFile,"sync never copies remote file references and preserves the local reference");
        Program.Check(receiverHistory.GetRecent(senderDeviceId,now).All(record=>record.Event.EventId!=deletedEvent.EventId) && File.ReadAllText(deletedLocalFile)=="preserve deleted history bytes","synced tombstones delete only history and preserve file bytes");
        int senderCount=senderHistory.GetRecent(receiverDeviceId,now).Count; int receiverCount=receiverHistory.GetRecent(senderDeviceId,now).Count;
        Program.Check(await sender.SyncHistoryAsync(System.Net.IPAddress.Loopback,receiverDeviceId,receiver.Port,System.Threading.CancellationToken.None),"retrying an acknowledged history sync succeeds");
        Program.Check(senderHistory.GetRecent(receiverDeviceId,now).Count==senderCount && receiverHistory.GetRecent(senderDeviceId,now).Count==receiverCount,"history sync retry is idempotent");

        using var stalledListener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); stalledListener.Start();
        int stalledPort=((System.Net.IPEndPoint)stalledListener.LocalEndpoint).Port;
        var stallTask=System.Threading.Tasks.Task.Run(async()=> { using var stalled=await stalledListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)); byte[] header=new byte[TrustedChannel.HeaderSize]; await stalled.GetStream().ReadExactlyAsync(header); byte[] length=new byte[4]; await stalled.GetStream().ReadExactlyAsync(length); int size=SharedProtocol.ReadFrameLength(length); byte[] frame=new byte[size]; await stalled.GetStream().ReadExactlyAsync(frame); await System.Threading.Tasks.Task.Delay(500); });
        using(var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            Program.Check(!await sender.SyncHistoryAsync(System.Net.IPAddress.Loopback,receiverDeviceId,stalledPort,timeout.Token),"history sync timeout is isolated and reported as incomplete");
        await stallTask;
        string? textAfterTimeout=null; receiver.RemoteTextReceived+=value=>textAfterTimeout=value;
        Program.Check(await sender.SendTextAsync(System.Net.IPAddress.Loopback,"after-history-sync-timeout",System.Threading.CancellationToken.None,receiver.Port),"ordinary text send remains available after history sync timeout");
        for(int n=0;n<100 && textAfterTimeout==null;n++) await System.Threading.Tasks.Task.Delay(10);
        Program.Check(textAfterTimeout=="after-history-sync-timeout","text receive remains available after history sync timeout");
    }

    private static async System.Threading.Tasks.Task FileHistoryRoundtripsAsync(DeviceHub senderHub, DeviceHub receiverHub, string senderDeviceId, string receiverDeviceId, byte[] legacy, string historyRoot)
    {
        string root=Path.Combine(historyRoot,"file-transfers"); Directory.CreateDirectory(root);
        string source=Path.Combine(root,"source"); Directory.CreateDirectory(source);
        string acceptedOutput=Path.Combine(root,"accepted");
        string sourceFile=Path.Combine(source,"accepted.txt"); await File.WriteAllTextAsync(sourceFile,"verified file bytes");
        var senderHistory=new HistoryStore(Path.Combine(root,"accepted-sender","history","events-v1.jsonl"));
        var receiverHistory=new HistoryStore(Path.Combine(root,"accepted-receiver","history","events-v1.jsonl"));
        using(var receiver=new FileTransferNode(legacy,acceptedOutput,(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,receiverHub,receiverHistory))
        using(var sender=new FileTransferNode(legacy,Path.Combine(root,"unused"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,senderHub,senderHistory))
        {
            long started=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await sender.SendAsync(System.Net.IPAddress.Loopback,new[]{sourceFile},System.Threading.CancellationToken.None,receiver.Port);
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for(int n=0;n<100 && receiverHistory.GetRecent(senderDeviceId,now).Count==0;n++) await System.Threading.Tasks.Task.Delay(10);
            HistoryLocalRecord sent=senderHistory.GetRecent(receiverDeviceId,now).Single();
            HistoryLocalRecord received=receiverHistory.GetRecent(senderDeviceId,now).Single();
            Program.Check(sent.Event.EventId==received.Event.EventId && sent.Event.EventId.Length==32,"accepted file records retain the original transfer ID on both peers");
            Program.Check(sent.Event.CreatedUtc==received.Event.CreatedUtc && sent.Event.CreatedUtc>=started && sent.Event.CreatedUtc<=now && sent.Event.SenderDeviceId==senderDeviceId && received.Event.SenderDeviceId==senderDeviceId,"accepted file records preserve original offer timestamp and sender identity");
            Program.Check(sent.Event.ContentType=="file" && sent.Event.State=="sent" && sent.Event.Files.SequenceEqual(received.Event.Files) && sent.Event.Files.Single().Name=="accepted.txt" && sent.Event.Files.Single().Size==new FileInfo(sourceFile).Length && sent.Event.Files.Single().Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceFile))),"accepted file records share validated metadata");
            string savedPath=Path.Combine(acceptedOutput,"accepted.txt");
            Program.Check(received.LocalFileReferences.TryGetValue("0",out string? localPath) && localPath==savedPath,"received file history keeps its committed local path outside the sync event");
            string wireJson=System.Text.Json.JsonSerializer.Serialize(received.Event);
            Program.Check(!wireJson.Contains(savedPath,StringComparison.Ordinal) && !wireJson.Contains("localFileReferences",StringComparison.OrdinalIgnoreCase),"file history sync event never serializes local paths");
            receiverHistory.DeleteRecord(senderDeviceId,received.Event.EventId,now);
            Program.Check(File.Exists(savedPath) && File.ReadAllText(savedPath)=="verified file bytes","deleting a received file history record preserves local file bytes");
        }

        int consentRequests=0;
        string rejectedOutput=Path.Combine(root,"rejected");
        var rejectedSenderHistory=new HistoryStore(Path.Combine(root,"rejected-sender","history","events-v1.jsonl"));
        var rejectedReceiverHistory=new HistoryStore(Path.Combine(root,"rejected-receiver","history","events-v1.jsonl"));
        using(var receiver=new FileTransferNode(legacy,rejectedOutput,(_,_,_)=>{ consentRequests++; return System.Threading.Tasks.Task.FromResult(false); },0,receiverHub,rejectedReceiverHistory))
        using(var sender=new FileTransferNode(legacy,Path.Combine(root,"unused-reject"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,senderHub,rejectedSenderHistory))
        {
            bool rejected=false;
            try { await sender.SendAsync(System.Net.IPAddress.Loopback,new[]{sourceFile},System.Threading.CancellationToken.None,receiver.Port); }
            catch(IOException) { rejected=true; }
            Program.Check(!rejected && consentRequests==0,"paired files arrive without requesting consent");
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for(int n=0;n<100 && rejectedReceiverHistory.GetRecent(senderDeviceId,now).Count==0;n++) await System.Threading.Tasks.Task.Delay(10);
            HistoryLocalRecord senderRecord=rejectedSenderHistory.GetRecent(receiverDeviceId,now).Single();
            HistoryLocalRecord receiverRecord=rejectedReceiverHistory.GetRecent(senderDeviceId,now).Single();
            Program.Check(senderRecord.Event.EventId==receiverRecord.Event.EventId && senderRecord.Event.State=="sent" && receiverRecord.Event.State=="sent","automatic receive records the same completed event on both peers");
            Program.Check(File.ReadAllText(Path.Combine(rejectedOutput,"accepted.txt"))=="verified file bytes","automatic receive saves verified bytes");
        }

        string failedOutput=Path.Combine(root,"failed"); Directory.CreateDirectory(failedOutput);
        using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start();
        int failedPort=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var failedSenderHistory=new HistoryStore(Path.Combine(root,"failed-sender","history","events-v1.jsonl"));
        using(var sender=new FileTransferNode(legacy,Path.Combine(root,"unused-failed"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,senderHub,failedSenderHistory))
        {
            var closePeer=System.Threading.Tasks.Task.Run(async()=> { using var accepted=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)); });
            bool failed=false;
            try { await sender.SendAsync(System.Net.IPAddress.Loopback,new[]{sourceFile},System.Threading.CancellationToken.None,failedPort); }
            catch(Exception ex) when(ex is IOException or System.Net.Sockets.SocketException) { failed=true; }
            await closePeer;
            Program.Check(failed,"file transport interruption remains a failed send result");
            Program.Check(failedSenderHistory.GetRecent(receiverDeviceId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Single().Event.State=="failed","file transport failure is retained on the endpoint that observed it");
        }

        string historyFailureOutput=Path.Combine(root,"history-failure");
        string invalidLogPath=Path.Combine(root,"history-store-is-a-directory"); Directory.CreateDirectory(invalidLogPath);
        var unavailableHistory=new HistoryStore(invalidLogPath);
        using(var receiver=new FileTransferNode(legacy,historyFailureOutput,(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,receiverHub,unavailableHistory))
        using(var sender=new FileTransferNode(legacy,Path.Combine(root,"unused-history-failure"),(_,_,_)=>System.Threading.Tasks.Task.FromResult(true),0,senderHub))
        {
            await sender.SendAsync(System.Net.IPAddress.Loopback,new[]{sourceFile},System.Threading.CancellationToken.None,receiver.Port);
            Program.Check(File.ReadAllText(Path.Combine(historyFailureOutput,"accepted.txt"))=="verified file bytes","history persistence failure does not change verified file transfer success");
        }
    }
    private static async System.Threading.Tasks.Task SenderHistoryFollowsAuthenticatedRouteAsync(DeviceHub senderHub, Store senderStore, Store originalPeerStore, NearbyDevice originalPeer, byte[] legacy)
    {
        string alternatePeerId=Guid.NewGuid().ToString("N");
        byte[] alternateKey=RandomNumberGenerator.GetBytes(32);
        senderStore.Save(new DeviceTrust(alternatePeerId,"Alternate synthetic peer","windows",alternateKey,"127.0.0.1",true));
        senderHub.Nearby.Clear();
        senderHub.Nearby[originalPeer.Id]=originalPeer with { Authenticated=true,Seen=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        string root=Path.Combine(Path.GetTempPath(),"clipbridge-route-history-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);
        listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        string logPath=Path.Combine(root,"history","events-v1.jsonl");
        try
        {
            var history=new HistoryStore(logPath);
            using var sender=new SyncNode(legacy,x=>x(),0,false,senderHub,history);
            int switched=0;
            senderStore.BeforeRecords=() =>
            {
                if(System.Threading.Interlocked.CompareExchange(ref switched,1,0)!=0) return;
                senderHub.Nearby.Clear();
                senderHub.Nearby[alternatePeerId]=new(alternatePeerId,"Alternate synthetic peer","windows","127.0.0.1",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),true);
            };
            async System.Threading.Tasks.Task<byte[]> CaptureHeaderAsync()
            {
                using var accepted=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var stream=accepted.GetStream();
                byte[] header=new byte[TrustedChannel.HeaderSize]; await stream.ReadExactlyAsync(header);
                byte[] lengthBytes=new byte[4]; await stream.ReadExactlyAsync(lengthBytes);
                int length=SharedProtocol.ReadFrameLength(lengthBytes);
                byte[] frame=new byte[length]; await stream.ReadExactlyAsync(frame);
                return header;
            }
            var capture=CaptureHeaderAsync();
            bool sent=await sender.SendTextAsync(System.Net.IPAddress.Loopback,"route-race",System.Threading.CancellationToken.None,port);
            byte[] writtenHeader=await capture;
            senderStore.BeforeRecords=null;
            string routePeerId=Convert.ToHexString(writtenHeader.AsSpan(21,16)).ToLowerInvariant();
            DeviceTrust routeTrust=senderStore.Records().Single(record=>record.Id==routePeerId);
            Program.Check(TrustedChannel.Source(writtenHeader)==senderStore.DeviceId && TrustedChannel.Open(writtenHeader,routePeerId,1,routeTrust.Key).Length==32,
                "captured outbound header authenticates its exact route peer");
            Program.Check(sent,"route-attribution test frame was written successfully");
            Program.Check(switched==1,"route-attribution test changed the nearby peer between prelookup and header selection");
            Program.Check(routePeerId==originalPeerStore.DeviceId || routePeerId==alternatePeerId,"outbound header selected one of the test's trusted peers");
            HistoryLocalRecord[] records=history.GetRecent(originalPeerStore.DeviceId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Concat(history.GetRecent(alternatePeerId,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).ToArray();
            Program.Check(records.Length==1 && records[0].Event.PeerId==routePeerId,
                "sender history peer matches the peer ID in the authenticated outgoing header");
        }
        finally
        {
            senderStore.BeforeRecords=null;
            senderHub.Nearby.Clear();
            senderHub.Nearby[originalPeer.Id]=originalPeer with { Authenticated=true,Seen=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            senderStore.Remove(alternatePeerId);
            if(Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(root,true);
        }
    }
    public static async System.Threading.Tasks.Task MalformedDiscoveryAsync()
    {
        var store=new Store(); using var hub=new DeviceHub(store,RandomNumberGenerator.GetBytes(32),0,false);
        var field=typeof(DeviceHub).GetField("_udp",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var socket=(System.Net.Sockets.UdpClient)field.GetValue(hub)!;
        int port=((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port;
        var method=typeof(DeviceHub).GetMethod("ReceiveAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var receive=(System.Threading.Tasks.Task)method.Invoke(hub,null)!;
        using var sender=new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
        var stale=DiscoveryProtocol.Encode(DiscoveryProtocol.Create(Guid.NewGuid().ToString("N"),"Synthetic device","android") with { Time=1 });
        await sender.SendAsync(stale,new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,port));
        await System.Threading.Tasks.Task.Delay(250);
        Program.Check(!receive.IsCompleted,"a malformed discovery packet does not terminate future searches");
        hub.Dispose();
        await receive.WaitAsync(TimeSpan.FromSeconds(3));
    }
    public static async System.Threading.Tasks.Task PairingIdentityMismatchAsync()
    {
        var a=new Store(); var b=new Store(); byte[] legacy=RandomNumberGenerator.GetBytes(32);
        using var ah=new DeviceHub(a,legacy,0,false); using var bh=new DeviceHub(b,legacy,0,false);
        string wrongId=Guid.NewGuid().ToString("N");
        var task=ah.PairAsync(new NearbyDevice(wrongId,"Synthetic peer","windows","127.0.0.1",0,false),bh.PairingPort);
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Program.Check(ah.Pending.IsEmpty && bh.Pending.IsEmpty && a.Records().Length==0 && b.Records().Length==0,
            "a QR-pinned identity mismatch cannot display a code or establish trust");
    }
    public static async System.Threading.Tasks.Task SessionCollisionAsync()
    {
        var store=new Store(); using var hub=new DeviceHub(store,RandomNumberGenerator.GetBytes(32),0,false);
        string session=Guid.NewGuid().ToString("N");
        async System.Threading.Tasks.Task<System.Net.Sockets.TcpClient> Begin(string id)
        {
            var client=new System.Net.Sockets.TcpClient(); await client.ConnectAsync(System.Net.IPAddress.Loopback,hub.PairingPort);
            using var material=new PairingMaterial(); var stream=client.GetStream();
            using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            async System.Threading.Tasks.Task Write(object value) => await stream.WriteAsync(SharedProtocol.Frame(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)),timeout.Token);
            async System.Threading.Tasks.Task<System.Text.Json.JsonElement> Read()
            {
                byte[] h=new byte[4]; await stream.ReadExactlyAsync(h,timeout.Token); int n=SharedProtocol.ReadFrameLength(h); if(n<2 || n>4096) throw new IOException();
                byte[] data=new byte[n]; await stream.ReadExactlyAsync(data,timeout.Token); using var json=System.Text.Json.JsonDocument.Parse(data); return json.RootElement.Clone();
            }
            try
            {
                await Write(new { Type="hello",Session=session,Id=id,Name="Synthetic peer",Platform="android",Commit=PairingProtocol.Commitment(session,"i",id,"Synthetic peer",material.PublicKey,material.Nonce) });
                await Read(); await Write(new { Type="reveal",Public=material.PublicKey,Nonce=material.Nonce }); await Read();
                return client;
            } catch { client.Dispose(); throw; }
        }
        using var original=await Begin(Guid.NewGuid().ToString("N"));
        for(int n=0;n<100 && !hub.Pending.ContainsKey(session);n++) await System.Threading.Tasks.Task.Delay(10);
        var request=hub.Pending[session];
        await System.Threading.Tasks.Task.Delay(5200); // Respect the per-address rate limit, keeping the legitimate session alive.
        bool rejected=false;
        using var attackerLifetime=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { using var attacker=await Begin(Guid.NewGuid().ToString("N")); }
        catch(IOException) { rejected=true; }
        await System.Threading.Tasks.Task.Delay(50);
        Program.Check(rejected && hub.Pending.TryGetValue(session,out var current) && ReferenceEquals(request,current),"duplicate live session ID is rejected without replacing or deleting the displayed confirmation");
    }
    public static void Run()
    {
        string i = new string('1', 32), r = new string('2', 32), session = new string('3', 32);
        var sessions=new PairingSessions<object>(); var owner=new object(); var attacker=new object(); var original=new object(); var replacement=new object();
        Program.Check(sessions.Reserve(session,owner),"session owner is reserved before publishing a code");
        sessions.Publish(session,owner,original);
        Program.Check(!sessions.Reserve(session,attacker),"duplicate session ownership cannot be acquired");
        sessions.Release(session,attacker,replacement);
        Program.Check(sessions.IsCurrent(session,original),"losing session cleanup cannot remove another request");
        sessions.Release(session,owner,original); Program.Check(sessions.Reserve(session,attacker),"finished session ID can only be reused after ownership release");
        sessions.Publish(session,attacker,replacement);
        Program.Check(!sessions.IsCurrent(session,original) && sessions.IsCurrent(session,replacement),"stale UI approval cannot resolve a replacement request");

        using var a = new PairingMaterial(); using var b = new PairingMaterial();
        string commitment = PairingProtocol.Commitment(session,"i",i,"手机",a.PublicKey,a.Nonce);
        Program.Rejects<CryptographicException>(() => PairingProtocol.Verify(commitment, new string('0',64)), "changed pairing commitment rejected");
        byte[] transcript = PairingProtocol.Transcript(session,i,"手机",a.PublicKey,a.Nonce,r,"电脑",b.PublicKey,b.Nonce);
        byte[] ak = PairingProtocol.SessionKey(a,b.PublicKey,transcript), bk = PairingProtocol.SessionKey(b,a.PublicKey,transcript);
        Program.Check(ak.SequenceEqual(bk) && PairingProtocol.Code(ak,transcript) == PairingProtocol.Code(bk,transcript), "both peers derive identical pairing key and six digit code");
        Program.Check(PairingProtocol.Code(ak,transcript).Length == 6, "pairing code retains leading zeros");
        byte[] key = Enumerable.Range(0,32).Select(n => (byte)n).ToArray();
        byte[] header = TrustedChannel.Header(i,r,1,key);
        Program.Check(TrustedChannel.Open(header,r,1,key).Length == 32, "paired session authenticates routing header");
        Program.Rejects<CryptographicException>(() => TrustedChannel.Open(header,i,1,key), "different target cannot accept paired session");
        Program.Rejects<CryptographicException>(() => TrustedChannel.Open(header,r,2,key), "file and text session cannot be confused");
        header[5] ^= 1;
        Program.Rejects<CryptographicException>(() => TrustedChannel.Open(header,r,1,key), "forged sender rejected by AEAD");
        var packet = DiscoveryProtocol.Create(i,"手机","android");
        Program.Check(DiscoveryProtocol.Parse(DiscoveryProtocol.Encode(packet)).Id == i, "unpaired devices can be discovered without any key");
        Program.Check(!DiscoveryProtocol.Authenticate(packet,r,key), "public discovery cannot establish trust");
        var presence = DiscoveryProtocol.Create(i,"手机","android",r,key);
        Program.Check(DiscoveryProtocol.Authenticate(DiscoveryProtocol.Parse(DiscoveryProtocol.Encode(presence)),r,key), "paired presence requires matching key");
        Program.Check(!DiscoveryProtocol.Authenticate(presence with { Name = "forged" },r,key), "device name and route are authenticated");
        Program.Rejects<InvalidDataException>(() => DiscoveryProtocol.Parse(DiscoveryProtocol.Encode(packet with { Time = 1 })), "stale discovery rejected");
    }
}
