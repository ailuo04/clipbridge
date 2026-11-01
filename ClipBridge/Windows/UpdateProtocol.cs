using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal static class UpdateProtocol
{
	internal const string DefaultManifestUrl = "https://raw.githubusercontent.com/ailuo04/clipbridge/master/latest-v2.json";

	private const string PublicKeyBase64 = "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAqLueWEm2miyzy6IHBjOP6Q/eiHBi7J+fWoqtKeGsdrMb0nh5Sp19QIJtPT98eDZEc0p5gbGizbuBYCskiygavqssGSOODlv7NefcP9JT4fEEbfge+48bq/YwEZd06QID1Q3dGldRYrW0rJNF/jvHOMGHwRTWGWxBvi2B1dJonCwJhQX/zM/lolRVJKn4ZgfGYGgymzG1o8JWy5l/gp8j2ltXVpm/onPDbU3BaF0q3lkxFlIq2rrzeadvEtLztBidgPH/4YH/v0aPVvCR0XdbyF8YgylXf4ke8oM7C1ktvgGX+u3jZyORMeeYc1rO4SHe8tmSTjTnfvGrzYQEV44A+wsNWM88KnsuHtFxqi6Lg0YXBmcNAvmkCrClzIV0eJU+gko88yzT4FZaHnwe5Fe1fNx+1wN2jWlhzlIsgWN0dA6qcUue3JXnbJ8w1PSiV8JI1HaPUN6tildqAI4q1erCD95C/uHWZtZhmLMLL+OgdkmI+1ht2ze82CY5TlI5XXWdAgMBAAE=";

	private const int MaxManifestBytes = 1048576;

	private const long MaxPackageBytes = 209715200L;

	internal static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

	internal static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
	{
		string text = Environment.GetEnvironmentVariable("CLIPBRIDGE_UPDATE_URL");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = DefaultManifestUrl;
		}
		EnsureTrustedUrl(text, "更新清单地址");
		using HttpClient client = CreateHttpClient();
		using HttpResponseMessage response = await client.GetAsync(text, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.StatusCode != HttpStatusCode.OK)
		{
			throw new UpdateProtocolException("更新清单请求失败：HTTP " + (int)response.StatusCode);
		}
		return ParseAndVerify(await ReadLimitedStringAsync(response.Content, 1048576, cancellationToken));
	}

	internal static AvailableUpdate? ParseAndVerify(string envelopeJson)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(envelopeJson, new JsonDocumentOptions
			{
				MaxDepth = 16
			});
			JsonElement rootElement = jsonDocument.RootElement;
			string s = RequiredString(rootElement, "payload");
			string s2 = RequiredString(rootElement, "signature");
			byte[] array = Convert.FromBase64String(s);
			byte[] signature = Convert.FromBase64String(s2);
			using RSA rSA = RSA.Create();
			rSA.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
			bool validSignature = rSA.VerifyData(array, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			if (!validSignature)
			{
				using Stream publicKeyStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClipBridge.release-signing-public-key.txt")
					?? throw new UpdateProtocolException("更新签名公钥不可用");
				using StreamReader publicKeyReader = new StreamReader(publicKeyStream);
				using RSA rotatedKey = RSA.Create();
				rotatedKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyReader.ReadToEnd().Trim()), out _);
				validSignature = rotatedKey.VerifyData(array, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			}
			if (!validSignature)
			{
				throw new UpdateProtocolException("更新清单签名无效");
			}
			using JsonDocument jsonDocument2 = JsonDocument.Parse((ReadOnlyMemory<byte>)array, new JsonDocumentOptions
			{
				MaxDepth = 16
			});
			JsonElement rootElement2 = jsonDocument2.RootElement;
			if (rootElement2.GetProperty("schema").GetInt32() != 1)
			{
				throw new UpdateProtocolException("不支持的更新清单格式");
			}
			if (!rootElement2.TryGetProperty("windows", out var value) || value.ValueKind == JsonValueKind.Null)
			{
				return null;
			}
			string text = RequiredString(value, "version");
			if (!Version.TryParse(text, out Version result))
			{
				throw new UpdateProtocolException("更新版本号格式无效");
			}
			if (result <= CurrentVersion)
			{
				return null;
			}
			string text2 = RequiredString(value, "url");
			EnsureTrustedUrl(text2, "安装包地址");
			byte[] array2 = Convert.FromHexString(RequiredString(value, "sha256"));
			if (array2.Length != 32)
			{
				throw new UpdateProtocolException("安装包 SHA-256 格式无效");
			}
			long @int = value.GetProperty("size").GetInt64();
			if (@int <= 0 || @int > 209715200)
			{
				throw new UpdateProtocolException("安装包大小超出允许范围");
			}
			string text3 = (value.TryGetProperty("notes", out var value2) ? (value2.GetString() ?? "") : "");
			if (text3.Length > 4000)
			{
				text3 = text3.Substring(0, 4000);
			}
			return new AvailableUpdate(text, text2, @int, Convert.ToHexString(array2), text3);
		}
		catch (UpdateProtocolException)
		{
			throw;
		}
		catch (Exception ex2) when ((ex2 is JsonException || ex2 is FormatException || ex2 is InvalidOperationException || ex2 is CryptographicException || ex2 is KeyNotFoundException) ? true : false)
		{
			throw new UpdateProtocolException("更新清单格式或签名无效", ex2);
		}
	}

	internal static async Task DownloadAsync(AvailableUpdate update, string destinationPath, CancellationToken cancellationToken)
	{
		EnsureTrustedUrl(update.DownloadUrl, "安装包地址");
		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
		string temporaryPath = destinationPath + ".part";
		try
		{
			using HttpClient client = CreateHttpClient();
			using HttpResponseMessage response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (response.StatusCode != HttpStatusCode.OK)
			{
				throw new UpdateProtocolException("安装包下载失败：HTTP " + (int)response.StatusCode);
			}
			long? contentLength = response.Content.Headers.ContentLength;
			if (contentLength.HasValue)
			{
				long valueOrDefault = contentLength.GetValueOrDefault();
				if (valueOrDefault != update.Size)
				{
					throw new UpdateProtocolException("安装包长度与更新清单不一致");
				}
			}
			await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
			await using FileStream output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			byte[] buffer = new byte[81920];
			long total = 0L;
			int num;
			while ((num = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
			{
				total += num;
				if (total > update.Size)
				{
					throw new UpdateProtocolException("安装包长度与更新清单不一致");
				}
				hash.AppendData(buffer, 0, num);
				await output.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
			}
			if (total != update.Size)
			{
				throw new UpdateProtocolException("安装包长度与更新清单不一致");
			}
			string x = Convert.ToHexString(hash.GetHashAndReset());
			if (!StringComparer.OrdinalIgnoreCase.Equals(x, update.Sha256))
			{
				throw new UpdateProtocolException("安装包 SHA-256 校验失败");
			}
			output.Close();
			File.Move(temporaryPath, destinationPath, overwrite: true);
		}
		finally
		{
			try
			{
				if (File.Exists(temporaryPath))
				{
					File.Delete(temporaryPath);
				}
			}
			catch (IOException)
			{
			}
		}
	}

	private static HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient(new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			ConnectTimeout = TimeSpan.FromSeconds(10.0),
			AllowAutoRedirect = true,
			MaxAutomaticRedirections = 5
		});
		httpClient.Timeout = TimeSpan.FromMinutes(5.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ClipBridge-Windows/0.2");
		httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		return httpClient;
	}

	private static async Task<string> ReadLimitedStringAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
	{
		long? contentLength = content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault > maxBytes)
			{
				throw new UpdateProtocolException("更新清单过大");
			}
		}
		string result;
		await using (Stream input = await content.ReadAsStreamAsync(cancellationToken))
		{
			using MemoryStream output = new MemoryStream();
			byte[] buffer = new byte[8192];
			int num;
			while ((num = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
			{
				if (output.Length + num > maxBytes)
				{
					throw new UpdateProtocolException("更新清单过大");
				}
				output.Write(buffer, 0, num);
			}
			result = Encoding.UTF8.GetString(output.ToArray());
		}
		return result;
	}

	private static string RequiredString(JsonElement element, string property)
	{
		if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			throw new UpdateProtocolException("更新清单缺少字段：" + property);
		}
		string? text = value.GetString();
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new UpdateProtocolException("更新清单字段为空：" + property);
		}
		return text;
	}

	private static void EnsureTrustedUrl(string value, string fieldName)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri result))
		{
			throw new UpdateProtocolException(fieldName + "无效");
		}
		if (result.Scheme == Uri.UriSchemeHttps || (result.Scheme == Uri.UriSchemeHttp && result.IsLoopback))
		{
			return;
		}
		throw new UpdateProtocolException(fieldName + "必须使用 HTTPS");
	}
}
