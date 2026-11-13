package app.clipbridge.ui

import android.content.Context
import android.graphics.Color
import android.net.Uri
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import app.clipbridge.history.HistoryLocalRecord

class TimelineRowView(
    context: Context, private val ui: BridgeUi, val record: HistoryLocalRecord,
    localDeviceId: String, val peerIsComputer: Boolean, private val onCopy: (String) -> Unit,
    private val onOpen: (Uri) -> Unit, private val onShare: (HistoryLocalRecord) -> Unit,
    private val onDelete: (HistoryLocalRecord) -> Unit
) : LinearLayout(context) {
    val localDeviceIdBound = localDeviceId
    private val outgoing = record.event.senderDeviceId == localDeviceId
    /** Sender's avatar: this phone on the right, the other device on the left. */
    private fun avatar(): View = FrameLayout(context).apply {
        background = ui.background(if(outgoing) ui.muted else ui.primaryFill,10)
        addView(ui.icon(if(outgoing || !peerIsComputer) 8 else 0,Color.WHITE),FrameLayout.LayoutParams(ui.dp(20),ui.dp(20),Gravity.CENTER))
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
    }
    init {
        orientation = HORIZONTAL
        layoutDirection = View.LAYOUT_DIRECTION_LTR
        gravity = Gravity.TOP
        setPadding(ui.dp(4), ui.dp(4), ui.dp(4), ui.dp(10))
        val column = ui.column()
        val visible = record.event.files.withIndex().filter { it.value.relativePath?.contains('/') != true }
        val imageExtensions = setOf("png","jpg","jpeg","gif","bmp","webp")
        // A single picture is shown as the picture itself, like a chat photo.
        val picture = visible.singleOrNull()?.takeIf { !it.value.isDirectory && it.value.name.substringAfterLast('.').lowercase(java.util.Locale.ROOT) in imageExtensions && record.localFileReferences[it.index.toString()] != null && !record.event.state.contains("%") }
        val bubble = ui.column(if(picture != null) 4 else 14).apply { background = ui.background(if(outgoing && record.event.contentType=="text") ui.tint else ui.surface,if(picture != null) 12 else 18) }
        val spacer = View(context)
        val avatarParams = { LayoutParams(ui.dp(36),ui.dp(36)).apply { if(outgoing) marginStart = ui.dp(8) else marginEnd = ui.dp(8) } }
        if(outgoing) addView(spacer,LayoutParams(0,1,.12f)) else addView(avatar(),avatarParams())
        addView(column,LayoutParams(0,-2,.88f))
        if(outgoing) addView(avatar(),avatarParams()) else addView(spacer,LayoutParams(0,1,.12f))
        column.addView(bubble,LayoutParams(-1,-2))
        contentDescription = "${UiPresentation.historyDirection(outgoing)}，${UiPresentation.historyContentKind(record)}"
        val actions = if(record.event.state.contains("%")) emptyList() else UiPresentation.historyActions(record)
        fun menu(anchor:View) {
            if(actions.isEmpty()) return
            val popup = PopupMenu(context,anchor)
            actions.forEachIndexed { index, title -> popup.menu.add(0,index,index,title) }
            popup.setOnMenuItemClickListener { item ->
                when(actions[item.itemId]) {
                    "复制" -> onCopy(record.event.text.orEmpty())
                    "打开本地文件" -> record.localFileReferences.values.firstOrNull()?.let { onOpen(Uri.parse(it)) }
                    "分享" -> onShare(record)
                    "删除" -> onDelete(record)
                }; true
            }
            popup.show()
        }
        bubble.setOnLongClickListener { menu(bubble); true }
        if(record.event.contentType=="text") {
            bubble.addView(ui.text(record.event.text.orEmpty(),15f).apply { setOnLongClickListener { menu(bubble); true } })
        } else if(picture != null) {
            val uri=Uri.parse(record.localFileReferences.getValue(picture.index.toString()))
            val preview=android.widget.ImageView(context).apply { scaleType=android.widget.ImageView.ScaleType.FIT_CENTER; adjustViewBounds=true; maxHeight=ui.dp(240); minimumHeight=ui.dp(120); contentDescription=picture.value.name+" · 图片" }
            bubble.addView(preview,LayoutParams(-1,-2))
            MediaThumbnail.load(preview,uri)
            preview.setOnClickListener { onOpen(uri) }; preview.setOnLongClickListener { menu(bubble); true }
        } else {
            visible.forEach { (index,file) ->
                val uri=record.localFileReferences[index.toString()]?.let(Uri::parse)
                val extension=file.name.substringAfterLast('.').lowercase(java.util.Locale.ROOT)
                if(uri!=null && extension in imageExtensions+setOf("mp4","mov","mkv","webm")) {
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
        val meta=ui.row()
        val stamp=ui.text(time+" · "+UiPresentation.historyState(record.event.state),10f,ui.muted).apply { gravity=if(outgoing) Gravity.END else Gravity.START }
        // Visible entry point for the same actions as the long-press menu.
        val more=ui.text("•••",11f,ui.muted).apply {
            gravity=Gravity.CENTER; minimumWidth=ui.dp(40); minimumHeight=ui.dp(28); contentDescription="更多操作"
            isClickable=true; isFocusable=true; setOnClickListener { menu(this) }
        }
        if(outgoing && actions.isNotEmpty()) meta.addView(more)
        meta.addView(stamp,LayoutParams(0,-2,1f))
        if(!outgoing && actions.isNotEmpty()) meta.addView(more)
        column.addView(meta,LayoutParams(-1,-2))
    }
}
