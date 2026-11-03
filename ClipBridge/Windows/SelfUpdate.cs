using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal static class SelfUpdate
{
	private sealed record UpdateOptions(int ProcessId, string Source, string Target, string DotnetPath);

	internal const string ApplyArgument = "--apply-update";

	private const string DllName = "ClipBridge.dll";

	private const string RuntimeConfigName = "ClipBridge.runtimeconfig.json";

	private static string AppDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge");

	internal static bool IsApplyMode(string[] args)
	{
		if (args.Length != 0)
		{
			return string.Equals(args[0], "--apply-update", StringComparison.Ordinal);
		}
		return false;
	}

	internal static Task<string> StagePackageAsync(AvailableUpdate update, CancellationToken cancellationToken)
	{
		return StagePackageAsync(update, cancellationToken, AppDataRoot);
	}

	internal static async Task<string> StagePackageAsync(AvailableUpdate update, CancellationToken cancellationToken, string appDataRoot)
	{
		string path = Path.Combine(appDataRoot, "updates");
		string text = string.Concat(update.Version.Select((char character) =>
		{
			bool flag = char.IsLetterOrDigit(character);
			if (!flag)
			{
				bool flag2 = ((character == '-' || character == '.' || character == '_') ? true : false);
				flag = flag2;
			}
			return (!flag) ? '_' : character;
		}));
		string packageRoot = Path.Combine(path, text + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(packageRoot);
		string archivePath = Path.Combine(packageRoot, "update.zip");
		string stagedDirectory = Path.Combine(packageRoot, "package");
		try
		{
			await UpdateProtocol.DownloadAsync(update, archivePath, cancellationToken);
			Directory.CreateDirectory(stagedDirectory);
			ExtractSafely(archivePath, stagedDirectory);
			if (!File.Exists(Path.Combine(stagedDirectory, "ClipBridge.dll")) || !File.Exists(Path.Combine(stagedDirectory, "ClipBridge.runtimeconfig.json")))
			{
				throw new UpdateProtocolException("更新包缺少 ClipBridge 运行文件");
			}
			File.Delete(archivePath);
			return stagedDirectory;
		}
		catch
		{
			try
			{
				Directory.Delete(packageRoot, recursive: true);
			}
			catch (IOException)
			{
			}
			throw;
		}
	}

	internal static void LaunchUpdater(string stagedDirectory)
	{
		LaunchUpdater(stagedDirectory, AppDataRoot);
	}

	internal static void LaunchUpdater(string stagedDirectory, string appDataRoot)
	{
		string text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
		string location = Assembly.GetExecutingAssembly().Location;
		string text2 = Path.Combine(text, "ClipBridge.runtimeconfig.json");
		if (!File.Exists(location) || !File.Exists(text2))
		{
			throw new UpdateProtocolException("无法定位当前 ClipBridge 运行文件");
		}
		string text3 = Path.Combine(appDataRoot, "updater", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text3);
		string text4 = Path.Combine(text3, "ClipBridge.dll");
		File.Copy(location, text4);
		File.Copy(text2, Path.Combine(text3, "ClipBridge.runtimeconfig.json"));
        foreach (string dependency in new[] { "ClipBridge.deps.json", "QRCoder.dll" })
        {
            string source = Path.Combine(text, dependency);
            if (File.Exists(source)) File.Copy(source, Path.Combine(text3, dependency));
        }
		string runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
		string? dotnetRoot = new DirectoryInfo(runtimeDirectory).Parent?.Parent?.Parent?.FullName;
		string text5 = Path.Combine(dotnetRoot ?? string.Empty, "dotnet.exe");
		if (!File.Exists(text5))
		{
			throw new UpdateProtocolException("无法找到 .NET 运行环境，更新未安装");
		}
		if (Process.Start(new ProcessStartInfo(text5)
		{
			UseShellExecute = false,
			WorkingDirectory = text3,
			CreateNoWindow = true,
			ArgumentList =
			{
				text4,
				"--apply-update",
				"--pid",
				Environment.ProcessId.ToString(),
				"--source",
				stagedDirectory,
				"--target",
				text,
				"--dotnet",
				text5
			}
		}) == null)
		{
			throw new UpdateProtocolException("无法启动更新辅助进程");
		}
	}

	internal static int Apply(string[] args)
	{
		return Apply(args, AppDataRoot);
	}

	internal static int Apply(string[] args, string appDataRoot)
	{
		string path = Path.Combine(appDataRoot, "update.log");
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		try
		{
			UpdateOptions updateOptions = ParseArguments(args);
			Log(path, "开始更新，等待主程序退出。PID=" + updateOptions.ProcessId);
			WaitForProcessExit(updateOptions.ProcessId, TimeSpan.FromSeconds(30.0));
			string fullPath = Path.GetFullPath(updateOptions.Source);
			string fullPath2 = Path.GetFullPath(updateOptions.Target);
			if (!Directory.Exists(fullPath))
			{
				throw new DirectoryNotFoundException("更新暂存目录不存在");
			}
			if (!Directory.Exists(fullPath2))
			{
				throw new DirectoryNotFoundException("ClipBridge 安装目录不存在");
			}
			string text = Path.Combine(appDataRoot, "update-backup", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text);
			List<string> list = new List<string>();
			try
			{
				string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
				foreach (string text2 in files)
				{
					string relativePath = Path.GetRelativePath(fullPath, text2);
					string fullPath3 = Path.GetFullPath(Path.Combine(fullPath2, relativePath));
					EnsureChildPath(fullPath2, fullPath3);
					string text3 = Path.Combine(text, relativePath);
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath3));
					if (File.Exists(fullPath3))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(text3));
						File.Copy(fullPath3, text3, overwrite: true);
					}
					list.Add(fullPath3);
					File.Copy(text2, fullPath3, overwrite: true);
				}
				Log(path, "更新文件替换完成，重新启动 ClipBridge。");
				string item = Path.Combine(fullPath2, "ClipBridge.dll");
				if (Process.Start(new ProcessStartInfo(updateOptions.DotnetPath)
				{
					UseShellExecute = false,
					WorkingDirectory = fullPath2,
					CreateNoWindow = true,
					ArgumentList = { item }
				}) == null)
				{
					throw new UpdateProtocolException("无法重新启动 ClipBridge");
				}
			}
			catch
			{
				RestoreBackup(fullPath2, text, list);
				throw;
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch (IOException)
			{
			}
			TryCleanStagedSource(fullPath, appDataRoot);
			return 0;
		}
		catch (Exception ex2)
		{
			try
			{
				Log(path, "更新失败：" + ex2.GetType().Name + ": " + ex2.Message);
			}
			catch (IOException)
			{
			}
			return 1;
		}
	}

	private static void ExtractSafely(string archivePath, string destination)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(archivePath);
		if (zipArchive.Entries.Count > 100)
		{
			throw new UpdateProtocolException("更新包文件数量异常");
		}
		long num = 0L;
		string value = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			num += entry.Length;
			if (num > 52428800)
			{
				throw new UpdateProtocolException("更新包解压后过大");
			}
			string text = entry.FullName.Replace('\\', '/');
			if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith('/'))
			{
				string fullPath = Path.GetFullPath(Path.Combine(destination, text));
				if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
				{
					throw new UpdateProtocolException("更新包包含非法路径");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
				entry.ExtractToFile(fullPath, overwrite: true);
			}
		}
	}

	private static UpdateOptions ParseArguments(string[] args)
	{
		string s = null;
		string text = null;
		string text2 = null;
		string text3 = null;
		for (int i = 1; i < args.Length - 1; i += 2)
		{
			switch (args[i])
			{
			case "--pid":
				s = args[i + 1];
				break;
			case "--source":
				text = args[i + 1];
				break;
			case "--target":
				text2 = args[i + 1];
				break;
			case "--dotnet":
				text3 = args[i + 1];
				break;
			}
		}
		if (!int.TryParse(s, out var result) || text == null || text2 == null || text3 == null)
		{
			throw new ArgumentException("更新辅助进程参数无效");
		}
		return new UpdateOptions(result, text, text2, text3);
	}

	private static void WaitForProcessExit(int processId, TimeSpan timeout)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			if (!process.WaitForExit((int)timeout.TotalMilliseconds))
			{
				throw new TimeoutException("等待 ClipBridge 退出超时");
			}
		}
		catch (ArgumentException)
		{
		}
	}

	private static void RestoreBackup(string target, string backup, IEnumerable<string> copiedTargets)
	{
		foreach (string copiedTarget in copiedTargets)
		{
			string relativePath = Path.GetRelativePath(target, copiedTarget);
			string text = Path.Combine(backup, relativePath);
			try
			{
				if (File.Exists(text))
				{
					File.Copy(text, copiedTarget, overwrite: true);
				}
				else if (File.Exists(copiedTarget))
				{
					File.Delete(copiedTarget);
				}
			}
			catch (IOException)
			{
			}
		}
	}

	private static void EnsureChildPath(string root, string candidate)
	{
		string value = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new UpdateProtocolException("更新路径越界");
		}
	}

	private static void TryCleanStagedSource(string source, string appDataRoot)
	{
		string value = Path.GetFullPath(Path.Combine(appDataRoot, "updates")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string directoryName = Path.GetDirectoryName(source);
		if (directoryName == null || !(Path.GetFullPath(directoryName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			Directory.Delete(directoryName, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	private static void Log(string path, string message)
	{
		File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
	}
}
