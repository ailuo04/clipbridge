package app.clipbridge.history

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.RandomAccessFile
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.UUID

/** Syncable snapshot intentionally contains events and tombstones only, never local file references. */
data class HistoryStoreSnapshot(
    val entries: List<HistorySyncEvent>,
    val tombstones: List<HistoryTombstone>
)

/** Per-peer JSONL history log. History operations never modify referenced local files. */
class HistoryStore(filesDir: File) {
    private data class Key(val peerId: String, val eventId: String)

    private val lock = Any()
    private val logFile = File(File(filesDir, "history"), "events-v1.jsonl")
    private val events = LinkedHashMap<Key, HistoryLocalRecord>()
    private val tombstones = LinkedHashMap<Key, HistoryTombstone>()

    init {
        load()
    }

    fun upsert(entry: HistoryLocalRecord) = synchronized(lock) {
        HistoryPolicy.validateEvent(entry.event, entry.event.peerId, System.currentTimeMillis())
        val key = Key(entry.event.peerId, entry.event.eventId)
        if (tombstones.containsKey(key)) return@synchronized
        val incoming = copyRecord(entry)
        val current = events[key]
        if (current != null && current.event == incoming.event && current.localFileReferences == incoming.localFileReferences) return@synchronized
        val merged = mergeLocalReferences(current, incoming)
        append(operationForUpsert(merged))
        events[key] = merged
    }

    /** Returns visible records newest-first, with event ID as a deterministic tie-breaker. */
    fun getRecent(peerId: String, nowUtc: Long): List<HistoryLocalRecord> = synchronized(lock) {
        require(peerId.isNotBlank()) { "History peer ID is required" }
        events.values
            .filter { it.event.peerId == peerId && HistoryPolicy.isVisible(it.event.createdUtc, nowUtc) }
            .sortedWith(compareByDescending<HistoryLocalRecord> { it.event.createdUtc }.thenBy { it.event.eventId })
            .map(::copyRecord)
    }

    fun deleteRecord(peerId: String, eventId: String, nowUtc: Long) = synchronized(lock) {
        require(peerId.isNotBlank()) { "History peer ID is required" }
        require(eventId.isNotBlank()) { "History event ID is required" }
        val key = Key(peerId, eventId)
        val current = tombstones[key]
        if (current != null && current.deletedUtc >= nowUtc) {
            events.remove(key)
            return@synchronized
        }
        val tombstone = HistoryTombstone(eventId, peerId, nowUtc)
        append(operationForTombstone(tombstone))
        events.remove(key)
        tombstones[key] = tombstone
    }

    fun merge(peerId: String, entries: List<HistorySyncEvent>, tombstones: List<HistoryTombstone>, nowUtc: Long) = synchronized(lock) {
        require(peerId.isNotBlank()) { "History peer ID is required" }
        tombstones.forEach { HistoryPolicy.validateTombstone(it, peerId) }
        entries.forEach { HistoryPolicy.validateEvent(it, peerId, nowUtc) }
        tombstones.forEach(::mergeTombstone)
        entries.forEach { entry ->
            val key = Key(entry.peerId, entry.eventId)
            if (tombstonesById(key)) return@forEach
            val current = events[key]
            if (current?.event == entry) return@forEach
            val merged = mergeLocalReferences(current, HistoryLocalRecord(entry))
            append(operationForUpsert(merged))
            events[key] = merged
        }
    }

    fun getSyncSnapshot(peerId: String, nowUtc: Long): HistoryStoreSnapshot = synchronized(lock) {
        require(peerId.isNotBlank()) { "History peer ID is required" }
        val entries = events.values
            .filter { it.event.peerId == peerId && HistoryPolicy.isVisible(it.event.createdUtc, nowUtc) }
            .map { it.event }
            .sortedWith(compareByDescending<HistorySyncEvent> { it.createdUtc }.thenBy { it.eventId })
        val peerTombstones = tombstones.values
            .filter { it.peerId == peerId }
            .sortedWith(compareByDescending<HistoryTombstone> { it.deletedUtc }.thenBy { it.eventId })
        HistoryStoreSnapshot(entries, peerTombstones)
    }

