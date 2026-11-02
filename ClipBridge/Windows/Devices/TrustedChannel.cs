using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ClipBridge.Windows;

internal static class TrustedChannel
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("CBV2");
    public const int HeaderSize = 65;
    public static byte[] Header(string source, string target, byte kind, byte[] pairing)
    {
        if (!PairingProtocol.ValidId(source) || !PairingProtocol.ValidId(target) || kind is not (1 or 2)) throw new InvalidDataException("无效的会话路由");
        byte[] header = Magic.Concat(new[] { kind }).Concat(Convert.FromHexString(source)).Concat(Convert.FromHexString(target)).ToArray();
        byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16];
        using var aes = new AesGcm(RouteKey(pairing), 16); aes.Encrypt(nonce, Array.Empty<byte>(), Array.Empty<byte>(), tag, header);
        return header.Concat(nonce).Concat(tag).ToArray();
    }
    private static byte[] RouteKey(byte[] pairing) => PairingProtocol.Hkdf(pairing, new byte[32], Encoding.UTF8.GetBytes("ClipBridge/route/v2"));
    public static string Source(byte[] header) => Convert.ToHexString(header.AsSpan(5, 16)).ToLowerInvariant();
    public static byte[] Open(byte[] header, string target, byte kind, byte[] pairing)
    {
        if (header.Length != HeaderSize || !header.AsSpan(0, 4).SequenceEqual(Magic) || header[4] != kind || !header.AsSpan(21, 16).SequenceEqual(Convert.FromHexString(target))) throw new CryptographicException("会话目标不符");
        using var aes = new AesGcm(RouteKey(pairing), 16);
        aes.Decrypt(header.AsSpan(37, 12), ReadOnlySpan<byte>.Empty, header.AsSpan(49, 16), Span<byte>.Empty, header.AsSpan(0, 37));
        return PairingProtocol.Hkdf(pairing, header.AsSpan(37, 12).ToArray(), header.AsSpan(0, 37).ToArray());
    }
}
