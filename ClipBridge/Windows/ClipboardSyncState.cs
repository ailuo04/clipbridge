using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClipBridge.Windows;

internal sealed class ClipboardSyncState
{
    private readonly object _gate = new();
    private string? _targetPeerId;
    private string? _suppressedIncoming;
    private string? _lastSent;
    private bool _enabled;
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public string? TargetPeerId { get { lock (_gate) return _enabled ? _targetPeerId : null; } }
    public void Enable(string peerId) { if (string.IsNullOrWhiteSpace(peerId)) throw new ArgumentException("请先选择已配对设备", nameof(peerId)); lock (_gate) { _targetPeerId = peerId; _lastSent = null; _enabled = true; } }
    public void Disable() { lock (_gate) { _enabled = false; _targetPeerId = null; _lastSent = null; } }
    public void SuppressIncoming(string text) { lock (_gate) _suppressedIncoming = text; }
    public bool IsSuppressed(string text) { lock (_gate) { if (_suppressedIncoming == text) return true; _suppressedIncoming = null; return false; } }
    public bool ShouldSend(string text) { lock (_gate) { if (!_enabled || string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > SharedProtocol.MaxTextBytes || text == _lastSent) return false; _lastSent = text; return true; } }
}

internal sealed record ClipboardReadResult(string Status, string Text = "", long TimestampUtc = 0, string MessageId = "");
internal sealed record ClipboardRequestMessage(string Type, string MessageId, string OriginDeviceId, string TargetDeviceId, long TimestampUtc);
internal sealed record ClipboardResponseMessage(string Type, string MessageId, string OriginDeviceId, long TimestampUtc, string Status, string Text);
internal static class ClipboardSyncProtocol
{
    public static ClipboardRequestMessage Request(string requestId, string originId, string targetId, long now) => new("clipboardRequest", requestId, originId, targetId, now);
    public static ClipboardResponseMessage Response(string requestId, string responderId, ClipboardReadResult result, long now) => new("clipboardResponse", requestId, responderId, now, result.Status, result.Status == "ok" ? result.Text : "");
    public static ClipboardReadResult ReadResponse(JsonDocument document, string expectedId, string expectedPeerId, long now)
    {
        JsonElement root = document.RootElement;
        if (root.GetProperty("Type").GetString() != "clipboardResponse" || root.GetProperty("MessageId").GetString() != expectedId || root.GetProperty("OriginDeviceId").GetString() != expectedPeerId || !SharedProtocol.IsFresh(root.GetProperty("TimestampUtc").GetInt64())) throw new InvalidDataException("剪贴板同步响应无效");
        string status = root.GetProperty("Status").GetString() ?? "error";
        string text = root.GetProperty("Text").GetString() ?? "";
        if (status is not ("ok" or "empty" or "foreground" or "disabled" or "error") || status == "ok" && (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > SharedProtocol.MaxTextBytes) || status != "ok" && text.Length != 0) throw new InvalidDataException("剪贴板同步内容无效");
        return new(status, text, root.GetProperty("TimestampUtc").GetInt64(), expectedId);
    }
    public static ClipboardRequestMessage ReadRequest(JsonDocument document, string expectedPeerId, string localId)
    {
        var request = JsonSerializer.Deserialize<ClipboardRequestMessage>(document.RootElement.GetRawText()) ?? throw new InvalidDataException("剪贴板同步请求无效");
        if (request.Type != "clipboardRequest" || string.IsNullOrWhiteSpace(request.MessageId) || request.MessageId.Length > 64 || request.OriginDeviceId != expectedPeerId || request.TargetDeviceId != localId || !SharedProtocol.IsFresh(request.TimestampUtc)) throw new InvalidDataException("剪贴板同步请求无效");
        return request;
    }
}