    fun prune(nowUtc: Long) = synchronized(lock) {
        val expired = events.keys.filter { key ->
            val createdUtc = events.getValue(key).event.createdUtc
            createdUtc <= nowUtc - HistoryPolicy.SEVEN_DAYS_MILLIS
        }
        if (expired.isEmpty()) return@synchronized
        expired.forEach(events::remove)
        compact()
    }

    private fun load() = synchronized(lock) {
        if (!logFile.isFile) return@synchronized
        val bytes = logFile.readBytes()
        var start = 0
        while (start < bytes.size) {
            var newline = start
            while (newline < bytes.size && bytes[newline] != '\n'.code.toByte()) newline++
            val hasNewline = newline < bytes.size
            val end = if (hasNewline) newline else bytes.size
            var valid = false
            try {
                val decoder = StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                val line = decoder.decode(ByteBuffer.wrap(bytes, start, end - start)).toString().trimEnd('\r')
                require(line.isNotBlank()) { "Empty history log line" }
                applyLoadedOperation(parseOperation(line))
                valid = true
            } catch (exception: Exception) {
                if (exception is java.io.IOException && exception !is java.nio.charset.CharacterCodingException) throw exception
                val isTrailingLine = !hasNewline || newline == bytes.lastIndex
                if (isTrailingLine) {
                    truncateLog(start)
                    return@synchronized
                }
            }
            if (!hasNewline) {
                if (valid) appendLineTerminator()
                return@synchronized
            }
            start = newline + 1
        }
    }

    private fun appendLineTerminator() {
        FileOutputStream(logFile, true).use { stream ->
            stream.write('\n'.code)
            stream.fd.sync()
        }
    }

    private fun parseOperation(line: String): JSONObject = JSONObject(line)

    private fun applyLoadedOperation(operation: JSONObject) {
        require(operation.getInt("schemaVersion") == LOG_SCHEMA_VERSION) { "Unsupported history log schema" }
        when (operation.getString("operation")) {
            UPSERT_OPERATION -> {
                val record = operation.getJSONObject("record")
                val event = parseEvent(record.getJSONObject("event"))
                HistoryPolicy.validateEvent(event, event.peerId, Long.MAX_VALUE)
                val referencesObject = record.optJSONObject("localFileReferences") ?: JSONObject()
                val references = linkedMapOf<String, String>()
                referencesObject.keys().forEach { id -> references[id] = referencesObject.getString(id) }
                applyUpsert(HistoryLocalRecord(event, references))
            }
            TOMBSTONE_OPERATION -> applyTombstone(parseTombstone(operation.getJSONObject("tombstone")))
            else -> throw IllegalArgumentException("Invalid history operation")
        }
    }

    private fun applyUpsert(entry: HistoryLocalRecord) {
        val key = Key(entry.event.peerId, entry.event.eventId)
        if (tombstones.containsKey(key)) return
        events[key] = mergeLocalReferences(events[key], entry)
    }

    private fun mergeTombstone(tombstone: HistoryTombstone) {
        val key = Key(tombstone.peerId, tombstone.eventId)
        val current = this.tombstones[key]
        if (current != null && current.deletedUtc >= tombstone.deletedUtc) {
            events.remove(key)
            return
        }
        append(operationForTombstone(tombstone))
        events.remove(key)
        this.tombstones[key] = tombstone
    }

    private fun applyTombstone(tombstone: HistoryTombstone) {
        val key = Key(tombstone.peerId, tombstone.eventId)
        events.remove(key)
        val current = tombstones[key]
        if (current == null || current.deletedUtc < tombstone.deletedUtc) tombstones[key] = tombstone
    }

    private fun append(operation: JSONObject) {
        logFile.parentFile?.mkdirs()
        FileOutputStream(logFile, true).use { stream ->
            stream.write((operation.toString() + "\n").toByteArray(StandardCharsets.UTF_8))
            stream.fd.sync()
        }
    }

