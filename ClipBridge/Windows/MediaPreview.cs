using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace ClipBridge.Windows;

internal static class MediaPreview
{
    private static readonly Dictionary<string,Image> Cache = new();
    private static readonly object Gate = new();
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    public static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm";
    // Small copies of previews, keyed by content hash. They keep a sent picture
    // visible in the conversation after the original is moved or deleted.
    internal static string ThumbnailDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "Thumbnails");
    private static string? ThumbnailPath(string sha256) => sha256.Length == 64 && sha256.All(Uri.IsHexDigit) && sha256.Any(c => c != (char)48) ? Path.Combine(ThumbnailDirectory, sha256.ToUpperInvariant() + ".png") : null;
    public static void Remember(string sha256, Image preview)
    {
        if (ThumbnailPath(sha256) is not { } path || File.Exists(path)) return;
        try { Directory.CreateDirectory(ThumbnailDirectory); using var copy = new Bitmap(preview); copy.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { }
    }
    public static Image? Recall(string sha256)
    {
        if (ThumbnailPath(sha256) is not { } path || !File.Exists(path)) return null;
        try { using var stored = Image.FromFile(path); return new Bitmap(stored); }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
    public static void Forget(string sha256)
    {
        if (ThumbnailPath(sha256) is not { } path) return;
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    // Preview pixels are separate from the original file sent over the wire.
    public static Image? Load(string path,int maxWidth=280,int maxHeight=170)
    {
        if(!IsImage(path) || !File.Exists(path)) return null;
        try {
            var info=new FileInfo(path);
            if(info.Length > 40L*1024*1024) return null;
            string key=path+":"+info.LastWriteTimeUtc.Ticks+":"+maxWidth+":"+maxHeight;
            lock(Gate) { if(Cache.TryGetValue(key,out var cached)) return new Bitmap(cached); }
            using var source=Image.FromFile(path);
            double scale=Math.Min(1d,Math.Min(maxWidth/(double)source.Width,maxHeight/(double)source.Height));
            var preview=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
            using var graphics=Graphics.FromImage(preview); graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source,0,0,preview.Width,preview.Height);
            lock(Gate) {
                if(Cache.Count>=32) { var oldest=Cache.Keys.First(); Cache[oldest].Dispose(); Cache.Remove(oldest); }
                if(Cache.Remove(key,out var previous)) previous.Dispose();
                Cache[key]=new Bitmap(preview);
            }
            return preview;
        } catch(Exception e) when(e is IOException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
}
