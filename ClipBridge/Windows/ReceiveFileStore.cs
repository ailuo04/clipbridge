using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace ClipBridge.Windows;

internal sealed class ReceiveFileStore : IDisposable
{
    private readonly string _directory;
    private readonly string _temporary;
    private readonly TransferFile[] _files;
    private readonly long[] _written;
    private int _current;
    private bool _committed;
    public ReceiveFileStore(string directory, TransferFile[] files)
    {
        TransferProtocol.ValidateOffer(new FileOffer("fileOffer", Guid.NewGuid().ToString("N"), "local", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), files));
        _directory = Path.GetFullPath(directory);
        _files = files.ToArray(); _written = new long[files.Length];
        Directory.CreateDirectory(_directory);
        _temporary = Path.Combine(_directory, ".clipbridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporary);
        try { for (int i = 0; i < files.Length; i++) using (File.Create(Path.Combine(_temporary, i.ToString()))) { } }
        catch { Dispose(); throw; }
    }
    public void Append(int index, long offset, byte[] data)
    {
        while (_current < _files.Length && _written[_current] == _files[_current].Size) _current++;
        if (index != _current || index < 0 || index >= _files.Length || offset != _written[index] || data.Length < 1 || data.Length > TransferProtocol.ChunkSize || data.Length > _files[index].Size - offset) throw new InvalidDataException("文件分块顺序或大小错误");
        using var output = new FileStream(Path.Combine(_temporary, index.ToString()), FileMode.Open, FileAccess.Write, FileShare.None);
        output.Position = offset; output.Write(data); _written[index] += data.Length;
    }
    public string[] Commit(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_committed) throw new InvalidOperationException("Already committed");
        for (int i = 0; i < _files.Length; i++)
        {
            if (_written[i] != _files[i].Size) throw new InvalidDataException("文件大小不匹配");
            using var stream = File.OpenRead(Path.Combine(_temporary, i.ToString()));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[TransferProtocol.ChunkSize];
            int count;
            while ((count = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(_files[i].Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件完整性校验失败");
        }
        string[] saved = new string[_files.Length];
        for (int i = 0; i < _files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            string name = _files[i].Name;
            for (int suffix = 0; ; suffix++)
            {
                string candidate = suffix == 0 ? name : Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name);
                string destination = Path.Combine(_directory, candidate);
                try { File.Move(Path.Combine(_temporary, i.ToString()), destination, false); saved[i] = destination; break; }
                catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
            }
        }
        _committed = true; Directory.Delete(_temporary); return saved;
    }
    public void Dispose()
    {
        // Only this receiver's unpredictable temporary directory is removable.
        if (!Path.GetFullPath(_temporary).StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary directory");
        if (Directory.Exists(_temporary)) Directory.Delete(_temporary, true);
    }
}
