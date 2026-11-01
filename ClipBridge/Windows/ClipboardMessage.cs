namespace ClipBridge.Windows;

internal sealed record ClipboardMessage(string Type, string MessageId, string OriginDeviceId, string Text, long TimestampUtc);
