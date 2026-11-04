package app.clipbridge.ui

import java.util.Locale

/** Presentation-only rules: no Activity, storage, network or clipboard access. */
object UiPresentation {
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
