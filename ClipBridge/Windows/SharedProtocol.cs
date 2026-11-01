using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipBridge.Windows;

internal static class SharedProtocol
{
	public const int DiscoveryPort = 45821;

	public const int TcpPort = 45822;

	public const int MaxTextBytes = 262144;

	public const int MaxFrameBytes = 1581056;

	private const int NonceSize = 12;

	private const int TagSize = 16;

	private static readonly byte[] Domain = Encoding.UTF8.GetBytes("ClipBridge/v1\0");

	public static byte[] DeriveKey(byte[] pairingKey)
	{
		if (pairingKey.Length != 32)
		{
			throw new ArgumentException("Pairing key must be 32 bytes.", "pairingKey");
		}
		return SHA256.HashData(Domain.Concat(pairingKey).ToArray());
	}

	public static byte[] Encrypt(byte[] key, byte[] plaintext)
	{
		byte[] bytes = RandomNumberGenerator.GetBytes(12);
		byte[] array = new byte[plaintext.Length];
		byte[] array2 = new byte[16];
		using (AesGcm aesGcm = new AesGcm(key, 16))
		{
			aesGcm.Encrypt(bytes, plaintext, array, array2);
		}
		return bytes.Concat(array).Concat(array2).ToArray();
	}

	public static byte[] Decrypt(byte[] key, byte[] frame)
	{
		if (frame.Length < 28 || frame.Length > 1581056)
		{
			throw new InvalidDataException("Invalid encrypted frame size.");
		}
		int num = frame.Length - 12 - 16;
		byte[] array = new byte[num];
		using AesGcm aesGcm = new AesGcm(key, 16);
		aesGcm.Decrypt(frame.AsSpan(0, 12), frame.AsSpan(12, num), frame.AsSpan(12 + num, 16), array);
		return array;
	}

	public static byte[] Frame(byte[] encryptedPayload)
	{
		byte[] array = new byte[4 + encryptedPayload.Length];
		BinaryPrimitives.WriteInt32BigEndian(array.AsSpan(0, 4), encryptedPayload.Length);
		Buffer.BlockCopy(encryptedPayload, 0, array, 4, encryptedPayload.Length);
		return array;
	}

	public static int ReadFrameLength(byte[] header)
	{
		return BinaryPrimitives.ReadInt32BigEndian(header);
	}

	public static byte[] Serialize<T>(T value)
	{
		return JsonSerializer.SerializeToUtf8Bytes(value);
	}

	public static bool IsFresh(long timestamp)
	{
		return Math.Abs((double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (double)timestamp) <= 120000.0;
	}

	public static bool IsLocalAddress(IPAddress address)
	{
		byte[] addressBytes = address.GetAddressBytes();
		bool flag = addressBytes.Length == 4;
		int num;
		if (flag)
		{
			byte b = addressBytes[0];
			if ((b != 10 && b != 127) || 1 == 0)
			{
				if (addressBytes[0] == 172)
				{
					byte b2 = addressBytes[1];
					if (b2 >= 16 && b2 <= 31)
					{
						goto IL_0070;
					}
				}
				if (addressBytes[0] != 192 || addressBytes[1] != 168)
				{
					num = ((addressBytes[0] == 169 && addressBytes[1] == 254) ? 1 : 0);
					goto IL_0071;
				}
			}
			goto IL_0070;
		}
		goto IL_0072;
		IL_0071:
		flag = (byte)num != 0;
		goto IL_0072;
		IL_0070:
		num = 1;
		goto IL_0071;
		IL_0072:
		return flag;
	}
}
