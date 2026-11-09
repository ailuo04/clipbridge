package app.clipbridge.ui

import android.content.Context
import android.net.Uri
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.PopupMenu
import app.clipbridge.history.HistoryLocalRecord

class TimelineRowView(
    context: Context, private val ui: BridgeUi, val record: HistoryLocalRecord,
    localDeviceId: String, private val onCopy: (String) -> Unit,
    private val onOpen: (Uri) -> Unit, private val onShare: (HistoryLocalRecord) -> Unit,
    private val onDelete: (HistoryLocalRecord) -> Unit
) : LinearLayout(context) {
    val localDeviceIdBound = localDeviceId
    private val outgoing = record.event.senderDeviceId == localDeviceId
    init {
        orientation = HORIZONTAL
        layoutDirection = View.LAYOUT_DIRECTION_LTR
        setPadding(ui.dp(4), ui.dp(4), ui.dp(4), ui.dp(10))
        val column = ui.column()
        val bubble = ui.column(14).apply { background = ui.background(if(outgoing) ui.tint else ui.surface,18) }
        val spacer = View(context)
        if(outgoing) addView(spacer,LayoutParams(0,1,.16f))
        addView(column,LayoutParams(0,-2,.84f))
        if(!outgoing) addView(spacer,LayoutParams(0,1,.16f))
        column.addView(bubble,LayoutParams(-1,-2))
        contentDescription = "${UiPresentation.historyDirection(outgoing)}，${UiPresentation.historyContentKind(record)}"
        fun menu(anchor:View) {
            if(record.event.state.contains("%")) return
            val popup = PopupMenu(context,anchor)
            val actions = UiPresentation.historyActions(record)
            actions.forEachIndexed { index, title -> popup.menu.add(0,index,index,title) }
            popup.setOnMenuItemClickListener { item ->
                when(actions[item.itemId]) {
                    "复制" -> onCopy(record.event.text.orEmpty())
                    "打开本地文件" -> record.localFileReferences.values.firstOrNull()?.let { onOpen(Uri.parse(it)) }
                    "分享" -> onShare(record)
                    "删除记录" -> onDelete(record)
                }; true
            }
            popup.show()
        }
        bubble.setOnLongClickListener { menu(bubble); true }
        if(record.event.contentType=="text") {
            bubble.addView(ui.text(record.event.text.orEmpty(),15f).apply { setOnLongClickListener { menu(bubble); true } })
        } else {
            record.event.files.forEachIndexed { index,file ->
                if(file.relativePath?.contains('/')==true) return@forEachIndexed
                val uri=record.localFileReferences[index.toString()]?.let(Uri::parse)
                val extension=file.name.substringAfterLast('.').lowercase(java.util.Locale.ROOT)
                if(uri!=null && extension in setOf("png","jpg","jpeg","gif","bmp","webp","mp4","mov","mkv","webm")) {
                    val preview=android.widget.ImageView(context).apply { scaleType=android.widget.ImageView.ScaleType.FIT_CENTER; contentDescription=file.name+" · 原文件" }
                    bubble.addView(preview,LayoutParams(-1,ui.dp(170)))
                    MediaThumbnail.load(preview,uri)
                    preview.setOnClickListener { onOpen(uri) }; preview.setOnLongClickListener { menu(bubble); true }
                }
                val row=ui.row()
                row.addView(ui.text(if(file.isDirectory) "▣" else if(extension in setOf("mp4","mov","mkv","webm","avi","m4v")) "▶" else "▤",28f,ui.primary),LayoutParams(ui.dp(42),-2))
                val details=ui.column()
                details.addView(ui.text(file.name,14f,bold=true))
                details.addView(ui.text(if(file.isDirectory) "文件夹 · 点击打开" else ui.size(file.size),12f,ui.muted))
                row.addView(details,LayoutParams(0,-2,1f))
                row.setOnClickListener { record.localFileReferences[index.toString()]?.let { onOpen(Uri.parse(it)) } }
                row.setOnLongClickListener { menu(bubble); true }
                bubble.addView(row)
            }
            if(record.event.state.contains("%")) {
                val percent=record.event.state.substringAfter(" · ").removeSuffix("%").toIntOrNull() ?: 0
                bubble.addView(ui.progress(percent,record.event.state))
            }
        }
        val time=java.text.SimpleDateFormat("MM-dd HH:mm",java.util.Locale.getDefault()).format(java.util.Date(record.event.createdUtc))
        column.addView(ui.text(time+" · "+UiPresentation.historyState(record.event.state),10f,ui.muted).apply { gravity=if(outgoing) Gravity.END else Gravity.START },LayoutParams(-1,-2))
    }
}
