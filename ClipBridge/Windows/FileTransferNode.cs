using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal sealed class FileTransferNode : IDisposable
{
    private readonly byte[] _key;
    private readonly string _directory;
    private readonly string _deviceId = Guid.NewGuid().ToString("N");
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(2);
    private readonly Func<FileOffer, IPAddress, CancellationToken, Task<bool>> _confirm;
    private readonly ConcurrentDictionary<string, long> _seen = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private bool _disposed;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public event Action<TransferProgress>? Progress;
    public event Action<string>? StatusChanged;
    public FileTransferNode(byte[] pairing, string directory, Func<FileOffer, IPAddress, CancellationToken, Task<bool>> confirm, int port = TransferProtocol.Port)
    {
        _key = TransferProtocol.DeriveKey(pairing); _directory = directory; _confirm = confirm;
        _listener = new TcpListener(IPAddress.Any, port); _listener.Start(4); _ = ListenAsync();
    }
    public void Cancel(string id) { if (_active.TryGetValue(id, out var source)) source.Cancel(); }
    public async Task SendAsync(IPAddress address, string[] paths, CancellationToken token, int port = TransferProtocol.Port)
    {
        if (!SharedProtocol.IsLocalAddress(address)) throw new ArgumentException("请输入局域网 IPv4 地址");
        if (paths.Length < 1 || paths.Length > 256) throw new ArgumentException("请选择 1 至 256 个文件");
        string id = Guid.NewGuid().ToString("N");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        _active[id] = lifetime;
        long completed = 0, total = 0;
        try
        {
            var files = new TransferFile[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                Progress?.Invoke(new TransferProgress(id, "发送", "准备文件", i, paths.Length));
                using var input = File.OpenRead(paths[i]);
                files[i] = new TransferFile(Path.GetFileName(paths[i]), input.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, lifetime.Token)));
            }
            var offer = new FileOffer("fileOffer", id, _deviceId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), files);
            total = TransferProtocol.ValidateOffer(offer);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) { connect.CancelAfter(5000); await client.ConnectAsync(address, port, connect.Token); }
            using var closeOnCancel = lifetime.Token.Register(() => client.Dispose());
            var stream = client.GetStream();
            await TransferProtocol.WriteJsonAsync(stream, _key, offer, lifetime.Token);
            Progress?.Invoke(new TransferProgress(id, "发送", "等待对方接受", 0, total));
            TransferProtocol.RequireReply(TransferProtocol.Decode<TransferReply>(await TransferProtocol.ReadAsync(stream, _key, lifetime.Token, 120)), id, "accept");
            byte[] buffer = new byte[TransferProtocol.ChunkSize];
            for (int i = 0; i < paths.Length; i++)
            {
                using var input = File.OpenRead(paths[i]); long offset = 0;
                while (offset < files[i].Size)
                {
                    int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, files[i].Size - offset)), lifetime.Token);
                    if (count == 0) throw new IOException("发送文件在准备后发生变化");
                    await TransferProtocol.WriteAsync(stream, _key, TransferProtocol.Chunk(id, i, offset, buffer.AsSpan(0, count)), lifetime.Token);
                    offset += count; completed += count;
                    Progress?.Invoke(new TransferProgress(id, "发送", "传输中", completed, total));
                }
            }
            await TransferProtocol.WriteJsonAsync(stream, _key, new TransferReply("finish", id), lifetime.Token);
            TransferProtocol.RequireReply(TransferProtocol.Decode<TransferReply>(await TransferProtocol.ReadAsync(stream, _key, lifetime.Token, 120)), id, "complete");
            Progress?.Invoke(new TransferProgress(id, "发送", "对方已保存并校验", total, total));
        }
        catch { Progress?.Invoke(new TransferProgress(id, "发送", lifetime.IsCancellationRequested ? "已取消" : "失败", completed, total)); throw; }
        finally { _active.TryRemove(id, out _); }
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
        using (client)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                var stream = client.GetStream();
                var offer = TransferProtocol.Decode<FileOffer>(await TransferProtocol.ReadAsync(stream, _key, lifetime.Token));
                total = TransferProtocol.ValidateOffer(offer); id = offer.TransferId;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var seen in _seen) if (now - seen.Value > 600000) _seen.TryRemove(seen.Key, out _);
                if (_seen.Count >= 2048 || !_seen.TryAdd(id, now)) throw new InvalidDataException("重复的文件请求");
                _active[id] = lifetime;
                Progress?.Invoke(new TransferProgress(id, "接收", "待接受", 0, total));
                bool accepted;
                using (var decision = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    decision.CancelAfter(TimeSpan.FromSeconds(100));
                    try { accepted = await _confirm(offer, address, decision.Token).WaitAsync(decision.Token); }
                    catch (OperationCanceledException) { accepted = false; }
                }
                await TransferProtocol.WriteJsonAsync(stream, _key, new TransferReply(accepted ? "accept" : "reject", id), lifetime.Token);
                if (!accepted) { Progress?.Invoke(new TransferProgress(id, "接收", "已拒绝或超时", 0, total)); return; }
                using var store = new ReceiveFileStore(_directory, offer.Files);
                while (true)
                {
                    byte[] plain = await TransferProtocol.ReadAsync(stream, _key, lifetime.Token);
                    if (plain.Length > 0 && plain[0] == 1)
                    {
                        var chunk = TransferProtocol.ParseChunk(id, plain); store.Append(chunk.Index, chunk.Offset, chunk.Data); done += chunk.Data.Length;
                        Progress?.Invoke(new TransferProgress(id, "接收", "传输中", done, total));
                    }
                    else
                    {
                        TransferProtocol.RequireReply(TransferProtocol.Decode<TransferReply>(plain), id, "finish");
                        store.Commit(lifetime.Token);
                        await TransferProtocol.WriteJsonAsync(stream, _key, new TransferReply("complete", id), lifetime.Token);
                        Progress?.Invoke(new TransferProgress(id, "接收", "已保存并校验", total, total)); break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is CryptographicException || ex is System.Text.Json.JsonException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException)
            {
                if (!_stop.IsCancellationRequested) { StatusChanged?.Invoke("文件接收未完成，请查看传输记录后重试"); Progress?.Invoke(new TransferProgress(id, "接收", lifetime.IsCancellationRequested ? "已取消" : "失败", done, total)); }
            }
            finally { if (id.Length != 0) _active.TryRemove(id, out _); _slots.Release(); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _stop.Cancel(); _listener.Stop();
    }
}
