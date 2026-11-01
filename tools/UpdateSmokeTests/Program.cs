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
        if (args.Length > 0 && args[0] == "--apply-update") return SelfUpdate.Apply(args, root!);
        if (args.Length > 0 && args[0] == "--begin")
        {
            AvailableUpdate update = await UpdateProtocol.CheckAsync(CancellationToken.None)
                ?? throw new Exception("Signed newer update not detected");
            string staged = await SelfUpdate.StagePackageAsync(update, CancellationToken.None, root!);
            SelfUpdate.LaunchUpdater(staged, root!);
            return 0;
        }
        if (args.Length > 0 && args[0] == "--run") return await RunAsync(args[1], args[2], args[3]);
        File.WriteAllText(Environment.GetEnvironmentVariable("CLIPBRIDGE_SMOKE_MARKER")!, Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
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
