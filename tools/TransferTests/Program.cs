using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClipBridge.Windows;

internal static class Program
{
    public static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    public static void Rejects<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL " + name); }
    static async Task TextRoundtripAsync()
    {
        byte[] pairing = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using var node = new SyncNode(pairing, a => a(), 0, false);
        var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
        // A second node has a different origin; discovery stays off and no real clipboard is touched.
        using var sender = new SyncNode(pairing, a => a(), 0, false);
        node.RemoteTextReceived += received.Enqueue;
        Check(await sender.SendTextAsync(IPAddress.Loopback, "重复中文🙂", CancellationToken.None, node.Port), "targeted text written");
        Check(await sender.SendTextAsync(IPAddress.Loopback, "重复中文🙂", CancellationToken.None, node.Port), "identical explicit send is allowed");
        for (int i = 0; i < 100 && received.Count < 2; i++) await Task.Delay(10);
        Check(received.Count == 2, "both explicit sends received without auto forwarding");
    }

    static void FileChecks()
    {
        foreach (string name in new[] { "../x", "C:\\x", "a/b", "a\\b", "CON", "x.", ".", "a:b", "NUL.txt" })
            Rejects<InvalidDataException>(() => TransferProtocol.ValidateFileName(name), "unsafe name rejected: " + name);
        TransferProtocol.ValidateFileName("照片🙂.txt");
        string dir = Path.Combine(Path.GetTempPath(), "clipbridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] data = Encoding.UTF8.GetBytes("中文🙂 file");
            var file = new TransferFile("照片.txt", data.Length, Convert.ToHexString(SHA256.HashData(data)));
            File.WriteAllText(Path.Combine(dir, file.Name), "existing");
            using (var store = new ReceiveFileStore(dir, new[] { file }))
            {
                store.Append(0, 0, data);
                string[] saved = store.Commit();
                Check(saved.Length == 1 && File.ReadAllBytes(saved[0]).SequenceEqual(data), "verified file committed");
                Check(File.ReadAllText(Path.Combine(dir, file.Name)) == "existing", "existing file never overwritten");
            }
            var corrupt = file with { Sha256 = new string('0', 64) };
            using (var store = new ReceiveFileStore(dir, new[] { corrupt }))
            { store.Append(0, 0, data); Rejects<InvalidDataException>(() => store.Commit(), "bad hash rejected"); }
            Check(!Directory.EnumerateDirectories(dir).Any(), "partial transfer directory removed");
            using (var store = new ReceiveFileStore(dir, new[] { file }))
                Rejects<InvalidDataException>(() => store.Append(0, 1, data), "out-of-order chunk rejected");
        }
        finally { Directory.Delete(dir, true); }
    }

