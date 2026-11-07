using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBridge.Windows;

internal sealed record DiscoveryPacket(string Type, string Id, string Name, string Platform, int Port, long Time, string Nonce, string Target = "", string Mac = "", [property: JsonPropertyName("historySyncVersion")] int HistorySyncVersion = 0);
internal static class DiscoveryProtocol
{
    public const int SupportedHistorySyncVersion = 1;
    private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("CBD2");
    public static byte[] Encode(DiscoveryPacket packet) => Prefix.Concat(JsonSerializer.SerializeToUtf8Bytes(packet)).ToArray();
    public static DiscoveryPacket Create(string id, string name, string platform, string target = "", byte[]? key = null)
    {
        var p = new DiscoveryPacket(target.Length == 0 ? "discover" : "presence", id, name, platform, PairingProtocol.Port, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("N"), target, HistorySyncVersion: SupportedHistorySyncVersion);
        return key == null ? p : p with { Mac = Sign(p, key) };
    }
    public static bool IsPublic(byte[] data) => data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual(Prefix);
    public static DiscoveryPacket Parse(byte[] data)
    {
        if (data.Length > 2048 || !IsPublic(data)) throw new InvalidDataException("无效的发现包");
        var p = JsonSerializer.Deserialize<DiscoveryPacket>(data.AsSpan(4)) ?? throw new InvalidDataException();
        if (p.Type is not ("discover" or "presence") || !PairingProtocol.ValidId(p.Id) || p.Name == null || p.Name.Length is < 1 or > 80 || p.Name.Any(char.IsControl) || p.Platform is not ("windows" or "android") || p.Port != PairingProtocol.Port || !SharedProtocol.IsFresh(p.Time) || !PairingProtocol.ValidId(p.Nonce) || p.Target == null || p.Mac == null || p.HistorySyncVersion < 0 || (p.Type == "presence" && (!PairingProtocol.ValidId(p.Target) || p.Mac.Length != 64)) || (p.Type == "discover" && (p.Target.Length != 0 || p.Mac.Length != 0))) throw new InvalidDataException("无效或过期的发现包");
        return p;
    }
    private static string Sign(DiscoveryPacket p, byte[] key) => Convert.ToHexString(PairingProtocol.Hmac(key, PairingProtocol.Fields("ClipBridge/presence/v2", p.Id, p.Target, p.Name, p.Platform, p.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Time.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Nonce))).ToLowerInvariant();
    public static bool Authenticate(DiscoveryPacket p, string self, byte[] key) => p.Type == "presence" && p.Target == self && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p.Mac), Encoding.ASCII.GetBytes(Sign(p, key)));
}
