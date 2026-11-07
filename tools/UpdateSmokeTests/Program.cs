using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? root = Environment.GetEnvironmentVariable("CLIPBRIDGE_SMOKE_ROOT");
        if (args.Length > 0 && args[0] == "--apply-update") return UpdateExperienceSmoke.Apply(args, root!);
        if (args.Length > 0 && args[0] == "--verify-helper") return await VerifyHelperAsync();
        if (args.Length > 0 && args[0] == "--verify-rollback") { await UpdateExperienceSmoke.VerifyRollbackAsync(); return 0; }
        if (args.Length > 0 && args[0] == "--verify-experience") { await UpdateExperienceSmoke.RunAsync(); return 0; }
        if (args.Length == 2 && args[0] == "--spawn-helper") { SelfUpdate.LaunchUpdater(args[1], root!); return 0; }
        if (args.Length > 0 && args[0] == "--begin")
        {
            AvailableUpdate update = await UpdateProtocol.CheckAsync(CancellationToken.None)
                ?? throw new Exception("Signed newer update not detected");
            string staged = await SelfUpdate.StagePackageAsync(update, CancellationToken.None, root!);
            SelfUpdate.LaunchUpdater(staged, root!);
            return 0;
        }
        if (args.Length > 0 && args[0] == "--run") return await RunAsync(args[1], args[2], args[3]);
        string restartMarker = Environment.GetEnvironmentVariable("CLIPBRIDGE_SMOKE_MARKER")!;
        File.WriteAllText(restartMarker, Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
        File.WriteAllLines(restartMarker + ".args", args);
        int readyIndex = Array.IndexOf(args, "--update-ready");
        if (readyIndex >= 0 && readyIndex + 1 < args.Length)
        {
            string backupRoot = Path.Combine(root!, "update-backup");
            File.WriteAllText(restartMarker + ".backup", (Directory.Exists(backupRoot) && Directory.GetDirectories(backupRoot).Length > 0).ToString());
            await Task.Delay(200);
            using var ready = EventWaitHandle.OpenExisting(args[readyIndex + 1]);
            ready.Set();
        }
        return 0;
    }

    private static async Task<int> VerifyHelperAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-helper-smoke-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(root, "install"), stage = Path.Combine(root, "stage"), data = Path.Combine(root, "data");
        Directory.CreateDirectory(install); Directory.CreateDirectory(stage); Directory.CreateDirectory(data);
        foreach (string name in new[] { "ClipBridge.exe", "ClipBridge.dll", "ClipBridge.deps.json", "ClipBridge.runtimeconfig.json" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(install, name));
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(stage, name));
        }
        byte[] dependency = System.Text.Encoding.UTF8.GetBytes("synthetic QR dependency; not a real assembly");
        File.WriteAllBytes(Path.Combine(install, "QRCoder.dll"), dependency);
        byte[] stagedDependency = System.Text.Encoding.UTF8.GetBytes("synthetic newer QR dependency");
        File.WriteAllBytes(Path.Combine(stage, "QRCoder.dll"), stagedDependency);
        File.WriteAllText(Path.Combine(stage, "QRCoder-LICENSE.txt"), "synthetic notice");
        string marker = Path.Combine(root, "restarted.txt");
        string progressFile = Path.Combine(root, "progress.txt");
        var start = new ProcessStartInfo(Path.Combine(install, "ClipBridge.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = install };
        start.ArgumentList.Add("--spawn-helper"); start.ArgumentList.Add(stage);
        foreach (string secret in new[] { "GITHUB_TOKEN", "CLIPBRIDGE_UPDATE_PRIVATE_KEY", "CLIPBRIDGE_ANDROID_KEYSTORE_BASE64", "CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD", "CLIPBRIDGE_ANDROID_KEYSTORE_PATH" }) start.Environment.Remove(secret);        start.Environment["CLIPBRIDGE_SMOKE_ROOT"] = data; start.Environment["CLIPBRIDGE_SMOKE_MARKER"] = marker; start.Environment["CLIPBRIDGE_SMOKE_PROGRESS"] = progressFile;
        using var child = Process.Start(start) ?? throw new Exception("Could not start synthetic update client");
        await child.WaitForExitAsync();
        if (child.ExitCode != 0) throw new Exception("Synthetic update client failed");
        string updaterRoot = Path.Combine(data, "updater");
        var watch = Stopwatch.StartNew();
        while ((!Directory.Exists(updaterRoot) || Directory.GetDirectories(updaterRoot).Length == 0 || !File.Exists(marker)) && watch.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
        if (!Directory.Exists(updaterRoot)) throw new Exception("Standalone helper directory was not created");
        string[] helpers = Directory.GetDirectories(updaterRoot);
        if (helpers.Length != 1) throw new Exception("Unexpected standalone helper directory count");
        string helper = helpers[0];
        if (!File.ReadAllBytes(Path.Combine(helper, "QRCoder.dll")).AsSpan().SequenceEqual(dependency)) throw new Exception("Standalone helper did not copy the QR runtime dependency");
        if (!File.Exists(Path.Combine(helper, "ClipBridge.deps.json"))) throw new Exception("Standalone helper did not copy the dependency manifest");
        if (!File.Exists(marker)) throw new Exception("Standalone helper did not restart the synthetic client");
        string[] restartArgs = File.ReadAllLines(marker + ".args");
        if (!Array.Exists(restartArgs, arg => arg == "--updated")) throw new Exception("FAIL updater does not request that the new control panel opens automatically");
        if (!Array.Exists(restartArgs, arg => arg == "--update-ready")) throw new Exception("FAIL updater does not wait for startup confirmation");
        while ((!File.Exists(progressFile) || !File.ReadAllText(progressFile).Contains("Completed 100")) && watch.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(50);
        if (!File.Exists(progressFile) || !File.ReadAllText(progressFile).Contains("Completed 100")) throw new Exception("Helper did not complete after startup confirmation");
        string[] stages = File.ReadAllLines(progressFile);
        foreach (string required in new[] { "Waiting", "Installing", "Restarting", "Completed" })
            if (!Array.Exists(stages, line => line.StartsWith(required + " "))) throw new Exception("Missing installer stage: " + required);
        if (File.ReadAllText(marker + ".backup") != "True") throw new Exception("Backup was removed before the new application confirmed startup");
        if (Directory.GetDirectories(Path.Combine(data, "update-backup")).Length != 0) throw new Exception("Successful startup did not clean the backup");
        if (!File.ReadAllBytes(Path.Combine(install, "QRCoder.dll")).AsSpan().SequenceEqual(stagedDependency)) throw new Exception("Update did not apply the staged QR dependency");
        if (!File.Exists(Path.Combine(install, "QRCoder-LICENSE.txt"))) throw new Exception("Update did not apply the staged QR license notice");
        Console.WriteLine("PASS standalone updater copies helper dependencies, applies new dependency files, and restarts a synthetic client");
        return 0;
    }
    private static async Task<int> RunAsync(string oldBuild, string newBuild, string privateKeyPath)
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-update-smoke-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "install");
        string appData = Path.Combine(root, "data");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(appData);
        foreach (string file in Directory.GetFiles(oldBuild)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        string archive = Path.Combine(root, "update.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "ClipBridge.exe", "ClipBridge.dll", "ClipBridge.deps.json", "ClipBridge.runtimeconfig.json" })
                zip.CreateEntryFromFile(Path.Combine(newBuild, name), name, CompressionLevel.Optimal);
        }
        byte[] package = await File.ReadAllBytesAsync(archive);
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        string origin = $"http://127.0.0.1:{port}/";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1,
            windows = new { version = "0.3.7", url = origin + "update.zip", size = package.Length, sha256 = Convert.ToHexString(SHA256.HashData(package)), notes = "Isolated synthetic test" }
        });
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(await File.ReadAllTextAsync(privateKeyPath));
        byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        });
        using HttpListener listener = new HttpListener();
        listener.Prefixes.Add(origin);
        listener.Start();
        Task serving = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    HttpListenerContext request = await listener.GetContextAsync();
                    byte[] data = request.Request.Url!.AbsolutePath == "/manifest" ? envelope : package;
                    request.Response.ContentLength64 = data.Length;
                    await request.Response.OutputStream.WriteAsync(data);
                    request.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
        string marker = Path.Combine(root, "restarted-version.txt");
        ProcessStartInfo start = new ProcessStartInfo(Path.Combine(target, "ClipBridge.exe"), "--begin") { UseShellExecute = false, WorkingDirectory = target, CreateNoWindow = true };
        start.Environment["CLIPBRIDGE_SMOKE_ROOT"] = appData;
        start.Environment["CLIPBRIDGE_SMOKE_MARKER"] = marker;
        start.Environment["CLIPBRIDGE_UPDATE_URL"] = origin + "manifest";
        using Process child = Process.Start(start) ?? throw new Exception("Could not start test client");
        await child.WaitForExitAsync();
        if (child.ExitCode != 0) throw new Exception("Test client failed with exit code " + child.ExitCode);
        Stopwatch watch = Stopwatch.StartNew();
        while (!File.Exists(marker) && watch.Elapsed < TimeSpan.FromSeconds(40)) await Task.Delay(200);
        if (!File.Exists(marker))
        {
            string log = Path.Combine(appData, "update.log");
            throw new Exception("Updated test client did not restart. " + (File.Exists(log) ? File.ReadAllText(log) : "No updater log"));
        }
        string version = await File.ReadAllTextAsync(marker);
        if (version != "0.3.7") throw new Exception("Restarted unexpected version: " + version);
        string actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(target, "ClipBridge.dll"))));
        string expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(newBuild, "ClipBridge.dll"))));
        if (actual != expected) throw new Exception("Updated assembly hash does not match");
        listener.Stop();
        await serving;
        Console.WriteLine("PASS signed manifest, local download, hash verification, staged install, restart and updated assembly");
        Console.WriteLine("Isolated test directory: " + root);
        return 0;
    }
}
