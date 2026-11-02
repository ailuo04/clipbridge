using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ClipBridge.Windows;

internal sealed class PairingMaterial : IDisposable
{
    internal readonly ECDiffieHellman Key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    public string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
    public string Nonce { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public void Dispose() => Key.Dispose();
}

internal static class PairingProtocol
{
    public const int Port = 45824;
    public static bool ValidId(string id) => id != null && id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static byte[] Fields(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (string value in values)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            stream.Write(length); stream.Write(bytes);
        }
        return stream.ToArray();
    }
    public static byte[] Hmac(byte[] key, byte[] value) => HMACSHA256.HashData(key, value);
    public static byte[] Hkdf(byte[] secret, byte[] salt, byte[] info) => Hmac(Hmac(salt, secret), info.Concat(new byte[] { 1 }).ToArray());
    public static string Commitment(string session, string role, string id, string name, string publicKey, string nonce)
    {
        if (!ValidId(session) || !ValidId(id) || name.Length is < 1 or > 80 || name.Any(char.IsControl) || nonce.Length != 64 || (role != "i" && role != "r")) throw new InvalidDataException("无效的配对材料");
        return Convert.ToHexString(SHA256.HashData(Fields("ClipBridge/pair/v2", session, role, id, name, publicKey, nonce))).ToLowerInvariant();
    }
    public static void Verify(string expected, string actual)
    {
        if (expected.Length != 64 || actual.Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual))) throw new CryptographicException("配对承诺校验失败");
    }
    public static byte[] Transcript(string session, string i, string iName, string iPublic, string iNonce, string r, string rName, string rPublic, string rNonce) =>
        Fields("ClipBridge/pair/transcript/v2", session, i, iName, iPublic, iNonce, r, rName, rPublic, rNonce);
    public static byte[] SessionKey(PairingMaterial local, string remotePublic, byte[] transcript)
    {
        byte[] encoded = Convert.FromBase64String(remotePublic);
        if (encoded.Length > 160) throw new CryptographicException("无效的配对公钥");
        using var remote = ECDiffieHellman.Create(); remote.ImportSubjectPublicKeyInfo(encoded, out int used);
        var parameters = remote.ExportParameters(false);
        if (used != encoded.Length || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new CryptographicException("仅支持 P-256 配对");
        byte[] secret = local.Key.DeriveRawSecretAgreement(remote.PublicKey);
        try { return Hkdf(secret, SHA256.HashData(transcript), Encoding.UTF8.GetBytes("ClipBridge/pair/session/v2")); }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    public static string Code(byte[] key, byte[] transcript) => (BinaryPrimitives.ReadUInt32BigEndian(Hmac(key, transcript)) % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
