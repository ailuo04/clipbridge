package app.clipbridge.history

/** Cross-platform event contract; local paths and URIs belong only in HistoryLocalRecord. */
data class HistorySyncEvent(
    val schemaVersion: Int,
    val eventId: String,
    val peerId: String,
    val senderDeviceId: String,
    val createdUtc: Long,
    val contentType: String,
    val text: String?,
    val files: List<HistoryFileMetadata>,
    val state: String
)

data class HistoryFileMetadata(
    val name: String,
    val size: Long,
    val sha256: String
)

data class HistoryTombstone(
    val eventId: String,
    val peerId: String,
    val deletedUtc: Long
)

data class HistorySyncPage(
    val schemaVersion: Int,
    val requestId: String,
    val peerId: String,
    val pageNumber: Int,
    val entries: List<HistorySyncEvent>,
    val tombstones: List<HistoryTombstone>
)

/** Store-only references never enter HistorySyncEvent JSON. */
data class HistoryLocalRecord(
    val event: HistorySyncEvent,
    val localFileReferences: Map<String, String> = emptyMap()
)