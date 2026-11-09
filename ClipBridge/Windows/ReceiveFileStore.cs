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
    private FileStream? _output;
    private int _outputIndex = -1;
    public ReceiveFileStore(string directory, TransferFile[] files)
    {
        TransferProtocol.ValidateOffer(new FileOffer(files.Any(x => x.RelativePath != null) ? "fileTreeOffer" : "fileOffer", Guid.NewGuid().ToString("N"), "local", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), files));
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
        if (_outputIndex != index)
        {
            _output?.Dispose();
            _output = new FileStream(Path.Combine(_temporary, index.ToString()), FileMode.Open, FileAccess.Write, FileShare.None, 256 * 1024, FileOptions.SequentialScan);
            _outputIndex = index;
        }
        _output!.Write(data); _written[index] += data.Length;
    }
    public string[] Commit(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        _output?.Dispose(); _output = null; _outputIndex = -1;
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
        if (_files.Any(x => x.RelativePath != null)) return CommitTree(token);
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
    private string[] CommitTree(CancellationToken token)
    {
        string payload = Path.Combine(_temporary, "payload"); Directory.CreateDirectory(payload);
        foreach (var file in _files.Where(x => x.IsDirectory).OrderBy(x => x.RelativePath!.Count(c => c == '/')))
            Directory.CreateDirectory(Path.Combine(payload, file.RelativePath!.Replace('/', Path.DirectorySeparatorChar)));
        for (int i = 0; i < _files.Length; i++) if (!_files[i].IsDirectory)
            File.Move(Path.Combine(_temporary, i.ToString()), Path.Combine(payload, _files[i].RelativePath!.Replace('/', Path.DirectorySeparatorChar)));
        var destinations = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in _files.Select(x => x.RelativePath!.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); string source = Path.Combine(payload, root); bool directory = Directory.Exists(source);
            for (int suffix = 0; ; suffix++)
            {
                string name = suffix == 0 ? root : directory ? root + " (" + suffix + ")" : Path.GetFileNameWithoutExtension(root) + " (" + suffix + ")" + Path.GetExtension(root);
                string destination = Path.Combine(_directory, name);
                try { if (directory) Directory.Move(source, destination); else File.Move(source, destination, false); destinations[root] = destination; break; }
                catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
            }
        }
        string[] saved = _files.Select(file => { var parts = file.RelativePath!.Split('/'); return parts.Length == 1 ? destinations[parts[0]] : Path.Combine(destinations[parts[0]], Path.Combine(parts.Skip(1).ToArray())); }).ToArray();
        _committed = true; return saved;
    }
    public void Dispose()
    {
        _output?.Dispose(); _output = null;
        // Only this receiver's unpredictable temporary directory is removable.
        if (!Path.GetFullPath(_temporary).StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary directory");
        if (Directory.Exists(_temporary)) Directory.Delete(_temporary, true);
    }
}