    private fun compact() {
        logFile.parentFile?.mkdirs()
        val temporary = File(logFile.parentFile, "${logFile.name}.tmp.${UUID.randomUUID()}")
        FileOutputStream(temporary, false).use { stream ->
            events.values
                .sortedWith(compareBy<HistoryLocalRecord> { it.event.peerId }.thenBy { it.event.eventId })
                .forEach { stream.write((operationForUpsert(it).toString() + "\n").toByteArray(StandardCharsets.UTF_8)) }
            tombstones.values
                .sortedWith(compareBy<HistoryTombstone> { it.peerId }.thenBy { it.eventId })
                .forEach { stream.write((operationForTombstone(it).toString() + "\n").toByteArray(StandardCharsets.UTF_8)) }
            stream.fd.sync()
        }
        Files.move(temporary.toPath(), logFile.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
    }

    private fun truncateLog(length: Int) {
        RandomAccessFile(logFile, "rw").use { file ->
            file.setLength(length.toLong())
            file.fd.sync()
        }
    }

    private fun operationForUpsert(record: HistoryLocalRecord): JSONObject = JSONObject()
        .put("schemaVersion", LOG_SCHEMA_VERSION)
        .put("operation", UPSERT_OPERATION)
        .put("record", JSONObject()
            .put("event", eventToJson(record.event))
            .put("localFileReferences", JSONObject().also { references ->
                record.localFileReferences.toSortedMap().forEach { (id, localReference) -> references.put(id, localReference) }
            }))
        .put("tombstone", JSONObject.NULL)

    private fun operationForTombstone(tombstone: HistoryTombstone): JSONObject = JSONObject()
        .put("schemaVersion", LOG_SCHEMA_VERSION)
        .put("operation", TOMBSTONE_OPERATION)
        .put("record", JSONObject.NULL)
        .put("tombstone", tombstoneToJson(tombstone))

    private fun eventToJson(event: HistorySyncEvent): JSONObject = JSONObject()
        .put("schemaVersion", event.schemaVersion)
        .put("eventId", event.eventId)
        .put("peerId", event.peerId)
        .put("senderDeviceId", event.senderDeviceId)
        .put("createdUtc", event.createdUtc)
        .put("contentType", event.contentType)
        .put("text", event.text ?: JSONObject.NULL)
        .put("files", JSONArray().also { array -> event.files.forEach { array.put(fileToJson(it)) } })
        .put("state", event.state)

    private fun fileToJson(file: HistoryFileMetadata): JSONObject = JSONObject()
        .put("name", file.name)
        .put("size", file.size)
        .put("sha256", file.sha256)

    private fun tombstoneToJson(tombstone: HistoryTombstone): JSONObject = JSONObject()
        .put("eventId", tombstone.eventId)
        .put("peerId", tombstone.peerId)
        .put("deletedUtc", tombstone.deletedUtc)

    private fun parseEvent(json: JSONObject): HistorySyncEvent {
        val files = json.getJSONArray("files")
        val metadata = (0 until files.length()).map { index ->
            val item = files.getJSONObject(index)
            HistoryFileMetadata(item.getString("name"), item.getLong("size"), item.getString("sha256"))
        }
        val textValue = json.opt("text")
        val text = if (textValue == null || textValue === JSONObject.NULL) null else textValue as String
        return HistorySyncEvent(
            json.getInt("schemaVersion"),
            json.getString("eventId"),
            json.getString("peerId"),
            json.getString("senderDeviceId"),
            json.getLong("createdUtc"),
            json.getString("contentType"),
            text,
            metadata,
            json.getString("state")
        )
    }

    private fun parseTombstone(json: JSONObject) = HistoryTombstone(
        json.getString("eventId"), json.getString("peerId"), json.getLong("deletedUtc")
    )

    private fun mergeLocalReferences(current: HistoryLocalRecord?, incoming: HistoryLocalRecord): HistoryLocalRecord {
        if (current == null || current.localFileReferences.isEmpty()) return copyRecord(incoming)
        if (incoming.localFileReferences.isEmpty()) return incoming.copy(localFileReferences = current.localFileReferences.toMap())
        return incoming.copy(localFileReferences = current.localFileReferences + incoming.localFileReferences)
    }

    private fun copyRecord(record: HistoryLocalRecord) = record.copy(localFileReferences = record.localFileReferences.toMap())

    private fun tombstonesById(key: Key) = tombstones.containsKey(key)

    private companion object {
        const val LOG_SCHEMA_VERSION = 1
        const val UPSERT_OPERATION = "upsert"
        const val TOMBSTONE_OPERATION = "tombstone"
    }
}