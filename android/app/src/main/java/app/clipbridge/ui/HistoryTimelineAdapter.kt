package app.clipbridge.ui

import android.view.View
import android.view.ViewGroup
import android.widget.BaseAdapter
import android.widget.ListView
import app.clipbridge.history.HistoryLocalRecord

class HistoryTimelineAdapter(
    private val listView: ListView,
    private val ui: BridgeUi,
    private val localDeviceId: () -> String,
    private val onCopy: (String) -> Unit,
    private val onOpen: (android.net.Uri) -> Unit,
    private val onShare: (HistoryLocalRecord) -> Unit,
    private val onDelete: (HistoryLocalRecord) -> Unit,
    private val peerIsComputer: () -> Boolean = { true }
) : BaseAdapter() {
    private var entries: List<HistoryLocalRecord> = emptyList()

    fun submit(values: List<HistoryLocalRecord>) {
        entries = UiPresentation.conversationOrder(values)
        notifyDataSetChanged()
    }

    override fun getCount(): Int = entries.size
    override fun getItem(position: Int): HistoryLocalRecord = entries[position]
    override fun getItemId(position: Int): Long = entries[position].event.eventId.hashCode().toLong()
    override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
        val entry = getItem(position)
        val deviceId = localDeviceId()
        val recycled = convertView as? TimelineRowView
        val computer = peerIsComputer()
        if (recycled != null && recycled.record == entry && recycled.localDeviceIdBound == deviceId && recycled.peerIsComputer == computer) return recycled
        return TimelineRowView(parent.context, ui, entry, deviceId, computer, onCopy, onOpen, onShare, onDelete).apply {
            layoutParams = android.widget.AbsListView.LayoutParams(-1, -2)
        }
    }
}

