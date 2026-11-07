package app.clipbridge.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import app.clipbridge.history.HistoryLocalRecord
import app.clipbridge.history.HistoryFileMetadata

class TimelineRowView(
    context: Context,
    private val ui: BridgeUi,
    val record: HistoryLocalRecord,
    localDeviceId: String,
    private val onCopy: (String) -> Unit,
    private val onOpen: (Uri) -> Unit,
    private val onShare: (HistoryLocalRecord) -> Unit,
    private val onDelete: (HistoryLocalRecord) -> Unit
) : LinearLayout(context) {
    val localDeviceIdBound: String = localDeviceId
    private val outgoing = record.event.senderDeviceId == localDeviceId

    init {
        orientation = VERTICAL
        background = ui.glass(ui.surface, 20)
        setPadding(ui.dp(14), ui.dp(12), ui.dp(14), ui.dp(10))
        minimumHeight = ui.dp(if (record.event.contentType == "text") 142 else 122)
        contentDescription = "${UiPresentation.historyDirection(outgoing)}，${UiPresentation.historyContentKind(record)}，${UiPresentation.historyState(record.event.state)}"
        addView(ui.text("${UiPresentation.historyDirection(outgoing)} · ${UiPresentation.historyContentKind(record)}", 15f, bold = true))
        addView(ui.text(meta(), 11f, ui.muted))
        ui.gap(this, 6)
        if (record.event.contentType == "text") {
            val text = ui.text(record.event.text ?: "", 14f)
            text.setTextIsSelectable(true)
            text.setPadding(ui.dp(4), ui.dp(8), ui.dp(4), ui.dp(8))
            text.background = ui.background(ui.field, 12)
            addView(text, LayoutParams(-1, -2))
        } else {
            addView(ui.text(record.event.files.joinToString("\n") { "${it.name} · ${ui.size(it.size)}" }, 13f, ui.ink))
        }
        val actions = ui.row().apply { gravity = Gravity.END }
        UiPresentation.historyActions(record).forEach { action ->
            val button = ui.button(action) {
                when (action) {
                    "复制" -> onCopy(record.event.text ?: "")
                    "打开本地文件" -> record.localFileReferences.values.firstOrNull()?.let { onOpen(Uri.parse(it)) }
                    "分享" -> onShare(record)
                    "删除记录" -> onDelete(record)
                }
            }
            actions.addView(button, LayoutParams(-2, -2).apply { marginStart = ui.dp(5) })
        }
        addView(actions, LayoutParams(-1, -2).apply { topMargin = ui.dp(5) })
    }

    private fun meta(): String = "${UiPresentation.historyState(record.event.state)} · ${UiPresentation.expiryLabel(record.event.createdUtc, System.currentTimeMillis())}"
}
