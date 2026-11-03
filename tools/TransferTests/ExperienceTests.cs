using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClipBridge.Windows;

internal static class ExperienceTests
{
    private sealed class Store : IDeviceStore
    {
        public string DeviceId { get; } = Guid.NewGuid().ToString("N");
        public string Name => "Synthetic test device";
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string,DeviceTrust> records=new();
        public DeviceTrust[] Records()=>records.Values.ToArray();
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
        using var receiver=new SyncNode(legacy,x=>x(),0,false,bh); using var sender=new SyncNode(legacy,x=>x(),0,false,ah);
        string? received=null; receiver.RemoteTextReceived+=value=>received=value;
        await sender.SendTextAsync(System.Net.IPAddress.Loopback,"独立设备密钥文字",System.Threading.CancellationToken.None,receiver.Port);
        for(int n=0;n<100 && received==null;n++) await System.Threading.Tasks.Task.Delay(10);
        Program.Check(received=="独立设备密钥文字","production text channel uses authenticated per-device routing");
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
