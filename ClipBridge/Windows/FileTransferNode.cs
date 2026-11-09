using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class FileTransferNode : IDisposable
{
    private readonly byte[] _key;
    private readonly DeviceHub? _hub;
    private readonly HistoryStore? _historyStore;
    private readonly ConcurrentDictionary<string, string> _activePeers = new();
    private readonly string _directory;
    private readonly string _deviceId = Guid.NewGuid().ToString("N");
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(2);
    private readonly SemaphoreSlim _sendSlots = new(2);
    public bool IsBusy => !_active.IsEmpty;
    private readonly Func<FileOffer, IPAddress, CancellationToken, Task<bool>> _confirm;
    private readonly ConcurrentDictionary<string, long> _seen = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private bool _disposed;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    private readonly ConcurrentDictionary<string, (string Name, long Created)> _descriptions = new();
    public event Action<TransferProgress>? Progress;
    private readonly ConcurrentDictionary<string, long> _progressTicks = new();
    private void Publish(TransferProgress progress)
    {
        if (progress.State == "传输中" && progress.Completed < progress.Total)
        {
            long now = Environment.TickCount64;
            if (_progressTicks.TryGetValue(progress.TransferId, out long last) && now - last < 100) return;
            _progressTicks[progress.TransferId] = now;
        }
        _descriptions.TryGetValue(progress.TransferId, out var info);
        _activePeers.TryGetValue(progress.TransferId, out string? peer);
        Progress?.Invoke(progress with { PeerId = peer, Name = info.Name ?? "文件", CreatedUtc = info.Created });
    }
    public event Action<string>? StatusChanged;
    public FileTransferNode(byte[] pairing, string directory, Func<FileOffer, IPAddress, CancellationToken, Task<bool>> confirm, int port = TransferProtocol.Port, DeviceHub? hub = null, HistoryStore? historyStore = null)
    {
        _hub = hub; _historyStore = historyStore; _deviceId = hub?.DeviceId ?? _deviceId;
        if (hub != null) hub.Revoked += Revoke;
        _key = TransferProtocol.DeriveKey(pairing); _directory = directory; _confirm = confirm;
        _listener = new TcpListener(IPAddress.Any, port); _listener.Start(4); _ = ListenAsync();
    }
    public void Cancel(string id) { if (_active.TryGetValue(id, out var source)) source.Cancel(); }
    public async Task SendAsync(IPAddress address, string[] paths, CancellationToken token, int port = TransferProtocol.Port, string? expectedPeerId = null)
    {
        if (!SharedProtocol.IsLocalAddress(address)) throw new ArgumentException("请输入局域网 IPv4 地址");
        if (paths.Length < 1 || paths.Length > 256) throw new ArgumentException("请选择 1 至 256 个文件");
        string id = Guid.NewGuid().ToString("N");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        _active[id] = lifetime;
        _descriptions[id] = (string.Join("、", paths.Select(Path.GetFileName)), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (_hub?.Nearby.Values.FirstOrDefault(p => p.Address == address.ToString() && p.Authenticated) is { } selected) _activePeers[id] = selected.Id;
        if (expectedPeerId != null) _activePeers[id] = expectedPeerId;
        bool slotAcquired = false;
        long completed = 0, total = 0;
        FileOffer? offer = null;
        string? authenticatedPeerId = null;
        bool terminalHistoryRecorded = false;
        try
        {
            Publish(new TransferProgress(id, "发送", "排队中", 0, 0));
            await _sendSlots.WaitAsync(lifetime.Token); slotAcquired = true;
            RequireActivePeer(id);
            var sources = await Task.Run(() => TransferSource.Expand(paths, lifetime.Token), lifetime.Token);
            var files = new TransferFile[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                Publish(new TransferProgress(id, "发送", "准备文件", i, sources.Length));
                if (sources[i].IsDirectory) { files[i] = new TransferFile(sources[i].Name, 0, TransferProtocol.EmptyHash, sources[i].RelativePath, true); continue; }
                using var input = new FileStream(sources[i].FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                files[i] = new TransferFile(sources[i].Name, input.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, lifetime.Token).ConfigureAwait(false)), sources[i].RelativePath);
            }
            offer = new FileOffer(sources.Any(x => x.IsDirectory) ? "fileTreeOffer" : "fileOffer", id, _deviceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), files);
            total = TransferProtocol.ValidateOffer(offer);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) { connect.CancelAfter(5000); await client.ConnectAsync(address, port, connect.Token); }
            using var closeOnCancel = lifetime.Token.Register(() => client.Dispose());
            var stream = client.GetStream();
            OutgoingSessionRoute route = _hub == null
                ? new OutgoingSessionRoute(_key, null)
                : await _hub.OutgoingRouteAsync(stream, address, 2, _key, lifetime.Token);
            if (expectedPeerId != null && route.PeerId != expectedPeerId) throw new IOException("目标设备已变化，请重新选择");
            byte[] key = route.Key;
            authenticatedPeerId = route.PeerId;
            if (authenticatedPeerId != null) _activePeers[id] = authenticatedPeerId;
            await TransferProtocol.WriteJsonAsync(stream, key, offer, lifetime.Token);
            Publish(new TransferProgress(id, "发送", "连接接收设备", 0, total));
            TransferReply acceptance = TransferProtocol.Decode<TransferReply>(await TransferProtocol.ReadAsync(stream, key, lifetime.Token, 120));
            if (acceptance.Type == "reject" && acceptance.TransferId == id)
            {
                RecordFileHistory(authenticatedPeerId, offer, "rejected");
                terminalHistoryRecorded = true;
            }
            TransferProtocol.RequireReply(acceptance, id, "accept");
            byte[] buffer = new byte[TransferProtocol.ChunkSize];
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].IsDirectory) continue;
                using var input = new FileStream(sources[i].FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); long offset = 0;
                while (offset < files[i].Size)
                {
                    RequireActivePeer(id);
                    int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, files[i].Size - offset)), lifetime.Token).ConfigureAwait(false);
                    if (count == 0) throw new IOException("发送文件在准备后发生变化");
                    await TransferProtocol.WriteAsync(stream, key, TransferProtocol.Chunk(id, i, offset, buffer.AsSpan(0, count)), lifetime.Token).ConfigureAwait(false);
                    offset += count; completed += count;
                    Publish(new TransferProgress(id, "发送", "传输中", completed, total));
                }
            }
            await TransferProtocol.WriteJsonAsync(stream, key, new TransferReply("finish", id), lifetime.Token);
            TransferProtocol.RequireReply(TransferProtocol.Decode<TransferReply>(await TransferProtocol.ReadAsync(stream, key, lifetime.Token, 120)), id, "complete");
            RecordFileHistory(authenticatedPeerId, offer, "sent", sources.Select(x => x.FullPath).ToArray());
            terminalHistoryRecorded = true;
            Publish(new TransferProgress(id, "发送", "对方已保存并校验", total, total));
        }
        catch
        {
            if (!terminalHistoryRecorded && offer != null) RecordFileHistory(authenticatedPeerId, offer, "failed");
            Publish(new TransferProgress(id, "发送", lifetime.IsCancellationRequested ? "已取消" : "失败", completed, total));
            throw;
        }
        finally { if (slotAcquired) _sendSlots.Release(); _active.TryRemove(id, out _); _activePeers.TryRemove(id, out _); _descriptions.TryRemove(id, out _); _progressTicks.TryRemove(id, out _); }
    }
    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !SharedProtocol.IsLocalAddress(peer.Address) || !_slots.Wait(0)) client.Dispose();
                else _ = ReceiveAsync(client, peer.Address);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || ex is SocketException) { }
    }
    private async Task ReceiveAsync(TcpClient client, IPAddress address)
    {
        string id = ""; long done = 0, total = 0;
        FileOffer? offer = null;
        string? authenticatedPeerId = null;
        bool terminalHistoryRecorded = false;
        string[]? committedPaths = null;
        using (client)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                var stream = client.GetStream();
                SessionRoute route;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                { handshake.CancelAfter(5000); route = _hub == null ? new SessionRoute(_key, null, null) : await _hub.IncomingAsync(stream, 2, _key, handshake.Token); }
                byte[] key = route.Key;
                FileOffer decodedOffer = TransferProtocol.Decode<FileOffer>(await TransferProtocol.ReadAsync(stream, key, lifetime.Token, firstHeader: route.Prefix));
                if (route.Source != null && route.Source != decodedOffer.OriginDeviceId) throw new CryptographicException("文件来源不符");
                total = TransferProtocol.ValidateOffer(decodedOffer);
                offer = decodedOffer;
                authenticatedPeerId = route.Source;
                if (authenticatedPeerId != null) _activePeers[decodedOffer.TransferId] = authenticatedPeerId;
                id = decodedOffer.TransferId;
                _descriptions[id] = (string.Join("、", offer.Files.Select(f => f.Name)), offer.TimestampUtc);
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var seen in _seen) if (now - seen.Value > 600000) _seen.TryRemove(seen.Key, out _);
                if (_seen.Count >= 2048 || !_seen.TryAdd(id, now)) throw new InvalidDataException("重复的文件请求");
                _active[id] = lifetime;
                Publish(new TransferProgress(id, "接收", "待接受", 0, total));
                bool accepted = authenticatedPeerId != null && _hub?.IsTrusted(authenticatedPeerId) == true;
                if (!accepted)
                    using (var decision = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        decision.CancelAfter(TimeSpan.FromSeconds(100));
                        try { accepted = await _confirm(offer, address, decision.Token).WaitAsync(decision.Token); }
                        catch (OperationCanceledException) { accepted = false; }
                    }
                RequireActivePeer(id);
                await TransferProtocol.WriteJsonAsync(stream, key, new TransferReply(accepted ? "accept" : "reject", id), lifetime.Token);
                if (!accepted)
                {
                    RecordFileHistory(authenticatedPeerId, offer, "rejected");
                    terminalHistoryRecorded = true;
                    Publish(new TransferProgress(id, "接收", "已拒绝或超时", 0, total));
                    return;
                }
                using var store = new ReceiveFileStore(_directory, offer.Files);
                while (true)
                {
                    RequireActivePeer(id);
                    byte[] plain = await TransferProtocol.ReadAsync(stream, key, lifetime.Token);
                    if (plain.Length > 0 && plain[0] == 1)
                    {
                        var chunk = TransferProtocol.ParseChunk(id, plain); store.Append(chunk.Index, chunk.Offset, chunk.Data); done += chunk.Data.Length;
                        Publish(new TransferProgress(id, "接收", "传输中", done, total));
                    }
                    else
                    {
                        TransferProtocol.RequireReply(TransferProtocol.Decode<TransferReply>(plain), id, "finish");
                        RequireActivePeer(id);
                        committedPaths = store.Commit(lifetime.Token);
                        await TransferProtocol.WriteJsonAsync(stream, key, new TransferReply("complete", id), lifetime.Token);
                        RecordFileHistory(authenticatedPeerId, offer, "sent", committedPaths);
                        terminalHistoryRecorded = true;
                        Publish(new TransferProgress(id, "接收", "已保存并校验", total, total)); break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is CryptographicException || ex is System.Text.Json.JsonException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException)
            {
                if (!_stop.IsCancellationRequested)
                {
                    if (!terminalHistoryRecorded && offer != null) RecordFileHistory(authenticatedPeerId, offer, "failed", committedPaths);
                    StatusChanged?.Invoke("文件接收未完成，请查看传输记录后重试");
                    Publish(new TransferProgress(id, "接收", lifetime.IsCancellationRequested ? "已取消" : "失败", done, total));
                }
            }
            finally { if (id.Length != 0) { _active.TryRemove(id, out _); _activePeers.TryRemove(id, out _); _descriptions.TryRemove(id, out _); _progressTicks.TryRemove(id, out _); } _slots.Release(); }
        }
    }
    private void RecordFileHistory(string? peerId, FileOffer offer, string state, string[]? localPaths = null)
    {
        if (peerId == null || _historyStore == null) return;
        var files = offer.Files.Select(file => new HistoryFileMetadata(file.Name, file.Size, file.Sha256, file.RelativePath, file.IsDirectory)).ToArray();
        var value = new HistorySyncEvent(HistoryPolicy.SchemaVersion, offer.TransferId, peerId, offer.OriginDeviceId,
            offer.TimestampUtc, "file", null, files, state);
        var references = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        if (localPaths != null)
            for (int index = 0; index < localPaths.Length; index++) references[index.ToString(CultureInfo.InvariantCulture)] = localPaths[index];
        try { _historyStore.Upsert(new HistoryLocalRecord(value, references)); }
        catch
        {
            try { StatusChanged?.Invoke("文件传输已结束，但历史记录保存失败"); } catch { }
        }
    }

    private void RequireActivePeer(string id) { if (_hub != null && _activePeers.TryGetValue(id, out var peer) && !_hub.IsTrusted(peer)) throw new IOException("配对已撤销"); }
    private void Revoke(string peer) { foreach (var p in _activePeers.Where(p => p.Value == peer)) Cancel(p.Key); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_hub != null) _hub.Revoked -= Revoke;
        _stop.Cancel(); _listener.Stop();
    }
}
