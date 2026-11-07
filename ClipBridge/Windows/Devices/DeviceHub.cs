using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;

namespace ClipBridge.Windows;
internal sealed record DeviceTrust(string Id, string Name, string Platform, byte[] Key, string Address, bool Active = true);
internal interface IDeviceStore
{
    string DeviceId { get; }
    string Name { get; }
    DeviceTrust[] Records();
    void Save(DeviceTrust record);
    void Remove(string id);
}
internal sealed record NearbyDevice(string Id, string Name, string Platform, string Address, long Seen, bool Authenticated, int HistorySyncVersion = 0, long ConnectionEpoch = 0);
internal sealed record PairingRequest(string Session, string Id, string Name, string Address, string Code, DateTimeOffset Expires, TaskCompletionSource<bool> Decision);
internal sealed record SessionRoute(byte[] Key, string? Source, byte[]? Prefix);
internal sealed record OutgoingSessionRoute(byte[] Key, string? PeerId);

internal sealed class DeviceHub : IDisposable
{
    private readonly IDeviceStore _store;
    private readonly byte[] _legacy;
    private readonly UdpClient _udp;
    private readonly TcpListener _pairing;
    private readonly CancellationTokenSource _stop = new();
    private readonly DiscoveryWake _wake = new();
    private readonly SemaphoreSlim _slots = new(4);
    private readonly ConcurrentDictionary<string,long> _replay = new(), _pairRate = new(), _peerEpochs = new();
    private readonly ConcurrentDictionary<TcpClient,byte> _clients = new();
    private readonly ConcurrentDictionary<string,byte> _pairIds = new();
    private readonly object _trustGate = new();
    private readonly ConcurrentDictionary<string,int> _epochs = new();
    public readonly ConcurrentDictionary<string,NearbyDevice> Nearby = new();
    private readonly PairingSessions<PairingRequest> _sessions=new();
    public ConcurrentDictionary<string,PairingRequest> Pending => _sessions.Pending;
    public event Action<string>? Status;
    public event Action? PairingRequested;
    public event Action<Announcement,IPAddress>? LegacyDiscovered;
    public event Action<NearbyDevice>? PeerReady;
    public string DeviceId => _store.DeviceId;
    public int PairingPort => ((IPEndPoint)_pairing.LocalEndpoint).Port;
    public DeviceHub(IDeviceStore store, byte[] legacy, int pairingPort = PairingProtocol.Port, bool discovery = true)
    {
        _store=store; _legacy=legacy; _udp=new UdpClient(AddressFamily.InterNetwork); _udp.EnableBroadcast=true;
        _udp.Client.Bind(new IPEndPoint(discovery ? IPAddress.Any : IPAddress.Loopback, discovery ? SharedProtocol.DiscoveryPort : 0));
        _pairing=new TcpListener(discovery ? IPAddress.Any : IPAddress.Loopback,pairingPort);
        try { _pairing.Start(4); } catch { _udp.Dispose(); throw; }
        if(discovery) { NetworkChange.NetworkAddressChanged+=OnNetworkAddressChanged; NetworkChange.NetworkAvailabilityChanged+=OnNetworkAvailabilityChanged; _=ReceiveAsync(); _=AnnounceAsync(); } _=AcceptPairsAsync();
    }
    public void RefreshDiscovery() => _wake.Signal();
    private void OnNetworkAddressChanged(object? sender, EventArgs args) => RefreshDiscovery();
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => RefreshDiscovery();
    public NearbyDevice[] Devices() => Nearby.Values.OrderByDescending(x=>x.Authenticated).ThenBy(x=>x.Name).ToArray();
    public DeviceTrust[] Trusted() => _store.Records();
    public bool IsTrusted(string id) => _store.Records().Any(t => t.Id == id && t.Active);
    private bool Remember(string value)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach(var p in _replay) if(now-p.Value>240000) _replay.TryRemove(p.Key,out _);
        return _replay.Count<4096 && _replay.TryAdd(value,now);
    }
    private static IPAddress[] Broadcasts() => NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork && SharedProtocol.IsLocalAddress(a.Address) && !IPAddress.IsLoopback(a.Address)).Select(a=>new IPAddress(a.Address.GetAddressBytes().Zip(a.IPv4Mask.GetAddressBytes(),(b,m)=>(byte)(b | ~m)).ToArray())).Append(IPAddress.Broadcast).Distinct().ToArray();
    private async Task SendAsync(byte[] bytes,IPAddress host) => await _udp.SendAsync(bytes,new IPEndPoint(host,SharedProtocol.DiscoveryPort),_stop.Token);
    private byte[] PublicBytes() => DiscoveryProtocol.Encode(DiscoveryProtocol.Create(DeviceId,_store.Name,"windows"));
    private async Task AnnounceAsync()
    {
        try { while(!_stop.IsCancellationRequested)
        {
            try
            {
                byte[] bytes=PublicBytes();
                foreach(var host in Broadcasts()) { try { await SendAsync(bytes,host); } catch(SocketException){} }
                var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach(var p in Nearby) if(now-p.Value.Seen>15000) Nearby.TryRemove(p.Key,out _);
                foreach(var t in _store.Records()) if(IPAddress.TryParse(t.Address,out var address) && SharedProtocol.IsLocalAddress(address)) await SendAsync(DiscoveryProtocol.Encode(DiscoveryProtocol.Create(DeviceId,_store.Name,"windows",t.Id,t.Key)),address);
                byte[] legacy=SharedProtocol.Encrypt(SharedProtocol.DeriveKey(_legacy),SharedProtocol.Serialize(new Announcement("announce",DeviceId,_store.Name,SharedProtocol.TcpPort,now)));
                foreach(var host in Broadcasts()) { try { await SendAsync(legacy,host); } catch(SocketException){} }
            } catch(Exception e) when(e is SocketException or NetworkInformationException or IOException) { Status?.Invoke("网络发现暂不可用，请检查同一 Wi-Fi、热点与防火墙"); }
            await _wake.WaitAsync(3000,_stop.Token);
        }} catch(OperationCanceledException){} catch(ObjectDisposedException){}
    }
    private async Task ReceiveAsync()
    {
        var replies=new ConcurrentDictionary<string,long>();
        try { while(!_stop.IsCancellationRequested)
        {
            try
            {
                var data=await _udp.ReceiveAsync(_stop.Token); var host=data.RemoteEndPoint.Address;
                if(!SharedProtocol.IsLocalAddress(host) || data.Buffer.Length>4096) continue;
                if(!DiscoveryProtocol.IsPublic(data.Buffer))
                {
                    var a=JsonSerializer.Deserialize<Announcement>(SharedProtocol.Decrypt(SharedProtocol.DeriveKey(_legacy),data.Buffer));
                    if(a!=null && a.Type=="announce" && a.DeviceId!=DeviceId && !string.IsNullOrEmpty(a.DeviceId) && a.DeviceId.Length<=64 && a.Port==SharedProtocol.TcpPort && SharedProtocol.IsFresh(a.TimestampUtc)) LegacyDiscovered?.Invoke(a,host);
                    continue;
                }
                var p=DiscoveryProtocol.Parse(data.Buffer); if(p.Id==DeviceId) continue;
                var trust=_store.Records().FirstOrDefault(t=>t.Id==p.Id);
                bool authenticated=trust!=null && DiscoveryProtocol.Authenticate(p,DeviceId,trust.Key) && Remember("presence:"+p.Id+":"+p.Nonce);
                if(p.Type=="presence" && !authenticated) continue;
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if(Nearby.Count>=32 && !Nearby.ContainsKey(p.Id)) continue;
                if(authenticated)
                {
                    lock(_trustGate)
                    {
                        var current=_store.Records().FirstOrDefault(t=>t.Id==p.Id);
                        if(current!=null && current.Key.AsSpan().SequenceEqual(trust!.Key)) _store.Save(current with { Address=host.ToString(),Name=p.Name,Active=true });
                        else continue;
                    }
                    bool wasReady=Nearby.TryGetValue(p.Id,out var prior) && prior.Authenticated && prior.HistorySyncVersion==p.HistorySyncVersion && now-prior.Seen<=15000;
                    long epoch=wasReady ? prior!.ConnectionEpoch : _peerEpochs.AddOrUpdate(p.Id,1,(_,value)=>value+1);
                    var readyPeer=new NearbyDevice(p.Id,p.Name,p.Platform,host.ToString(),now,true,p.HistorySyncVersion,epoch);
                    Nearby[p.Id]=readyPeer;
                    if(!wasReady) PeerReady?.Invoke(readyPeer);
                }
                else if(!Nearby.TryGetValue(p.Id,out var old) || !old.Authenticated) Nearby[p.Id]=new(p.Id,p.Name,p.Platform,host.ToString(),now,false,p.HistorySyncVersion);
                if(replies.Count>128) foreach(var item in replies) if(now-item.Value>30000) replies.TryRemove(item.Key,out _);
                if(replies.Count<128 && (!replies.TryGetValue(host.ToString(),out long last) || now-last>2500))
                {
                    replies[host.ToString()]=now;
                    await SendAsync(PublicBytes(),host);
                    if(trust!=null) await SendAsync(DiscoveryProtocol.Encode(DiscoveryProtocol.Create(DeviceId,_store.Name,"windows",trust.Id,trust.Key)),host);
                }
            }
            catch(Exception e) when(e is InvalidDataException or CryptographicException or JsonException or IOException or ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException){}
            catch(SocketException) { await Task.Delay(500,_stop.Token); }
        }} catch(OperationCanceledException){} catch(ObjectDisposedException){}
    }
    public async Task<byte[]> OutgoingAsync(Stream stream,IPAddress address,byte kind,byte[] legacyKey,CancellationToken token) =>
        (await OutgoingRouteAsync(stream,address,kind,legacyKey,token)).Key;

    public async Task<OutgoingSessionRoute> OutgoingRouteAsync(Stream stream,IPAddress address,byte kind,byte[] legacyKey,CancellationToken token)
    {
        DeviceTrust[] trustedRecords=_store.Records();
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var peer=Nearby.Values.FirstOrDefault(p=>p.Address==address.ToString() && p.Authenticated && now-p.Seen<=15000 && trustedRecords.Any(t=>t.Id==p.Id && t.Active));
        var trust=peer==null?null:trustedRecords.FirstOrDefault(t=>t.Id==peer.Id && t.Active);
        if(trust==null)
        {
            if(trustedRecords.Any(t=>t.Address==address.ToString())) throw new IOException("目标设备尚未通过在线认证");
            return new(legacyKey,null);
        }
        byte[] header=TrustedChannel.Header(DeviceId,trust.Id,kind,trust.Key);
        await stream.WriteAsync(header,token);
        return new(TrustedChannel.Open(header,trust.Id,kind,trust.Key),trust.Id);
    }
    public async Task<SessionRoute> IncomingAsync(Stream stream,byte kind,byte[] legacyKey,CancellationToken token)
    {
        byte[] first=new byte[4]; await stream.ReadExactlyAsync(first,token);
        if(!first.SequenceEqual(TrustedChannel.Magic)) return new(legacyKey,null,first);
        byte[] header=new byte[TrustedChannel.HeaderSize]; first.CopyTo(header,0); await stream.ReadExactlyAsync(header.AsMemory(4),token);
        var trust=_store.Records().FirstOrDefault(t=>t.Id==TrustedChannel.Source(header) && t.Active) ?? throw new CryptographicException("未知的设备");
        byte[] key=TrustedChannel.Open(header,DeviceId,kind,trust.Key);
        if(!Remember("route:"+Convert.ToHexString(header))) throw new CryptographicException("重复会话");
        return new(key,trust.Id,null);
    }
    public void Revoke(string id)
    {
        lock(_trustGate) { _epochs.AddOrUpdate(id,1,(_,n)=>n+1); _store.Remove(id); } Nearby.TryRemove(id,out _);
        foreach(var p in Pending.Values.Where(x=>x.Id==id)) p.Decision.TrySetResult(false);
        Revoked?.Invoke(id);
    }
    public event Action<string>? Revoked;
    public void Decide(PairingRequest request,bool accept) { if(_sessions.IsCurrent(request.Session,request) && request.Expires>DateTimeOffset.UtcNow) request.Decision.TrySetResult(accept); }
    public async Task PairAsync(NearbyDevice peer, int port = PairingProtocol.Port)
    {
        if(!_slots.Wait(0)) { Status?.Invoke("已有配对请求，请稍后重试"); return; }
        using var client=new TcpClient(AddressFamily.InterNetwork); _clients[client]=0;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(120000);
        try
        {
            using(var connect=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                connect.CancelAfter(5000);
                try { await client.ConnectAsync(IPAddress.Parse(peer.Address),port,connect.Token); }
                catch(Exception e) when(e is SocketException or OperationCanceledException)
                { if(!_stop.IsCancellationRequested) Status?.Invoke(PairingErrors.Connect(e)); return; }
            }
            await PairSessionAsync(client,true,PairingProtocol.ValidId(peer.Id)?peer.Id:null,deadline.Token);
        }
        catch(Exception e) when(e is InvalidDataException or IOException or SocketException or CryptographicException or JsonException or OperationCanceledException or ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { Status?.Invoke("配对未完成，可能是对方取消或连接中断，请重试"); }
        finally { _clients.TryRemove(client,out _); _slots.Release(); }
    }
    private async Task AcceptPairsAsync()
    {
        try { while(!_stop.IsCancellationRequested)
        {
            var client=await _pairing.AcceptTcpClientAsync(_stop.Token);
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach(var p in _pairRate) if(now-p.Value>30000) _pairRate.TryRemove(p.Key,out _);
            if(client.Client.RemoteEndPoint is not IPEndPoint ep || !SharedProtocol.IsLocalAddress(ep.Address) || _pairRate.Count>=128 || (_pairRate.TryGetValue(ep.Address.ToString(),out long last) && now-last<5000) || !_slots.Wait(0)) { client.Dispose(); continue; }
            _pairRate[ep.Address.ToString()]=now; _clients[client]=0;
            _=Task.Run(async()=> { using(client) using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)) { timeout.CancelAfter(120000); try { await PairSessionAsync(client,false,null,timeout.Token); } catch(Exception e) when(e is InvalidDataException or IOException or SocketException or CryptographicException or JsonException or OperationCanceledException or ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { Status?.Invoke("一项配对已取消或超时"); } finally { _clients.TryRemove(client,out _); _slots.Release(); } } });
        }} catch(OperationCanceledException){} catch(ObjectDisposedException){} catch(SocketException){}
    }
    private static async Task WriteAsync(Stream stream,object value,CancellationToken token,byte[]? key=null)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value); if(key!=null) bytes=SharedProtocol.Encrypt(key,bytes);
        if(bytes.Length>4096) throw new InvalidDataException();
        await stream.WriteAsync(SharedProtocol.Frame(bytes),token);
    }
    private static async Task<JsonElement> ReadAsync(Stream stream,CancellationToken token,byte[]? key=null)
    {
        byte[] header=new byte[4]; await stream.ReadExactlyAsync(header,token); int size=SharedProtocol.ReadFrameLength(header);
        if(size<2 || size>4096) throw new InvalidDataException();
        byte[] bytes=new byte[size]; await stream.ReadExactlyAsync(bytes,token);
        using var json=JsonDocument.Parse(key==null?bytes:SharedProtocol.Decrypt(key,bytes),new JsonDocumentOptions { MaxDepth=8 }); return json.RootElement.Clone();
    }
    private static string S(JsonElement j,string key) => j.GetProperty(key).GetString() ?? throw new InvalidDataException();
    private async Task PairSessionAsync(TcpClient client,bool initiator,string? expected,CancellationToken token)
    {
        string session=Guid.NewGuid().ToString("N"); string remoteId="",remoteName="",remotePlatform="",commit=""; int epoch=0; bool reserved=false;
        var stream=client.GetStream(); string address=((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        using var material=new PairingMaterial(); string role=initiator?"i":"r"; byte[]? key=null;
        object sessionOwner=new(); bool sessionReserved=false; PairingRequest? request=null;
        try
        {
            if(initiator) { if(!_sessions.Reserve(session,sessionOwner)) throw new IOException("重复的配对会话"); sessionReserved=true; }
            if(initiator) await WriteAsync(stream,new { Type="hello",Session=session,Id=DeviceId,Name=_store.Name,Platform="windows",Commit=PairingProtocol.Commitment(session,role,DeviceId,_store.Name,material.PublicKey,material.Nonce) },token);
            var hello=await ReadAsync(stream,token);
            if(S(hello,"Type")!=(initiator?"commit":"hello")) throw new InvalidDataException();
            if(!initiator) session=S(hello,"Session"); else if(S(hello,"Session")!=session) throw new InvalidDataException();
            remoteId=S(hello,"Id"); remoteName=S(hello,"Name"); remotePlatform=S(hello,"Platform"); commit=S(hello,"Commit");
            if(!PairingProtocol.ValidId(session) || !PairingProtocol.ValidId(remoteId) || remoteId==DeviceId || (expected!=null && expected!=remoteId) || remoteName.Length is <1 or >80 || remotePlatform is not ("windows" or "android") || _store.Records().Any(t=>t.Id==remoteId && t.Active)) throw new InvalidDataException("请先取消已有配对");
            if(!initiator) { if(!_sessions.Reserve(session,sessionOwner)) throw new IOException("重复的配对会话"); sessionReserved=true; }
            if(!_pairIds.TryAdd(remoteId,0)) throw new IOException("该设备已有配对会话");
            reserved=true; epoch=_epochs.GetOrAdd(remoteId,0);
            if(!initiator) await WriteAsync(stream,new { Type="commit",Session=session,Id=DeviceId,Name=_store.Name,Platform="windows",Commit=PairingProtocol.Commitment(session,role,DeviceId,_store.Name,material.PublicKey,material.Nonce) },token);
            if(initiator) await WriteAsync(stream,new { Type="reveal",Public=material.PublicKey,Nonce=material.Nonce },token);
            var reveal=await ReadAsync(stream,token); if(S(reveal,"Type")!="reveal") throw new InvalidDataException();
            string pub=S(reveal,"Public"),nonce=S(reveal,"Nonce");
            PairingProtocol.Verify(commit,PairingProtocol.Commitment(session,initiator?"r":"i",remoteId,remoteName,pub,nonce));
            if(!initiator) await WriteAsync(stream,new { Type="reveal",Public=material.PublicKey,Nonce=material.Nonce },token);
            byte[] transcript=initiator?PairingProtocol.Transcript(session,DeviceId,_store.Name,material.PublicKey,material.Nonce,remoteId,remoteName,pub,nonce):PairingProtocol.Transcript(session,remoteId,remoteName,pub,nonce,DeviceId,_store.Name,material.PublicKey,material.Nonce);
            key=PairingProtocol.SessionKey(material,pub,transcript);
            var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            request=new(session,remoteId,remoteName,address,PairingProtocol.Code(key,transcript),DateTimeOffset.UtcNow.AddSeconds(100),decision);
            _sessions.Publish(session,sessionOwner,request);
            PairingRequested?.Invoke();
            Status?.Invoke("有设备等待配对，请主动打开设备配对页核对验证码");
            using var wait=CancellationTokenSource.CreateLinkedTokenSource(token); wait.CancelAfter(100000);
            bool accepted=await decision.Task.WaitAsync(wait.Token);
            await WriteAsync(stream,new { Type="confirm",Accept=accepted,Session=session },token,key);
            if(!accepted) throw new IOException("配对已拒绝");
            var confirm=await ReadAsync(stream,token,key);
            if(S(confirm,"Type")!="confirm" || S(confirm,"Session")!=session || !confirm.GetProperty("Accept").GetBoolean()) throw new IOException("配对被拒绝");
            byte[] pairing;
            if(initiator)
            {
                pairing=RandomNumberGenerator.GetBytes(32);
                await WriteAsync(stream,new { Type="key",Key=Convert.ToBase64String(pairing) },token,key);
                if(S(await ReadAsync(stream,token,key),"Type")!="ready") throw new IOException();
                SavePair(new(remoteId,remoteName,remotePlatform,pairing,address,false),epoch);
                await WriteAsync(stream,new { Type="commit" },token,key);
                if(S(await ReadAsync(stream,token,key),"Type")!="done") throw new IOException();
            }
            else
            {
                var value=await ReadAsync(stream,token,key); if(S(value,"Type")!="key") throw new IOException();
                pairing=Convert.FromBase64String(S(value,"Key")); if(pairing.Length!=32) throw new InvalidDataException();
                SavePair(new(remoteId,remoteName,remotePlatform,pairing,address,false),epoch);
                await WriteAsync(stream,new { Type="ready" },token,key);
                if(S(await ReadAsync(stream,token,key),"Type")!="commit") throw new IOException();
                await WriteAsync(stream,new { Type="done" },token,key);
            }
            SavePair(new(remoteId,remoteName,remotePlatform,pairing,address,true),epoch);
            await SendAsync(DiscoveryProtocol.Encode(DiscoveryProtocol.Create(DeviceId,_store.Name,"windows",remoteId,pairing)),IPAddress.Parse(address));
            Status?.Invoke("设备已配对，正在认证连接");
        }
        finally { if(reserved) _pairIds.TryRemove(remoteId,out _); if(sessionReserved) _sessions.Release(session,sessionOwner,request); if(key!=null) CryptographicOperations.ZeroMemory(key); }
    }
    private void SavePair(DeviceTrust record,int epoch) { lock(_trustGate) { if (_epochs.GetOrAdd(record.Id,0)!=epoch) throw new IOException("配对已撤销"); _store.Save(record); } }
    public void Dispose() { NetworkChange.NetworkAddressChanged-=OnNetworkAddressChanged; NetworkChange.NetworkAvailabilityChanged-=OnNetworkAvailabilityChanged; _stop.Cancel(); _udp.Dispose(); _pairing.Stop(); foreach(var p in Pending.Values) p.Decision.TrySetResult(false); foreach(var c in _clients.Keys) c.Dispose(); _wake.Dispose(); }
}
