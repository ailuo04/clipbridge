namespace ClipBridge.Windows;

internal sealed record AvailableUpdate(string Version, string DownloadUrl, long Size, string Sha256, string Notes);
