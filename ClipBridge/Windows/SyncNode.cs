using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class SyncNode : IDisposable
{
    private sealed record Peer(IPEndPoint EndPoint, DateTimeOffset LastSeen, bool Manual = false, string Name = "", string PeerId = "");
    private readonly byte[] _key;
    private readonly DeviceHub? _hub;
    private readonly HistoryStore? _historyStore;
    private readonly string _pairingKeyText;
    private readonly string _deviceId = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, Peer> _peers = new();
    private readonly ConcurrentDictionary<string, long> _seenMessages = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connections = new(4);
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udp;
    private readonly Action<Action> _dispatchToUi;
    private bool _disposed;
    public int Port => ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
    public event Action<string>? RemoteTextReceived;
    public event Action<string>? StatusChanged;

    public SyncNode(byte[] pairingKey, Action<Action> dispatchToUi, int textPort = SharedProtocol.TcpPort, bool discovery = true, DeviceHub? hub = null, HistoryStore? historyStore = null)
    {
        _hub = hub;
        _historyStore = historyStore;
        if (_hub != null)
        {
            _hub.PeerReady += OnPeerReady;
            foreach (NearbyDevice peer in _hub.Devices().Where(peer => peer.Authenticated && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - peer.Seen <= 15000)) OnPeerReady(peer);
        }
        _deviceId = hub?.DeviceId ?? _deviceId;
        _key = SharedProtocol.DeriveKey(pairingKey);
        _pairingKeyText = Convert.ToHexString(pairingKey);
        _dispatchToUi = dispatchToUi;
        _tcpListener = new TcpListener(IPAddress.Any, textPort);
        _udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            _udp.EnableBroadcast = true;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, discovery ? SharedProtocol.DiscoveryPort : 0));
            _tcpListener.Start(8);
        }
        catch { _udp.Dispose(); _tcpListener.Stop(); throw; }
        if (discovery) { _ = ReceiveAnnouncementsAsync(_stop.Token); _ = AnnounceLoopAsync(_stop.Token); }
        _ = AcceptLoopAsync(_stop.Token);
    }

    public void DiscoveredLegacy(Announcement a, IPAddress host)
    {
        if (_peers.Count < 32 || _peers.ContainsKey(a.DeviceId))
            _peers[a.DeviceId] = new Peer(new IPEndPoint(host, SharedProtocol.TcpPort), DateTimeOffset.UtcNow, false, (a.Name ?? "配对设备").Substring(0, Math.Min((a.Name ?? "配对设备").Length, 80)));
    }

    public void AddManualPeer(IPAddress address)
    {
        if (!SharedProtocol.IsLocalAddress(address)) throw new ArgumentException("Only local IPv4 addresses are allowed.");
        string id = "manual:" + address;
        if (_peers.Count >= 32 && !_peers.ContainsKey(id)) throw new InvalidOperationException("Too many peers.");
        _peers[id] = new Peer(new IPEndPoint(address, SharedProtocol.TcpPort), DateTimeOffset.MinValue, true);
        Report("已添加手动设备地址");
    }

    public void RemoveManualPeer(IPAddress address)
    {
        if (_peers.TryRemove("manual:" + address, out _)) Report("已移除手动设备地址");
    }

    public PeerSnapshot[] GetPeerSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        var visible = _peers.Values.ToList();
        if (_hub != null) visible.AddRange(_hub.Devices().Where(p => p.Authenticated).Select(p => new Peer(new IPEndPoint(IPAddress.Parse(p.Address), SharedProtocol.TcpPort), DateTimeOffset.FromUnixTimeMilliseconds(p.Seen), false, p.Name, p.Id)));
        return visible.ToArray().GroupBy(p => p.EndPoint).Select(group =>
        {
            Peer? peer = group.Where(p => !p.Manual).OrderByDescending(p => p.LastSeen).FirstOrDefault();
            bool manual = group.Any(p => p.Manual);
            bool online = peer != null && now - peer.LastSeen <= TimeSpan.FromSeconds(15);
            return new PeerSnapshot(!string.IsNullOrWhiteSpace(peer?.Name) ? peer.Name : "手动设备", group.Key.Address.ToString(), group.Key.Port, manual, online, peer?.LastSeen, peer?.PeerId ?? "");
        }).OrderByDescending(p => p.RecentlySeen).ThenBy(p => p.Address, StringComparer.Ordinal).ToArray();
    }

    private readonly ConcurrentDictionary<string,byte> _activeHistorySync = new();

    public async Task<bool> SendTextAsync(IPAddress address, string text, CancellationToken token, int port = SharedProtocol.TcpPort)
    {
        if (!SharedProtocol.IsLocalAddress(address)) throw new ArgumentException("请输入局域网 IPv4 地址");
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("剪贴板没有纯文本");
        if (Encoding.UTF8.GetByteCount(text) > SharedProtocol.MaxTextBytes) throw new ArgumentException("文本超过 256 KiB");
        if (string.Equals(text.Trim(), _pairingKeyText, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("不能发送配对密钥");
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(address, port, deadline.Token);
            var message = new ClipboardMessage("clipboard", Guid.NewGuid().ToString("N"), _deviceId, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            OutgoingSessionRoute route = _hub == null
                ? new OutgoingSessionRoute(_key, null)
                : await _hub.OutgoingRouteAsync(client.GetStream(), address, 1, _key, deadline.Token);
            byte[] frame = SharedProtocol.Frame(SharedProtocol.Encrypt(route.Key, SharedProtocol.Serialize(message)));
            await client.GetStream().WriteAsync(frame, deadline.Token);
            Report("文字已发送，接收结果请在目标设备确认");
            if (route.PeerId is string peerId) RecordTextHistory(peerId, message);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _stop.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException)
        { Report("文字发送失败，请检查设备、网络和防火墙"); return false; }
    }

    public async Task<bool> SyncHistoryAsync(IPAddress address, string peerId, int port, CancellationToken token)
    {
        if (_hub == null || _historyStore == null || !PairingProtocol.ValidId(peerId) || !SharedProtocol.IsLocalAddress(address)) return false;
        string sessionKey = peerId + ":" + Interlocked.Increment(ref _historyRequestSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_activeHistorySync.TryAdd(sessionKey, 0)) return false;
        try { return await ExchangeHistoryAsync(address, peerId, port, token); }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or CryptographicException or JsonException or InvalidDataException or ObjectDisposedException)
        { System.Diagnostics.Trace.TraceWarning("ClipBridge history sync failed: {0}", exception); return false; }
        finally { _activeHistorySync.TryRemove(sessionKey, out _); }
    }

    private long _historyRequestSequence;

    private async Task<bool> ExchangeHistoryAsync(IPAddress address, string peerId, int port, CancellationToken token)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        connect.CancelAfter(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(address, port, connect.Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        NetworkStream stream = client.GetStream();
        OutgoingSessionRoute route = await _hub!.OutgoingRouteAsync(stream, address, 1, _key, deadline.Token);
        if (route.PeerId != peerId) { System.Diagnostics.Trace.TraceWarning("ClipBridge history sync route mismatch."); return false; }
        byte[] key = route.Key;
        string requestId = Guid.NewGuid().ToString("N");
        HistoryStoreSnapshot localSnapshot = _historyStore!.GetSyncSnapshot(peerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        IReadOnlyList<HistorySyncMessage> localPages = HistorySyncProtocol.CreatePages(requestId, _deviceId, localSnapshot);
        await WriteHistoryMessageAsync(stream, key, HistorySyncProtocol.CreateHello(requestId, _deviceId, localPages.Count), deadline.Token);
        var remoteEntries = new List<HistorySyncEvent>();
        var remoteTombstones = new List<HistoryTombstone>();
        HistorySyncMessage hello = await ReadHistoryMessageAsync(stream, key, peerId, deadline.Token);
        if (hello.Type != "historySyncHello" || hello.RequestId != requestId) { System.Diagnostics.Trace.TraceWarning("ClipBridge history sync hello mismatch."); return false; }
        for (int pageNumber = 0; pageNumber < hello.PageNumber; pageNumber++)
        {
            await WriteHistoryMessageAsync(stream, key, HistorySyncProtocol.CreateRequest(requestId, _deviceId, pageNumber), deadline.Token);
            HistorySyncMessage page = await ReadHistoryMessageAsync(stream, key, peerId, deadline.Token);
            if (page.Type != "historySyncPage" || page.RequestId != requestId || page.PageNumber != pageNumber) { System.Diagnostics.Trace.TraceWarning("ClipBridge history sync page mismatch."); return false; }
            remoteEntries.AddRange(page.Entries!); remoteTombstones.AddRange(page.Tombstones!);
            await WriteHistoryMessageAsync(stream, key, HistorySyncProtocol.CreateAck(requestId, _deviceId, pageNumber), deadline.Token);
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _historyStore.Merge(peerId, remoteEntries, remoteTombstones, now);
        for (int pageNumber = 0; pageNumber < localPages.Count; pageNumber++)
        {
            await WriteHistoryMessageAsync(stream, key, localPages[pageNumber], deadline.Token);
            HistorySyncMessage ack = await ReadHistoryMessageAsync(stream, key, peerId, deadline.Token);
            if (ack.Type != "historySyncAck" || ack.RequestId != requestId || ack.PageNumber != pageNumber) { System.Diagnostics.Trace.TraceWarning("ClipBridge history sync acknowledgement mismatch."); return false; }
        }
        return true;
    }

    private static async Task WriteHistoryMessageAsync(Stream stream, byte[] key, HistorySyncMessage message, CancellationToken token)
    {
        byte[] payload = HistorySyncProtocol.Encode(message, message.PeerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await stream.WriteAsync(SharedProtocol.Frame(SharedProtocol.Encrypt(key, payload)), token);
    }

    private static async Task<HistorySyncMessage> ReadHistoryMessageAsync(Stream stream, byte[] key, string peerId, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int size = SharedProtocol.ReadFrameLength(header);
        if (size < 28 || size > SharedProtocol.MaxFrameBytes) throw new InvalidDataException("Invalid history sync frame size.");
        byte[] frame = new byte[size]; await stream.ReadExactlyAsync(frame, token);
        return HistorySyncProtocol.Decode(SharedProtocol.Decrypt(key, frame), peerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private byte[] AnnouncementBytes() => SharedProtocol.Encrypt(_key, SharedProtocol.Serialize(new Announcement("announce", _deviceId, Environment.MachineName, SharedProtocol.TcpPort, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));

    private async Task AnnounceLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[] bytes = AnnouncementBytes();
                    await _udp.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, SharedProtocol.DiscoveryPort), token);
                    foreach (var item in _peers.ToArray())
                    {
                        if (item.Value.Manual) await _udp.SendAsync(bytes, new IPEndPoint(item.Value.EndPoint.Address, SharedProtocol.DiscoveryPort), token);
                        else if (DateTimeOffset.UtcNow - item.Value.LastSeen > TimeSpan.FromSeconds(15)) _peers.TryRemove(item.Key, out _);
                    }
                }
                catch (SocketException) { Report("网络发现暂不可用，可通过手动 IP 发送"); }
                await Task.Delay(3000, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ReceiveAnnouncementsAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync(token);
                    if (result.Buffer.Length < 28 || result.Buffer.Length > 4096 || !SharedProtocol.IsLocalAddress(result.RemoteEndPoint.Address)) continue;
                    var a = JsonSerializer.Deserialize<Announcement>(SharedProtocol.Decrypt(_key, result.Buffer));
                    if (a == null || a.Type != "announce" || a.DeviceId == _deviceId || string.IsNullOrEmpty(a.DeviceId) || a.DeviceId.Length > 64 || a.Port != SharedProtocol.TcpPort || !SharedProtocol.IsFresh(a.TimestampUtc)) continue;
                    bool added = !_peers.ContainsKey(a.DeviceId);
                    if (added && _peers.Count >= 32) continue;
                    string name = a.Name ?? "同密钥设备";
                    _peers[a.DeviceId] = new Peer(new IPEndPoint(result.RemoteEndPoint.Address, a.Port), DateTimeOffset.UtcNow, false, name.Substring(0, Math.Min(name.Length, 80)));
                    if (added) { Report("已发现配对设备"); await _udp.SendAsync(AnnouncementBytes(), result.RemoteEndPoint, token); }
                }
                catch (CryptographicException) { }
                catch (JsonException) { }
                catch (IOException) { }
                catch (SocketException) { await Task.Delay(1000, token); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(token);
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !SharedProtocol.IsLocalAddress(peer.Address) || !_connections.Wait(0)) client.Dispose();
                else _ = HandleClientAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { if (!token.IsCancellationRequested) Report("文字接收服务已停止，请重启应用"); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        bool handlingHistorySync = false;
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(5000);
            try
            {
                var stream = client.GetStream();
                var route = _hub == null ? new SessionRoute(_key, null, null) : await _hub.IncomingAsync(stream, 1, _key, deadline.Token);
                byte[] header = route.Prefix ?? new byte[4];
                if (route.Prefix == null) await stream.ReadExactlyAsync(header, deadline.Token);
                int size = SharedProtocol.ReadFrameLength(header);
                if (size < 28 || size > SharedProtocol.MaxFrameBytes) return;
                byte[] frame = new byte[size];
                await stream.ReadExactlyAsync(frame, deadline.Token);
                byte[] plaintext = SharedProtocol.Decrypt(route.Key, frame);
                using var messageDocument = JsonDocument.Parse(plaintext);
                if (messageDocument.RootElement.TryGetProperty("type", out JsonElement messageType) && messageType.ValueKind == JsonValueKind.String && messageType.GetString()?.StartsWith("historySync", StringComparison.Ordinal) == true)
                {
                    handlingHistorySync = true;
                    deadline.CancelAfter(TimeSpan.FromSeconds(120));
                    if (route.Source != null && route.Source == messageDocument.RootElement.GetProperty("peerId").GetString() && _hub?.IsTrusted(route.Source) == true)
                        await HandleHistorySyncAsync(stream, route.Key, route.Source, HistorySyncProtocol.Decode(plaintext, route.Source, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), deadline.Token);
                    return;
                }
                ClipboardMessage? m = null;
                try { m = JsonSerializer.Deserialize<ClipboardMessage>(plaintext); } catch (JsonException) { return; }
                if (m == null || m.Type != "clipboard" || (route.Source != null && route.Source != m.OriginDeviceId) || m.OriginDeviceId == _deviceId || string.IsNullOrEmpty(m.MessageId) || m.MessageId.Length > 64 || string.IsNullOrEmpty(m.OriginDeviceId) || m.OriginDeviceId.Length > 64 || string.IsNullOrEmpty(m.Text) || Encoding.UTF8.GetByteCount(m.Text) > SharedProtocol.MaxTextBytes || !SharedProtocol.IsFresh(m.TimestampUtc) || string.Equals(m.Text.Trim(), _pairingKeyText, StringComparison.OrdinalIgnoreCase) || !RememberMessage(m.MessageId)) return;
                if (route.Source != null && _hub?.IsTrusted(route.Source) == true) RecordTextHistory(route.Source, m);
                _dispatchToUi(() => { if (!_disposed && (route.Source == null || _hub!.IsTrusted(route.Source))) RemoteTextReceived?.Invoke(m.Text); });
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is CryptographicException || ex is JsonException || ex is OperationCanceledException)
            { if (!token.IsCancellationRequested && !handlingHistorySync) Report("一条文字未能接收，请检查配对和网络"); }
            finally { _connections.Release(); }
        }
    }

    private async Task HandleHistorySyncAsync(Stream stream, byte[] key, string peerId, HistorySyncMessage first, CancellationToken token)
    {
        if (first.Type != "historySyncHello" || _historyStore == null || _hub?.IsTrusted(peerId) != true) return;
        var localPages = HistorySyncProtocol.CreatePages(first.RequestId, _deviceId, _historyStore!.GetSyncSnapshot(peerId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await WriteHistoryMessageAsync(stream, key, HistorySyncProtocol.CreateHello(first.RequestId, _deviceId, localPages.Count), token);
        var entries = new List<HistorySyncEvent>(); var tombstones = new List<HistoryTombstone>();
        for (int number = 0; number < localPages.Count; number++)
        {
            HistorySyncMessage request = await ReadHistoryMessageAsync(stream, key, peerId, token);
            if (request.Type != "historySyncRequest" || request.RequestId != first.RequestId || request.PageNumber != number) return;
            HistorySyncMessage page = localPages[number];
            await WriteHistoryMessageAsync(stream, key, page, token);
            HistorySyncMessage ack = await ReadHistoryMessageAsync(stream, key, peerId, token);
            if (ack.Type != "historySyncAck" || ack.RequestId != first.RequestId || ack.PageNumber != number) return;
        }
        for (int number = 0; number < first.PageNumber; number++)
        {
            HistorySyncMessage page = await ReadHistoryMessageAsync(stream, key, peerId, token);
            if (page.Type != "historySyncPage" || page.RequestId != first.RequestId || page.PageNumber != number) return;
            entries.AddRange(page.Entries!); tombstones.AddRange(page.Tombstones!);
            await WriteHistoryMessageAsync(stream, key, HistorySyncProtocol.CreateAck(first.RequestId, _deviceId, number), token);
        }
        _historyStore.Merge(peerId, entries, tombstones, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private void OnPeerReady(NearbyDevice peer)
    {
        if (peer.HistorySyncVersion != HistorySyncProtocol.Version || peer.Id == _deviceId || StringComparer.Ordinal.Compare(_deviceId, peer.Id) > 0) return;
        string epochKey = peer.Id + ":" + peer.ConnectionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_activeHistorySync.TryAdd(epochKey, 0)) return;
        _ = Task.Run(async () =>
        {
            try { await SyncHistoryAsync(IPAddress.Parse(peer.Address), peer.Id, SharedProtocol.TcpPort, _stop.Token); }
            finally { _activeHistorySync.TryRemove(epochKey, out _); }
        });
    }

    private void RecordTextHistory(string peerId, ClipboardMessage message)
    {
        if (_historyStore == null) { try { Report("文字已传输，但历史记录不可用"); } catch { } return; }
        var value = new HistorySyncEvent(HistoryPolicy.SchemaVersion, message.MessageId, peerId, message.OriginDeviceId,
            message.TimestampUtc, "text", message.Text, Array.Empty<HistoryFileMetadata>(), "sent");
        try { _historyStore.Upsert(new HistoryLocalRecord(value, new Dictionary<string, string>())); }
        catch { try { Report("文字已传输，但历史记录保存失败"); } catch { } }
    }

    private bool RememberMessage(string id)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var item in _seenMessages) if (now - item.Value > 600000) _seenMessages.TryRemove(item.Key, out _);
        return _seenMessages.Count < 2048 && _seenMessages.TryAdd(id, now);
    }
    private void Report(string message) => _dispatchToUi(() => { if (!_disposed) StatusChanged?.Invoke(message); });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; if (_hub != null) _hub.PeerReady -= OnPeerReady; _stop.Cancel(); _tcpListener.Stop(); _udp.Dispose();
    }
}
