using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClipBridge.Windows;

// Which files on disk belong to ClipBridge. Deleting a history entry removes
// the copies the app itself wrote (received files, pasted images). The
// originals a user picked to send live elsewhere and are never touched.
internal static class LocalFiles
{
    internal static string ReceiveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ClipBridge");
    internal static string SentImages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "SentImages");

    public static bool IsAppOwned(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return new[] { ReceiveDirectory, SentImages }.Any(root =>
            {
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.Length > prefix.Length && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static string[] OwnedExisting(IEnumerable<string> paths) =>
        paths.Where(path => IsAppOwned(path) && (File.Exists(path) || Directory.Exists(path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Returns the paths that could not be removed.
    public static string[] Delete(IEnumerable<string> paths)
    {
        var failed = new List<string>();
        foreach (string path in paths)
        {
            if (!IsAppOwned(path)) continue;
            try
            {
                var attributes = File.GetAttributes(path);
                // Never follow a link out of the app's folders.
                if ((attributes & FileAttributes.ReparsePoint) != 0) { if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path); else File.Delete(path); }
                else if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, true);
                else File.Delete(path);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add(path); }
        }
        return failed.ToArray();
    }
}

