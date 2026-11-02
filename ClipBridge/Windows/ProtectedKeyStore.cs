using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClipBridge.Windows;

internal static class ProtectedKeyStore
{
	private struct DataBlob
	{
		public int Size;

		public nint Data;
	}

	private static readonly string KeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "pairing.bin");

	public static byte[] LoadOrCreate()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(KeyPath));
		if (File.Exists(KeyPath))
		{
			byte[] array = Unprotect(File.ReadAllBytes(KeyPath));
			if (array.Length != 32)
			{
				throw new CryptographicException("Invalid stored pairing key.");
			}
			return array;
		}
		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		string text = KeyPath + ".tmp";
		File.WriteAllBytes(text, Protect(bytes));
		File.Move(text, KeyPath, overwrite: false);
		return bytes;
	}

	internal static byte[] Protect(byte[] value)
	{
		return Transform(value, protect: true);
	}

	internal static byte[] Unprotect(byte[] value)
	{
		return Transform(value, protect: false);
	}

	private static byte[] Transform(byte[] value, bool protect)
	{
		nint num = Marshal.AllocHGlobal(value.Length);
		Marshal.Copy(value, 0, num, value.Length);
		DataBlob input = new DataBlob
		{
			Size = value.Length,
			Data = num
		};
		DataBlob output = default;
		try
		{
			if (!(protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)))
			{
				throw new CryptographicException(Marshal.GetLastWin32Error());
			}
			byte[] array = new byte[output.Size];
			Marshal.Copy(output.Data, array, 0, output.Size);
			return array;
		}
		finally
		{
			Marshal.FreeHGlobal(num);
			if (output.Data != IntPtr.Zero)
			{
				LocalFree(output.Data);
			}
		}
	}

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptProtectData(ref DataBlob input, string? description, nint optionalEntropy, nint reserved, nint prompt, int flags, out DataBlob output);

	[DllImport("crypt32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint optionalEntropy, nint reserved, nint prompt, int flags, out DataBlob output);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint memory);
}
