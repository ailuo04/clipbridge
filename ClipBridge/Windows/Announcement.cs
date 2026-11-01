namespace ClipBridge.Windows;

internal sealed record Announcement(string Type, string DeviceId, string Name, int Port, long TimestampUtc);
