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

    internal const int ExistingInstanceExitCode = 10;
    internal const int StartupUnconfirmedResult = 2;

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

	internal static Task<string> StagePackageAsync(AvailableUpdate update, CancellationToken cancellationToken, IProgress<UpdateProgress>? progress = null)
	{
		return StagePackageAsync(update, cancellationToken, AppDataRoot, progress);
	}

	internal static async Task<string> StagePackageAsync(AvailableUpdate update, CancellationToken cancellationToken, string appDataRoot, IProgress<UpdateProgress>? progress = null)
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
			await UpdateProtocol.DownloadAsync(update, archivePath, cancellationToken, progress).ConfigureAwait(false);
			Directory.CreateDirectory(stagedDirectory);
			await Task.Run(() => ExtractSafely(archivePath, stagedDirectory, cancellationToken, progress), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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

	internal static void DiscardStagedPackage(string stagedDirectory) => TryCleanStagedSource(stagedDirectory, AppDataRoot);

    internal static void DiscardStagedPackage(string stagedDirectory, string appDataRoot) => TryCleanStagedSource(stagedDirectory, appDataRoot);

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

    internal static int Apply(string[] args) => Apply(args, AppDataRoot);

    internal static int Apply(string[] args, string appDataRoot, IProgress<UpdateProgress>? progress = null)
    {
        string logPath = Path.Combine(appDataRoot, "update.log");
        Directory.CreateDirectory(appDataRoot);
        try
        {
            UpdateOptions options = ParseArguments(args);
            progress?.Report(new UpdateProgress(UpdateStage.Waiting));
            Log(logPath, "开始更新，等待主程序退出。PID=" + options.ProcessId);
            WaitForProcessExit(options.ProcessId, TimeSpan.FromSeconds(30));
            string source = Path.GetFullPath(options.Source), target = Path.GetFullPath(options.Target);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("更新暂存目录不存在");
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("ClipBridge 安装目录不存在");
            string backup = Path.Combine(appDataRoot, "update-backup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            var copiedTargets = new List<string>();
            try
            {
                string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
                if (files.Length == 0) throw new UpdateProtocolException("更新暂存目录为空");
                progress?.Report(new UpdateProgress(UpdateStage.Installing, 0, files.Length));
                foreach (string file in files)
                {
                    string relative = Path.GetRelativePath(source, file);
                    string destination = Path.GetFullPath(Path.Combine(target, relative));
                    EnsureChildPath(target, destination);
                    string backupFile = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (File.Exists(destination))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                        File.Copy(destination, backupFile, overwrite: true);
                    }
                    copiedTargets.Add(destination);
                    File.Copy(file, destination, overwrite: true);
                    progress?.Report(new UpdateProgress(UpdateStage.Installing, copiedTargets.Count, files.Length));
                }
                progress?.Report(new UpdateProgress(UpdateStage.Restarting));
                Log(logPath, "更新文件替换完成，重新启动并等待主界面就绪。");
                if (!RestartAndConfirm(options))
                {
                    Log(logPath, "已有 ClipBridge 实例接管启动；未收到新版就绪确认，已保留新文件、暂存文件和备份，不执行回滚。");
                    return StartupUnconfirmedResult;
                }
            }
            catch
            {
                RestoreBackup(target, backup, copiedTargets);
                Log(logPath, "已尝试恢复备份，重新打开原程序。");
                try { using Process? restored = Process.Start(CreateRestartInfo(options, "--update-rolled-back")); }
                catch (Exception ex) { Log(logPath, "原程序重新打开失败：" + ex.Message); }
                throw;
            }
            // The new process has confirmed startup. Cleanup failures must not roll it back.
            try { Directory.Delete(backup, recursive: true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log(logPath, "备份清理延后：" + ex.Message); }
            TryCleanStagedSource(source, appDataRoot);
            Log(logPath, "新版主界面已打开，更新完成。");
            progress?.Report(new UpdateProgress(UpdateStage.Completed, 1, 1));
            return 0;
        }
        catch (Exception ex)
        {
            try { Log(logPath, "更新失败：" + ex.GetType().Name + ": " + ex.Message); }
            catch (IOException) { }
            return 1;
        }
    }

    private static ProcessStartInfo CreateRestartInfo(UpdateOptions options, string argument)
    {
        var start = new ProcessStartInfo(options.DotnetPath)
        {
            UseShellExecute = false, WorkingDirectory = options.Target, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(options.Target, DllName));
        start.ArgumentList.Add(argument);
        return start;
    }

    private static bool RestartAndConfirm(UpdateOptions options)
    {
        string eventName = "Local\\ClipBridge.UpdateReady." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var start = CreateRestartInfo(options, "--updated");
        start.ArgumentList.Add("--update-ready"); start.ArgumentList.Add(eventName);
        using Process process = Process.Start(start) ?? throw new UpdateProtocolException("无法重新启动 ClipBridge");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (ready.WaitOne(100)) return true;
            if (process.HasExited)
            {
                // Another primary may hold the new files. Do not roll them back without startup confirmation.
                if (process.ExitCode == ExistingInstanceExitCode) return false;
                throw new UpdateProtocolException("新版启动后提前退出，已保留备份");
            }
        }
        // Stop only the process we just launched, so rollback can replace its files.
        if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
        throw new UpdateProtocolException("等待新版主界面打开超时，已保留备份");
    }

    private static void ExtractSafely(string archivePath, string destination, CancellationToken cancellationToken, IProgress<UpdateProgress>? progress)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 100) throw new UpdateProtocolException("更新包文件数量异常");
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            total += entry.Length;
            if (total > 52428800) throw new UpdateProtocolException("更新包解压后过大");
        }
        long extracted = 0;
        progress?.Report(new UpdateProgress(UpdateStage.Extracting, 0, total));
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        byte[] buffer = new byte[81920];
        var elapsed = Stopwatch.StartNew(); long lastReport = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith('/')) continue;
            string file = Path.GetFullPath(Path.Combine(destination, relative));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UpdateProtocolException("更新包包含非法路径");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using Stream input = entry.Open();
            using var output = new FileStream(file, FileMode.Create, FileAccess.Write);
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count); extracted += count;
                if (elapsed.ElapsedMilliseconds - lastReport >= 100 || extracted == total)
                {
                    progress?.Report(new UpdateProgress(UpdateStage.Extracting, extracted, total));
                    lastReport = elapsed.ElapsedMilliseconds;
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

	private static UpdateOptions ParseArguments(string[] args)
	{
		string? s = null;
		string? text = null;
		string? text2 = null;
		string? text3 = null;
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
		string? directoryName = Path.GetDirectoryName(source);
		if (directoryName == null || !(Path.GetFullPath(directoryName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			Directory.Delete(directoryName, recursive: true);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}

	private static void Log(string path, string message)
	{
		File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
	}
}
