package app.clipbridge.history

import app.clipbridge.protocol.BridgeProtocol
import app.clipbridge.protocol.FileProtocol
import java.nio.charset.StandardCharsets

object HistoryPolicy {
    const val SCHEMA_VERSION = 1
    const val SEVEN_DAYS_MILLIS = 7L * 24 * 60 * 60 * 1000

    fun isVisible(createdUtc: Long, nowUtc: Long): Boolean =
        createdUtc > nowUtc - SEVEN_DAYS_MILLIS && createdUtc <= nowUtc

    fun validateEvent(value: HistorySyncEvent, authenticatedPeerId: String, nowUtc: Long) {
        requireId(value.eventId, "eventId")
        requireId(value.peerId, "peerId")
        requireId(value.senderDeviceId, "senderDeviceId")
        requireId(authenticatedPeerId, "authenticated peerId")
        require(value.schemaVersion == SCHEMA_VERSION) { "Unsupported history event schema" }
        require(value.peerId == authenticatedPeerId) { "History event peer does not match the authenticated route" }
        require(value.createdUtc <= nowUtc) { "History event timestamp is in the future" }
        value.text?.let { require(it.toByteArray(StandardCharsets.UTF_8).size <= BridgeProtocol.MAX_TEXT) { "History text exceeds the shared protocol limit" } }
        value.files.forEach { file ->
            FileProtocol.validateName(file.name)
            file.relativePath?.let { FileProtocol.validateRelativePath(it); require(it.substringAfterLast('/')==file.name) }
            require(!file.isDirectory || (file.relativePath!=null && file.size==0L && file.sha256.equals(FileProtocol.EMPTY_HASH,true)))
            require(file.size >= 0) { "History file size cannot be negative" }
            require(file.sha256.matches(Regex("[0-9a-fA-F]{64}"))) { "Invalid history file SHA-256" }
        }
    }

    fun validateTombstone(value: HistoryTombstone, authenticatedPeerId: String) {
        requireId(value.eventId, "eventId")
        requireId(value.peerId, "peerId")
        requireId(authenticatedPeerId, "authenticated peerId")
        require(value.peerId == authenticatedPeerId) { "History tombstone peer does not match the authenticated route" }
    }

    fun validatePageEnvelope(value: HistorySyncPage, authenticatedPeerId: String) {
        require(value.schemaVersion == SCHEMA_VERSION) { "Unsupported history sync schema" }
        requireId(value.requestId, "requestId")
        requireId(value.peerId, "peerId")
        requireId(authenticatedPeerId, "authenticated peerId")
        require(value.peerId == authenticatedPeerId) { "History page peer does not match the authenticated route" }
        require(value.pageNumber >= 0) { "History page number cannot be negative" }
        require(value.entries != null) { "History page entries are required" }
        require(value.tombstones != null) { "History page tombstones are required" }
    }
    private fun requireId(value: String, name: String) {
        require(value.isNotBlank()) { "History $name is required" }
    }
}