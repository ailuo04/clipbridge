using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows;

internal static class UpdateExperienceSmoke
{
    private sealed class Capture<T> : IProgress<T>
    {
        public readonly List<object> Values = new();
        public Action<object>? OnReport = null;
        public void Report(T value) { Values.Add(value!); OnReport?.Invoke(value!); }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL " + description);
        Console.WriteLine("PASS " + description);
    }
    private static object Get(object value, string property) => value.GetType().GetProperty(property)!.GetValue(value)!;
    private static string Stage(object value) => Get(value, "Stage").ToString()!;

    private static void ExpectProtocolError(Action operation, string description)
    {
        try { operation(); }
        catch (UpdateProtocolException) { Check(true, description); return; }
        Check(false, description);
    }

    public static void VerifyManifest()
    {
        string envelope = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "manifest-0.5.0.json"));
        AvailableUpdate update = UpdateProtocol.ParseAndVerifyRollback(envelope, "0.5.0", new Version(0, 5, 6));
        Check(update.Version == "0.5.0" && update.IsRollback, "signed historical manifest returns an explicit rollback target");
        Check(update.DownloadUrl == "https://raw.githubusercontent.com/ailuo04/clipbridge/master/ClipBridge-0.5.0-win-x64.zip", "historical target uses the exact signed GitHub ZIP URL");
        Check(update.Size == 531856 && update.Sha256 == "300794E91407C99BBD512EF2A33715BCA75825900A5B9D9FA990BF6C0591DC14".Replace(" ", ""), "historical target preserves signed size and SHA-256");
        Check(UpdateProtocol.ParseAndVerify(envelope) == null, "automatic update parsing still rejects an older signed manifest");
        ExpectProtocolError(() => UpdateProtocol.ParseAndVerifyRollback(envelope, "0.5.1", new Version(0, 5, 6)), "release tag mismatch is rejected");
        ExpectProtocolError(() => UpdateProtocol.ParseAndVerifyRollback(envelope, "0.5.0", new Version(0, 5, 0)), "current version is not accepted as rollback target");
        ExpectProtocolError(() => UpdateProtocol.ParseAndVerifyRollback(envelope, "0.5.0", new Version(0, 4, 9)), "newer target is not accepted as rollback");
        using JsonDocument json = JsonDocument.Parse(envelope);
        string signature = json.RootElement.GetProperty("signature").GetString()!;
        string tamperedSignature = (signature[0] == 'A' ? "B" : "A") + signature[1..];
        ExpectProtocolError(() => UpdateProtocol.ParseAndVerifyRollback(envelope.Replace(signature, tamperedSignature, StringComparison.Ordinal), "0.5.0", new Version(0, 5, 6)), "tampered signature is rejected");
        Check(UpdateProtocol.IsTrustedHistoricalPackageUrl("https://raw.githubusercontent.com/ailuo04/clipbridge/master/ClipBridge-0.5.0-win-x64.zip", "0.5.0"), "only the exact official historical ZIP path is trusted");
        Check(!UpdateProtocol.IsTrustedHistoricalPackageUrl("https://example.com/ClipBridge-0.5.0-win-x64.zip", "0.5.0"), "foreign package host is rejected");
        Check(!UpdateProtocol.IsTrustedHistoricalPackageUrl("https://raw.githubusercontent.com/attacker/clipbridge/master/ClipBridge-0.5.0-win-x64.zip", "0.5.0"), "foreign repository is rejected");
        Check(!UpdateProtocol.IsTrustedHistoricalPackageUrl("https://raw.githubusercontent.com/ailuo04/clipbridge/master/ClipBridge-0.5.1-win-x64.zip", "0.5.0"), "different package version is rejected");
    }
    public static async Task RunAsync()
    {
        MethodInfo? download = typeof(UpdateProtocol).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == "DownloadAsync" && m.GetParameters().Length == 4);
        Check(download != null, "download accepts structured progress reports");
        Type progressType = typeof(UpdateProtocol).Assembly.GetType("ClipBridge.Windows.UpdateProgress")!;
        object Observer(Action<object>? action, out List<object> values)
        {
            Type captureType = typeof(Capture<>).MakeGenericType(progressType);
            object capture = Activator.CreateInstance(captureType, true)!;
            values = (List<object>)captureType.GetField("Values")!.GetValue(capture)!;
            captureType.GetField("OnReport")!.SetValue(capture, action);
            return capture;
        }
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-update-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] data = new byte[512 * 1024]; RandomNumberGenerator.Fill(data);
            foreach (string scenario in new[] { "success", "cancel", "hash", "length" })
            {
                using var listener = new HttpListener();
                var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); socket.Start();
                int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
                string origin = $"http://127.0.0.1:{port}/";
                listener.Prefixes.Add(origin); listener.Start();
                Task server = Task.Run(async () =>
                {
                    var request = await listener.GetContextAsync();
                    try
                    {
                        request.Response.ContentLength64 = data.Length;
                        for (int offset = 0; offset < data.Length; offset += 8192)
                        {
                            await request.Response.OutputStream.WriteAsync(data.AsMemory(offset, Math.Min(8192, data.Length - offset)));
                            await Task.Delay(3);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is HttpListenerException || ex is ObjectDisposedException) { }
                    finally { request.Response.Close(); }
                });
                using var cancellation = new CancellationTokenSource();
                object observer = Observer(value => { if (scenario == "cancel" && (long)Get(value, "BytesReceived") > 0) cancellation.Cancel(); }, out var reports);
                string target = Path.Combine(root, scenario + ".zip");
                var update = new AvailableUpdate("9.0.0", origin, data.Length + (scenario == "length" ? 1 : 0), scenario == "hash" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(data)), "Synthetic");
                Exception? error = null;
                try { await (Task)download!.Invoke(null, new[] { update, target, cancellation.Token, observer })!; }
                catch (Exception ex) { error = ex; }
                await server;
                if (scenario == "success")
                {
                    Check(error == null && File.ReadAllBytes(target).AsSpan().SequenceEqual(data), "downloaded bytes remain hash verified");
                    var downloading = reports.Where(v => Stage(v) == "Downloading").ToArray();
                    Check(downloading.Length >= 3 && (long)Get(downloading[0], "BytesReceived") == 0 && (long)Get(downloading[^1], "BytesReceived") == data.Length, "download reports initial, intermediate, and final byte counts");
                    Check(downloading.Zip(downloading.Skip(1)).All(pair => (long)Get(pair.First, "BytesReceived") <= (long)Get(pair.Second, "BytesReceived")), "download bytes never go backwards");
                    Check(reports.Any(v => Stage(v) == "Verifying") && reports.All(v => (double)Get(v, "OverallPercent") < 100), "verification is separate and download never claims installation complete");
                }
                else
                {
                    Check(scenario == "cancel" ? error is OperationCanceledException : error is UpdateProtocolException, scenario + " rejects incomplete or invalid package");
                    Check(!File.Exists(target) && !File.Exists(target + ".part"), scenario + " removes partial download");
                }
            }
            await StageChecksAsync(root);
        }
        finally { Directory.Delete(root, true); }
    }
    public static int Apply(string[] args, string appData)
    {
        var capture = new Capture<UpdateProgress>();
        string? path = Environment.GetEnvironmentVariable("CLIPBRIDGE_SMOKE_PROGRESS");
        capture.OnReport = value => { if (path != null) File.AppendAllText(path, $"{Stage(value)} {((UpdateProgress)value).OverallPercent:0.##}\n"); };
        return SelfUpdate.Apply(args, appData, capture);
    }

    public static async Task VerifyRollbackAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "clipbridge-rollback-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(root, "install"), staged = Path.Combine(root, "stage"), data = Path.Combine(root, "data");
        Directory.CreateDirectory(install); Directory.CreateDirectory(staged); Directory.CreateDirectory(data);
        foreach (string name in new[] { "ClipBridge.exe", "ClipBridge.dll", "ClipBridge.deps.json", "ClipBridge.runtimeconfig.json" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(install, name));
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(staged, name));
        }
        byte[] original = File.ReadAllBytes(Path.Combine(install, "ClipBridge.dll"));
        File.WriteAllText(Path.Combine(staged, "ClipBridge.dll"), "deliberately invalid managed executable");
        File.WriteAllText(Path.Combine(staged, "new-file.txt"), "must be removed on rollback");
        string marker = Path.Combine(root, "restarted.txt"), progress = Path.Combine(root, "progress.txt");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(install, "ClipBridge.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = install };
        start.ArgumentList.Add("--spawn-helper"); start.ArgumentList.Add(staged); start.ArgumentList.Add("0.5.4");
        foreach (string secret in new[] { "GITHUB_TOKEN", "CLIPBRIDGE_UPDATE_PRIVATE_KEY", "CLIPBRIDGE_ANDROID_KEYSTORE_BASE64", "CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD", "CLIPBRIDGE_ANDROID_KEYSTORE_PATH" }) start.Environment.Remove(secret);
        start.Environment["CLIPBRIDGE_SMOKE_ROOT"] = data; start.Environment["CLIPBRIDGE_SMOKE_MARKER"] = marker; start.Environment["CLIPBRIDGE_SMOKE_PROGRESS"] = progress;
        using var client = System.Diagnostics.Process.Start(start)!; await client.WaitForExitAsync();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string log = Path.Combine(data, "update.log");
        while ((!File.Exists(marker + ".args") || !File.Exists(log) || !File.ReadAllText(log).Contains("更新失败")) && watch.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
        Check(File.Exists(marker + ".args") && File.ReadAllLines(marker + ".args").Contains("--update-rolled-back"), "startup failure reopens the restored client");
        Check(File.ReadAllBytes(Path.Combine(install, "ClipBridge.dll")).AsSpan().SequenceEqual(original), "startup failure restores the previous executable");
        Check(!File.Exists(Path.Combine(install, "new-file.txt")), "rollback removes files introduced by failed update");
        Check(Directory.GetDirectories(Path.Combine(data, "update-backup")).Length == 1, "failure preserves the backup for diagnosis");
        Check(!File.ReadAllText(progress).Contains("Completed"), "failed startup never reports update complete");
        Console.WriteLine("Isolated rollback directory: " + root);
    }

    private static async Task StageChecksAsync(string root)
    {
        foreach (string scenario in new[] { "success", "cancel", "traversal", "missing" })
        {
            byte[] package;
            using (var memory = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    string[] names = scenario == "traversal" ? new[] { "../escape.txt" } : scenario == "missing" ? new[] { "ClipBridge.dll" } : new[] { "ClipBridge.dll", "ClipBridge.runtimeconfig.json", "sub/notice.txt" };
                    foreach (string name in names) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("synthetic runtime data"); }
                }
                package = memory.ToArray();
            }
            using var listener = new HttpListener();
            var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            string origin = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(origin); listener.Start();
            Task server = Task.Run(async () => { var request = await listener.GetContextAsync(); request.Response.ContentLength64 = package.Length; await request.Response.OutputStream.WriteAsync(package); request.Response.Close(); });
            using var cancellation = new CancellationTokenSource();
            var capture = new Capture<UpdateProgress>();
            capture.OnReport = value => { if (scenario == "cancel" && Stage(value) == "Extracting") cancellation.Cancel(); };
            var update = new AvailableUpdate("9.0.0", origin, package.Length, Convert.ToHexString(SHA256.HashData(package)), "Synthetic");
            string appData = Path.Combine(root, "stage-" + scenario); string? staged = null; Exception? error = null;
            try { staged = await SelfUpdate.StagePackageAsync(update, cancellation.Token, appData, capture); }
            catch (Exception ex) { error = ex; }
            await server;
            if (scenario == "success")
            {
                Check(error == null && File.Exists(Path.Combine(staged!, "sub", "notice.txt")), "verified staging extracts nested files");
                Check(capture.Values.Any(v => Stage(v) == "Extracting") && capture.Values.All(v => ((UpdateProgress)v).OverallPercent <= 90), "staging exposes extraction without claiming installation complete");
                MethodInfo? discard = typeof(SelfUpdate).GetMethod("DiscardStagedPackage", BindingFlags.Static | BindingFlags.NonPublic, new[] { typeof(string), typeof(string) });
                Check(discard != null, "a staged update can be discarded if handoff is cancelled or fails");
                string external = Path.Combine(root, "external", "package"); Directory.CreateDirectory(external);
                File.WriteAllText(Path.Combine(external, "keep.txt"), "keep");
                discard!.Invoke(null, new[] { external, appData });
                Check(File.Exists(Path.Combine(external, "keep.txt")), "staging cleanup cannot delete outside the update cache");
                discard.Invoke(null, new[] { staged!, appData });
                Check(!Directory.Exists(staged), "handoff cancellation removes a fully staged package");
            }
            else
            {
                Check(scenario == "cancel" ? error is OperationCanceledException : error is UpdateProtocolException, "staging " + scenario + " is rejected");
                Check(!Directory.EnumerateDirectories(Path.Combine(appData, "updates")).Any(), "staging " + scenario + " removes partial extracted files");
            }
            Check(!File.Exists(Path.Combine(appData, "updates", "escape.txt")), "archive cannot escape the package directory");
        }
    }

}
