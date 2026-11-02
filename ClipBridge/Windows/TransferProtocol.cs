using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal sealed record TransferFile(string Name, long Size, string Sha256);
internal sealed record FileOffer(string Type, string TransferId, string OriginDeviceId, long TimestampUtc, TransferFile[] Files);
internal sealed record TransferReply(string Type, string TransferId);
internal sealed record TransferProgress(string TransferId, string Direction, string State, long Completed, long Total);

internal static class TransferProtocol
{
    public const int Port = 45823;
    public const int ChunkSize = 65536;
    public const int MaxControlBytes = 262144;
    public const long MaxTotalBytes = 1L << 40;
    public static byte[] DeriveKey(byte[] pairing)
    {
        if (pairing.Length != 32) throw new ArgumentException("Invalid pairing key");
        return SHA256.HashData(Encoding.UTF8.GetBytes("ClipBridge/files/v1\0").Concat(pairing).ToArray());
    }
    public static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || Encoding.UTF8.GetByteCount(name) > 220 || name.IndexOfAny(new[] { (char)60, (char)62, (char)58, (char)34, (char)47, (char)92, (char)124, (char)63, (char)42 }) >= 0 || name.Any(c => c < 32)) throw new InvalidDataException("不安全的文件名");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" }.Contains(stem) || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '0' && stem[3] <= '9')) throw new InvalidDataException("不支持的文件名");
    }
    public static long ValidateOffer(FileOffer offer)
    {
        if (offer.Type != "fileOffer" || offer.TransferId == null || offer.TransferId.Length != 32 || !offer.TransferId.All(Uri.IsHexDigit) || string.IsNullOrEmpty(offer.OriginDeviceId) || offer.OriginDeviceId.Length > 64 || !SharedProtocol.IsFresh(offer.TimestampUtc) || offer.Files == null || offer.Files.Length < 1 || offer.Files.Length > 256) throw new InvalidDataException("无效的文件请求");
        long total = 0;
        foreach (var file in offer.Files)
        {
            if (file == null) throw new InvalidDataException("无效的文件信息");
            ValidateFileName(file.Name);
            if (file.Size < 0 || file.Size > MaxTotalBytes || file.Sha256 == null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("无效的文件信息");
            total = checked(total + file.Size);
            if (total > MaxTotalBytes) throw new InvalidDataException("文件总大小超过限制");
        }
        return total;
    }
    public static byte[] Chunk(string id, int index, long offset, ReadOnlySpan<byte> data)
    {
        if (data.Length < 1 || data.Length > ChunkSize) throw new InvalidDataException("Invalid chunk size");
        byte[] result = new byte[29 + data.Length]; result[0] = 1;
        Convert.FromHexString(id).CopyTo(result, 1);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(17, 4), index);
        BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(21, 8), offset);
        data.CopyTo(result.AsSpan(29)); return result;
    }
    public static (int Index, long Offset, byte[] Data) ParseChunk(string id, byte[] plain)
    {
        if (plain.Length <= 29 || plain.Length > ChunkSize + 29 || plain[0] != 1 || !plain.AsSpan(1, 16).SequenceEqual(Convert.FromHexString(id))) throw new InvalidDataException("无效文件分块");
        return (BinaryPrimitives.ReadInt32BigEndian(plain.AsSpan(17, 4)), BinaryPrimitives.ReadInt64BigEndian(plain.AsSpan(21, 8)), plain.AsSpan(29).ToArray());
    }
    public static async Task WriteAsync(Stream stream, byte[] key, byte[] plain, CancellationToken token)
    {
        if (plain.Length > MaxControlBytes) throw new InvalidDataException("Frame too large");
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(30));
        await stream.WriteAsync(SharedProtocol.Frame(SharedProtocol.Encrypt(key, plain)), idle.Token);
    }
    public static Task WriteJsonAsync<T>(Stream stream, byte[] key, T value, CancellationToken token) => WriteAsync(stream, key, JsonSerializer.SerializeToUtf8Bytes(value), token);
    public static async Task<byte[]> ReadAsync(Stream stream, byte[] key, CancellationToken token, int timeoutSeconds = 30, byte[]? firstHeader = null)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        byte[] header = firstHeader ?? new byte[4]; if (firstHeader == null) await stream.ReadExactlyAsync(header, idle.Token);
        int size = SharedProtocol.ReadFrameLength(header);
        if (size < 28 || size > MaxControlBytes + 28) throw new InvalidDataException("Invalid frame size");
        byte[] data = new byte[size]; await stream.ReadExactlyAsync(data, idle.Token);
        return SharedProtocol.Decrypt(key, data);
    }
    public static T Decode<T>(byte[] data) => JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Invalid message");
    public static void RequireReply(TransferReply reply, string id, string type)
    {
        if (reply.TransferId != id || reply.Type != type) throw new IOException(reply.Type == "reject" ? "对方已拒绝或未及时接受" : "传输未得到确认");
    }
}
