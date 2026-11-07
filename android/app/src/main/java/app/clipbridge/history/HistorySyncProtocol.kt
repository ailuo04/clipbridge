package app.clipbridge.history

import app.clipbridge.protocol.BridgeProtocol
import org.json.JSONArray
import org.json.JSONObject
import java.nio.charset.StandardCharsets

internal data class HistorySyncMessage(
    val type: String,
    val schemaVersion: Int,
    val requestId: String,
    val peerId: String,
    val pageNumber: Int,
    val entries: List<HistorySyncEvent>? = null,
    val tombstones: List<HistoryTombstone>? = null
)

internal object HistorySyncProtocol {
    const val VERSION = 1
    const val MAX_PAYLOAD_BYTES = BridgeProtocol.MAX_FRAME - 28
    const val MAX_PAGES_PER_CONNECTION = 64
    private val messageTypes = setOf("historySyncHello", "historySyncRequest", "historySyncPage", "historySyncAck")

    fun encode(message: HistorySyncMessage, authenticatedPeerId: String, nowUtc: Long, maxPayloadBytes: Int = MAX_PAYLOAD_BYTES): ByteArray {
        validate(message, authenticatedPeerId, nowUtc)
        require(maxPayloadBytes in 1..MAX_PAYLOAD_BYTES) { "Invalid history sync payload limit" }
        val bytes = toJson(message).toString().toByteArray(StandardCharsets.UTF_8)
        require(bytes.size <= maxPayloadBytes) { "History sync payload is too large" }
        return bytes
    }

    fun decode(payload: ByteArray, authenticatedPeerId: String, nowUtc: Long, maxPayloadBytes: Int = MAX_PAYLOAD_BYTES): HistorySyncMessage {
        require(maxPayloadBytes in 1..MAX_PAYLOAD_BYTES && payload.size <= maxPayloadBytes) { "History sync payload is too large" }
        val message = try { fromJson(JSONObject(payload.toString(StandardCharsets.UTF_8))) } catch (e: Exception) { throw IllegalArgumentException("Invalid history sync message", e) }
        validate(message, authenticatedPeerId, nowUtc)
        return message
    }

    fun createHello(requestId: String, peerId: String, pageCount: Int) = HistorySyncMessage("historySyncHello", VERSION, requestId, peerId, pageCount)
    fun createRequest(requestId: String, peerId: String, pageNumber: Int) = HistorySyncMessage("historySyncRequest", VERSION, requestId, peerId, pageNumber)
    fun createAck(requestId: String, peerId: String, pageNumber: Int) = HistorySyncMessage("historySyncAck", VERSION, requestId, peerId, pageNumber)

    fun createPages(requestId: String, peerId: String, snapshot: HistoryStoreSnapshot, maxPayloadBytes: Int = MAX_PAYLOAD_BYTES, maxPages: Int = MAX_PAGES_PER_CONNECTION, nowUtc: Long = System.currentTimeMillis()): List<HistorySyncMessage> {
        require(requestId.isValidId() && peerId.isValidId()) { "Invalid history sync identity" }
        require(maxPages in 1..MAX_PAGES_PER_CONNECTION) { "Invalid history sync page limit" }
        require(maxPayloadBytes in 1..MAX_PAYLOAD_BYTES) { "Invalid history sync payload limit" }
        val entries = snapshot.entries.map { it.copy(peerId = peerId) }.sortedWith(compareByDescending<HistorySyncEvent> { it.createdUtc }.thenBy { it.eventId })
        val tombstones = snapshot.tombstones.map { it.copy(peerId = peerId) }.sortedWith(compareByDescending<HistoryTombstone> { it.deletedUtc }.thenBy { it.eventId })
        val pages = mutableListOf<HistorySyncMessage>()
        var pageEntries = mutableListOf<HistorySyncEvent>()
        var pageTombstones = mutableListOf<HistoryTombstone>()
        var pageNumber = 0
        fun page(entries: List<HistorySyncEvent>, tombstones: List<HistoryTombstone>) = HistorySyncMessage("historySyncPage", VERSION, requestId, peerId, pageNumber, entries, tombstones)
        fun measureEmptyPage() = encode(page(emptyList(), emptyList()), peerId, nowUtc, maxPayloadBytes).size
        var currentPageBytes = measureEmptyPage()

        fun flush() {
            if (pages.size >= maxPages) throw IllegalArgumentException("History sync page count exceeds connection limit")
            val current = page(pageEntries.toList(), pageTombstones.toList())
            if (encode(current, peerId, nowUtc, maxPayloadBytes).size > maxPayloadBytes) throw IllegalArgumentException("History sync payload is too large")
            pages += current
            pageNumber++
            pageEntries = mutableListOf()
            pageTombstones = mutableListOf()
            currentPageBytes = measureEmptyPage()
        }
        fun add(entry: HistorySyncEvent?, tombstone: HistoryTombstone?, itemBytes: Int, isEntry: Boolean) {
            val sameCollectionCount = if (isEntry) pageEntries.size else pageTombstones.size
            var additionalBytes = itemBytes + if (sameCollectionCount > 0) 1 else 0
            if (currentPageBytes + additionalBytes > maxPayloadBytes) {
                if (pageEntries.isEmpty() && pageTombstones.isEmpty()) throw IllegalArgumentException("A history record cannot fit in a sync page")
                flush()
                additionalBytes = itemBytes
                if (currentPageBytes + additionalBytes > maxPayloadBytes) throw IllegalArgumentException("A history record cannot fit in a sync page")
            }
            if (entry != null) pageEntries += entry else pageTombstones += requireNotNull(tombstone)
            currentPageBytes += additionalBytes
        }
        entries.forEach { add(it, null, eventToJson(it).toString().toByteArray(StandardCharsets.UTF_8).size, true) }
        tombstones.forEach { add(null, it, tombstoneToJson(it).toString().toByteArray(StandardCharsets.UTF_8).size, false) }
        if (pageEntries.isNotEmpty() || pageTombstones.isNotEmpty()) flush()
        if (pages.isEmpty()) pages += page(emptyList(), emptyList())
        return pages
    }

