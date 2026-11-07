using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

/// <summary>
/// Keeps timeline copy, state, and expiry wording independent from WinForms controls.
/// </summary>
internal static class HistoryPresentation
{
    private const long DayMilliseconds = 24L * 60 * 60 * 1000;

    public static string Direction(bool isOutgoing) => isOutgoing ? "已发送" : "收到";

    public static string ContentKind(string contentType) =>
        string.Equals(contentType, "file", StringComparison.OrdinalIgnoreCase) ? "文件" : "文本";

    public static string ContentKind(HistorySyncEvent entry) => ContentKind(entry.ContentType);

    public static string State(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "未知状态";
        if (state.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("error", StringComparison.OrdinalIgnoreCase)) return "失败";
        if (state.Contains("拒绝", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("reject", StringComparison.OrdinalIgnoreCase)) return "已拒绝";
        if (state.Contains("取消", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return "已取消";
        if (state.Contains("传输中", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("active", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("transferring", StringComparison.OrdinalIgnoreCase)) return "传输中";
        if (state.Contains("待接受", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("等待", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("waiting", StringComparison.OrdinalIgnoreCase)) return "待接受";
        if (state.Contains("准备", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("preparing", StringComparison.OrdinalIgnoreCase)) return "准备中";
        if (state.Equals("sent", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("已保存", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("完成", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
            state.Contains("saved", StringComparison.OrdinalIgnoreCase)) return "已完成";
        return state;
    }

    public static IReadOnlyList<string> Actions(string contentType, bool hasLocalFile)
    {
        if (string.Equals(contentType, "file", StringComparison.OrdinalIgnoreCase))
            return hasLocalFile ? new[] { "打开本地文件", "删除记录" } : new[] { "删除记录" };
        return new[] { "复制", "删除记录" };
    }

    public static IReadOnlyList<string> Actions(HistoryLocalRecord entry) =>
        Actions(entry.Event.ContentType, entry.LocalFileReferences.Count > 0);

    public static string ExpiryLabel(long createdUtc, long nowUtc)
    {
        long remaining = createdUtc + HistoryPolicy.SevenDaysMilliseconds - nowUtc;
        if (remaining <= 0) return "已过期";
        long days = (remaining + DayMilliseconds - 1) / DayMilliseconds;
        return $"{days} 天后过期";
    }

    public static bool IsLocalFileAvailable(HistoryLocalRecord entry) =>
        entry.LocalFileReferences.Values.Any(File.Exists);
}