    static async Task InteropServerAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "from-jvm");
        string source = Path.Combine(directory, "from-dotnet"); Directory.CreateDirectory(source);
        string text = Path.Combine(source, "中文🙂.txt"), empty = Path.Combine(source, "empty.bin");
        await File.WriteAllTextAsync(text, "C# → Kotlin 中文🙂"); await File.WriteAllBytesAsync(empty, Array.Empty<byte>());
        byte[] pairing = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using var node = new FileTransferNode(pairing, output, (_, _, _) => Task.FromResult(true), 0);
        var control = new TcpListener(IPAddress.Loopback, 0); control.Start();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "server.json"), System.Text.Json.JsonSerializer.Serialize(new { receivePort = node.Port, controlPort = ((IPEndPoint)control.LocalEndpoint).Port }));
            Console.WriteLine("INTEROP READY " + directory);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var client = await control.AcceptTcpClientAsync(timeout.Token);
            byte[] portBytes = new byte[8]; await client.GetStream().ReadExactlyAsync(portBytes, timeout.Token);
            if (!portBytes.AsSpan(0,4).SequenceEqual(Encoding.ASCII.GetBytes("CBIT"))) throw new IOException("Unexpected interoperability control client");
            int port = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(portBytes.AsSpan(4,4));
            await node.SendAsync(IPAddress.Loopback, new[] { text, empty }, timeout.Token, port);
            Check(File.ReadAllText(Path.Combine(output, "来自手机.txt")) == "Kotlin → C# 中文🙂", "Kotlin sender to production C# receiver");
            Console.WriteLine("BIDIRECTIONAL JVM DOTNET INTEROP PASSED");
        }
        finally { control.Stop(); }
    }

    static async Task FileRoundtripAsync()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        string dir = Path.Combine(Path.GetTempPath(), "clipbridge-net-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "source"); Directory.CreateDirectory(source);
            string output = Path.Combine(dir, "received");
            string small = Path.Combine(source, "中文.txt"), empty = Path.Combine(source, "empty.bin");
            await File.WriteAllTextAsync(small, "Hello 中文🙂"); await File.WriteAllBytesAsync(empty, Array.Empty<byte>());
            using var node = new FileTransferNode(key, output, (_, _, _) => Task.FromResult(true), 0);
            await node.SendAsync(IPAddress.Loopback, new[] { small, empty }, CancellationToken.None, node.Port);
            Check(File.ReadAllText(Path.Combine(output, "中文.txt")) == "Hello 中文🙂" && new FileInfo(Path.Combine(output, "empty.bin")).Length == 0, "encrypted multi-file and empty-file roundtrip");
            using var reject = new FileTransferNode(key, Path.Combine(dir, "rejected"), (_, _, _) => Task.FromResult(false), 0);
            bool denied = false;
            try { await node.SendAsync(IPAddress.Loopback, new[] { small }, CancellationToken.None, reject.Port); }
            catch (IOException) { denied = true; }
            Check(denied && !Directory.Exists(Path.Combine(dir, "rejected")), "receiver rejection writes nothing");
        }
        finally { Directory.Delete(dir, true); }
    }

    static async Task<int> Main(string[] args)
    {
        try { await RunAsync(args); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }

    static async Task RunAsync(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-qr")
        {
            string invitation = PairingInvitationCodec.Format("0123456789abcdef0123456789abcdef", IPAddress.Parse("10.51.2.197"));
            await File.WriteAllBytesAsync(args[1], PairingQr.RenderPng(invitation));
            return;
        }
        if (args.Length == 1 && args[0] == "--malformed") { await ExperienceTests.MalformedDiscoveryAsync(); return; }
        if (args.Length == 1 && args[0] == "--wake") { await DiscoveryWakeTests.RunAsync(); return; }
        if (args.Length == 1 && args[0] == "--invitation") { PairingInvitationTests.Run(); return; }
        if (args.Length == 1 && args[0] == "--pair-security") { await ExperienceTests.SessionCollisionAsync(); return; }
        if (args.Length == 2 && args[0] == "--experience-interop") { await ExperienceInterop.RunAsync(args[1]); return; }
        if (args.Length == 2 && args[0] == "--interop") { await InteropServerAsync(args[1]); return; }
        PairingInvitationTests.Run();
        await DiscoveryWakeTests.RunAsync();
        ExperienceTests.Run();
        await ExperienceTests.PairingRoundtripAsync();
        await ExperienceTests.PairingIdentityMismatchAsync();
        await ExperienceTests.MalformedDiscoveryAsync();
        await ExperienceTests.SessionCollisionAsync();
        byte[] pairing = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] key = SharedProtocol.DeriveKey(pairing);
        Check(Convert.ToHexString(key).ToLowerInvariant() == "d9202a642d557b019c9620bd24781958a89e4a95543e9352576a9839fb72871b", "cross-platform key vector");
        byte[] frame = Convert.FromHexString("000102030405060708090a0ba2863c8171fd10de14a29c87777e13be29dfaa28530c92ee0aef");
        Check(Encoding.UTF8.GetString(SharedProtocol.Decrypt(key, frame)) == "中文🙂", "cross-platform AES-GCM vector");
        frame[^1] ^= 1;
        Rejects<CryptographicException>(() => SharedProtocol.Decrypt(key, frame), "tampered text rejected");
        Check(!SharedProtocol.IsFresh(DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds()), "stale message rejected");
        Check(SharedProtocol.IsLocalAddress(IPAddress.Parse("192.168.1.4")) && !SharedProtocol.IsLocalAddress(IPAddress.Parse("8.8.8.8")), "local IPv4 only");
        Check(typeof(SyncNode).GetMethod("SendTextAsync") != null, "explicit targeted text send API exists");
        Check(typeof(SyncNode).GetMethod("OnLocalClipboardTextChanged") == null, "automatic clipboard change API removed");
        await TextRoundtripAsync();
        FileChecks();
        await FileRoundtripAsync();
        Console.WriteLine("ALL TRANSFER TESTS PASSED");
        await Task.CompletedTask;
    }
}
