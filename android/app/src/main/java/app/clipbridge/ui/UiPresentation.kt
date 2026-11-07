package app.clipbridge.ui

import app.clipbridge.history.HistoryLocalRecord
import app.clipbridge.history.HistoryPolicy
import java.util.Locale

/** Presentation-only rules: no Activity, storage, network or clipboard access. */
object UiPresentation {
    fun historyDirection(isOutgoing: Boolean): String = if (isOutgoing) "已发送" else "收到"

    fun historyContentKind(entry: HistoryLocalRecord): String = if (entry.event.contentType == "file") "文件" else "文本"

    fun historyState(state: String): String = when {
        state.contains("失败", true) || state.contains("failed", true) || state.contains("error", true) -> "失败"
        state.contains("拒绝", true) || state.contains("reject", true) -> "已拒绝"
        state.contains("取消", true) || state.contains("cancel", true) -> "已取消"
        state.contains("传输中", true) || state.contains("active", true) -> "传输中"
        state.contains("待接受", true) || state.contains("等待", true) || state.contains("pending", true) -> "待接受"
        state == "sent" || state.contains("完成", true) || state.contains("保存", true) || state.contains("saved", true) || state.contains("complete", true) -> "已完成"
        else -> state
    }

    fun historyActions(entry: HistoryLocalRecord): List<String> = if (entry.event.contentType == "file") {
        if (entry.localFileReferences.isNotEmpty()) listOf("打开本地文件", "分享", "删除记录") else listOf("删除记录")
    } else listOf("复制", "分享", "删除记录")

    fun expiryLabel(createdUtc: Long, nowUtc: Long): String {
        val remaining = createdUtc + HistoryPolicy.SEVEN_DAYS_MILLIS - nowUtc
        if (remaining <= 0) return "已过期"
        val day = 24L * 60 * 60 * 1000
        val days = (remaining + day - 1) / day
        return "$days 天后过期"
    }

    fun localFilesFallback(hasSystemDownloadsHandler: Boolean): String =
        if (hasSystemDownloadsHandler) "system-downloads" else "clipbridge-mediastore"

    fun percent(done: Long, total: Long): Int = if (total <= 0) 0 else
        ((done.coerceAtLeast(0).toDouble() / total.toDouble()) * 100).coerceIn(0.0, 100.0).toInt()

    fun transferGroup(state: String): String = when {
        state in setOf("准备文件", "等待对方接受", "传输中") -> "active"
        state == "待接受" -> "waiting"
        state.startsWith("已保存") || state == "对方已保存并校验" || state == "已发送" -> "finished"
        state.startsWith("失败") || state.startsWith("已取消") || state.startsWith("已拒绝") -> "unsuccessful"
        else -> "other"
    }

    fun progressUnit(state: String) = if (state == "准备文件") "files" else "bytes"
    fun scrollOffset(saved: Int, observed: Int, pendingRestore: Boolean, focusPairing: Boolean): Int =
        (if(focusPairing) 0 else if(pendingRestore) saved else observed).coerceAtLeast(0)

    fun stackActions(widthDp: Int, fontScale: Float) = widthDp < 360 || fontScale > 1.2f
    fun updateKind(state: String, ready: Boolean): String = when {
        ready -> "ready"
        state.startsWith("正在检查") -> "checking"
        state.startsWith("正在下载") -> "downloading"
        state.startsWith("更新未完成") -> "error"
        state.contains("已是最新版本") -> "latest"
        else -> "idle"
    }

    fun size(bytes: Long): String {
        val safe = bytes.coerceAtLeast(0)
        return when {
            safe < 1024 -> "$safe B"
            safe < 1048576 -> String.format(Locale.ROOT, "%.1f KB", safe / 1024.0)
            safe < 1073741824 -> String.format(Locale.ROOT, "%.1f MB", safe / 1048576.0)
            else -> String.format(Locale.ROOT, "%.2f GB", safe / 1073741824.0)
        }
    }
}

