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
