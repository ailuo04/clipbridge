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

namespace ClipBridge.Windows;

internal sealed class SyncNode : IDisposable
{
    private sealed record Peer(IPEndPoint EndPoint, DateTimeOffset LastSeen, bool Manual = false, string Name = "");
    private readonly byte[] _key;
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

    public SyncNode(byte[] pairingKey, Action<Action> dispatchToUi, int textPort = SharedProtocol.TcpPort, bool discovery = true)
    {
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
        return _peers.Values.ToArray().GroupBy(p => p.EndPoint).Select(group =>
        {
            Peer? peer = group.Where(p => !p.Manual).OrderByDescending(p => p.LastSeen).FirstOrDefault();
            bool manual = group.Any(p => p.Manual);
            bool online = peer != null && now - peer.LastSeen <= TimeSpan.FromSeconds(15);
            return new PeerSnapshot(!string.IsNullOrWhiteSpace(peer?.Name) ? peer.Name : "手动设备", group.Key.Address.ToString(), group.Key.Port, manual, online, peer?.LastSeen);
        }).OrderByDescending(p => p.RecentlySeen).ThenBy(p => p.Address, StringComparer.Ordinal).ToArray();
    }

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
            byte[] frame = SharedProtocol.Frame(SharedProtocol.Encrypt(_key, SharedProtocol.Serialize(message)));
            await client.GetStream().WriteAsync(frame, deadline.Token);
            Report("文字已发送，接收结果请在目标设备确认");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _stop.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException)
        { Report("文字发送失败，请检查设备、网络和防火墙"); return false; }
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
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(5000);
            try
            {
                var stream = client.GetStream();
                byte[] header = new byte[4];
                await stream.ReadExactlyAsync(header, deadline.Token);
                int size = SharedProtocol.ReadFrameLength(header);
                if (size < 28 || size > SharedProtocol.MaxFrameBytes) return;
                byte[] frame = new byte[size];
                await stream.ReadExactlyAsync(frame, deadline.Token);
                var m = JsonSerializer.Deserialize<ClipboardMessage>(SharedProtocol.Decrypt(_key, frame));
                if (m == null || m.Type != "clipboard" || m.OriginDeviceId == _deviceId || string.IsNullOrEmpty(m.MessageId) || m.MessageId.Length > 64 || string.IsNullOrEmpty(m.OriginDeviceId) || m.OriginDeviceId.Length > 64 || string.IsNullOrEmpty(m.Text) || Encoding.UTF8.GetByteCount(m.Text) > SharedProtocol.MaxTextBytes || !SharedProtocol.IsFresh(m.TimestampUtc) || string.Equals(m.Text.Trim(), _pairingKeyText, StringComparison.OrdinalIgnoreCase) || !RememberMessage(m.MessageId)) return;
                _dispatchToUi(() => { if (!_disposed) RemoteTextReceived?.Invoke(m.Text); });
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is CryptographicException || ex is JsonException || ex is OperationCanceledException)
            { if (!token.IsCancellationRequested) Report("一条文字未能接收，请检查配对和网络"); }
            finally { _connections.Release(); }
        }
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
        _disposed = true; _stop.Cancel(); _tcpListener.Stop(); _udp.Dispose();
    }
}