    private fun validate(message: HistorySyncMessage, authenticatedPeerId: String, nowUtc: Long) {
        require(message.type in messageTypes) { "Unknown history sync message type" }
        require(message.schemaVersion == VERSION) { "Unsupported history sync version" }
        require(message.requestId.isValidId() && message.peerId.isValidId() && authenticatedPeerId.isValidId()) { "Invalid history sync identity" }
        require(message.peerId == authenticatedPeerId) { "History sync peer does not match authenticated route" }
        require(message.pageNumber >= 0) { "History sync page number cannot be negative" }
        val isPage = message.type == "historySyncPage"
        if (isPage) {
            require(message.pageNumber < MAX_PAGES_PER_CONNECTION && message.entries != null && message.tombstones != null) { "Invalid history sync page" }
        } else {
            require(message.entries == null && message.tombstones == null) { "Control messages cannot carry records" }
            if (message.type == "historySyncHello") require(message.pageNumber in 1..MAX_PAGES_PER_CONNECTION)
            if (message.type != "historySyncHello") require(message.pageNumber < MAX_PAGES_PER_CONNECTION)
        }
        message.entries?.forEach { HistoryPolicy.validateEvent(it, authenticatedPeerId, nowUtc) }
        message.tombstones?.forEach { HistoryPolicy.validateTombstone(it, authenticatedPeerId) }
    }

    private fun String.isValidId() = length == 32 && all { it in '0'..'9' || it in 'a'..'f' }

    private fun toJson(message: HistorySyncMessage): JSONObject = JSONObject()
        .put("type", message.type)
        .put("schemaVersion", message.schemaVersion)
        .put("requestId", message.requestId)
        .put("peerId", message.peerId)
        .put("pageNumber", message.pageNumber)
        .apply {
            if (message.entries != null) put("entries", JSONArray().also { array -> message.entries.forEach { array.put(eventToJson(it)) } })
            if (message.tombstones != null) put("tombstones", JSONArray().also { array -> message.tombstones.forEach { array.put(tombstoneToJson(it)) } })
        }

    private fun fromJson(json: JSONObject): HistorySyncMessage {
        val entries = if (json.has("entries") && !json.isNull("entries")) {
            val array = json.getJSONArray("entries")
            (0 until array.length()).map { eventFromJson(array.getJSONObject(it)) }
        } else null
        val tombstones = if (json.has("tombstones") && !json.isNull("tombstones")) {
            val array = json.getJSONArray("tombstones")
            (0 until array.length()).map { tombstoneFromJson(array.getJSONObject(it)) }
        } else null
        return HistorySyncMessage(json.getString("type"), json.getInt("schemaVersion"), json.getString("requestId"), json.getString("peerId"), json.getInt("pageNumber"), entries, tombstones)
    }

    private fun eventToJson(event: HistorySyncEvent) = JSONObject()
        .put("schemaVersion", event.schemaVersion).put("eventId", event.eventId).put("peerId", event.peerId)
        .put("senderDeviceId", event.senderDeviceId).put("createdUtc", event.createdUtc).put("contentType", event.contentType)
        .put("text", event.text ?: JSONObject.NULL).put("files", JSONArray().also { array -> event.files.forEach { array.put(JSONObject().put("name", it.name).put("size", it.size).put("sha256", it.sha256)) } })
        .put("state", event.state)

    private fun eventFromJson(json: JSONObject): HistorySyncEvent {
        val files = json.getJSONArray("files")
        return HistorySyncEvent(json.getInt("schemaVersion"), json.getString("eventId"), json.getString("peerId"), json.getString("senderDeviceId"), json.getLong("createdUtc"), json.getString("contentType"), if (json.isNull("text")) null else json.getString("text"), (0 until files.length()).map { val f=files.getJSONObject(it); HistoryFileMetadata(f.getString("name"),f.getLong("size"),f.getString("sha256")) }, json.getString("state"))
    }
    private fun tombstoneToJson(value: HistoryTombstone) = JSONObject().put("eventId", value.eventId).put("peerId", value.peerId).put("deletedUtc", value.deletedUtc)
    private fun tombstoneFromJson(json: JSONObject) = HistoryTombstone(json.getString("eventId"), json.getString("peerId"), json.getLong("deletedUtc"))
}
