using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ClipBridge.Windows;

internal sealed record TransferSource(string FullPath, string Name, string? RelativePath, bool IsDirectory)
{
    public static TransferSource[] Expand(string[] selections, CancellationToken token)
    {
        bool tree = selections.Any(Directory.Exists);
        var result = new List<TransferSource>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string selection in selections)
        {
            string full = Path.GetFullPath(selection).TrimEnd(Path.DirectorySeparatorChar);
            string name = Path.GetFileName(full); TransferProtocol.ValidateFileName(name);
            string unique = name;
            for (int suffix = 1; !roots.Add(unique); suffix++) unique = Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name);
            Visit(full, tree ? unique : null, unique);
        }
        return result.ToArray();
        void Visit(string full, string? relative, string name)
        {
            token.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(full);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("不发送符号链接或目录联接：" + name);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            if (result.Count >= 256) throw new IOException("一次最多发送 256 个文件和目录条目，请分批发送");
            TransferProtocol.ValidateFileName(name);
            if (relative != null) TransferProtocol.ValidateRelativePath(relative);
            result.Add(new(full, name, relative, directory));
            if (directory) foreach (string child in Directory.EnumerateFileSystemEntries(full).OrderBy(x => x, StringComparer.Ordinal))
                Visit(child, relative + "/" + Path.GetFileName(child), Path.GetFileName(child));
        }
    }
}
