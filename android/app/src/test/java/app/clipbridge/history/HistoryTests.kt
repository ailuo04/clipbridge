package app.clipbridge.history

import app.clipbridge.protocol.BridgeProtocol
import org.junit.Assert.*
import org.junit.Test

class HistoryTests {
    private val nowUtc = 1_800_000_000_000L
    private val peerId = "peer-a"

    @Test fun retentionUsesUtcMillisecondsAndSevenDayExclusiveBoundary() {
        val nearBoundary = nowUtc - 6L * 24 * 60 * 60 * 1000 - 23L * 60 * 60 * 1000 - 59L * 60 * 1000
        assertTrue(HistoryPolicy.isVisible(nearBoundary, nowUtc))
        assertTrue(HistoryPolicy.isVisible(nowUtc - HistoryPolicy.SEVEN_DAYS_MILLIS + 1, nowUtc))
        assertFalse(HistoryPolicy.isVisible(nowUtc - HistoryPolicy.SEVEN_DAYS_MILLIS, nowUtc))
        assertFalse(HistoryPolicy.isVisible(nowUtc + 1, nowUtc))
        invalid { HistoryPolicy.validateEvent(event(createdUtc = nowUtc + 1), peerId, nowUtc) }
    }

    @Test fun validationRejectsMalformedOrOversizedEvents() {
        HistoryPolicy.validateEvent(event(), peerId, nowUtc)
        invalid { HistoryPolicy.validateEvent(event(eventId = " "), peerId, nowUtc) }
        invalid { HistoryPolicy.validateEvent(event(schemaVersion = 2), peerId, nowUtc) }
        invalid { HistoryPolicy.validateEvent(event(files = listOf(HistoryFileMetadata("a.txt", -1, "a".repeat(64)))), peerId, nowUtc) }
        invalid { HistoryPolicy.validateEvent(event(files = listOf(HistoryFileMetadata("a.txt", 1, "bad"))), peerId, nowUtc) }
        invalid { HistoryPolicy.validateEvent(event(files = listOf(HistoryFileMetadata("../unsafe.txt", 1, "a".repeat(64)))), peerId, nowUtc) }
        invalid { HistoryPolicy.validateEvent(event(text = "界".repeat(BridgeProtocol.MAX_TEXT)), peerId, nowUtc) }
        HistoryPolicy.validateEvent(event(text = "a".repeat(BridgeProtocol.MAX_TEXT)), peerId, nowUtc)
    }

    @Test fun validationRejectsEventsAndTombstonesFromAnotherPeer() {
        HistoryPolicy.validateEvent(event(peerId = peerId), peerId, nowUtc)
        invalid { HistoryPolicy.validateEvent(event(peerId = "peer-b"), peerId, nowUtc) }
        invalid { HistoryPolicy.validateTombstone(HistoryTombstone("event-1", "peer-b", nowUtc), peerId) }
    }

    @Test fun sharedDtosExposeLowerCamelCaseFieldsWithoutLocalFileReferences() {
        val eventFields = HistorySyncEvent::class.java.declaredFields.map { it.name }.toSet()
        assertTrue(eventFields.containsAll(setOf("schemaVersion", "eventId", "peerId", "senderDeviceId", "createdUtc", "contentType", "text", "files", "state")))
        assertFalse(eventFields.any { it.contains("path", ignoreCase = true) || it.contains("uri", ignoreCase = true) })
        assertEquals(setOf("name", "size", "sha256"), HistoryFileMetadata::class.java.declaredFields.map { it.name }.filterNot { it.startsWith("$" ) }.toSet())
        assertEquals(setOf("eventId", "peerId", "deletedUtc"), HistoryTombstone::class.java.declaredFields.map { it.name }.toSet())
        val page = HistorySyncPage(1, "request-1", peerId, 0, listOf(event()), emptyList())
        assertEquals(1, page.schemaVersion)
    }

    @Test fun pageEnvelopeRejectsEmptyRequestId() {
        val page = HistorySyncPage(1, "", peerId, 0, listOf(event()), emptyList())
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }

    @Test fun pageEnvelopeRejectsUnsupportedSchemaVersion() {
        val page = HistorySyncPage(2, "request-1", peerId, 0, listOf(event()), emptyList())
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }

    @Test fun pageEnvelopeRejectsPeerMismatchWithAuthenticatedRoute() {
        val page = HistorySyncPage(1, "request-1", "peer-b", 0, listOf(event()), emptyList())
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }
    @Test fun pageEnvelopeRejectsNegativePageNumber() {
        val page = HistorySyncPage(1, "request-1", peerId, -1, listOf(event()), emptyList())
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }
    @Test fun pageEnvelopeRejectsNullEntries() {
        val page = HistorySyncPage(1, "request-1", peerId, 0, null, emptyList())
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }

    @Test fun pageEnvelopeRejectsNullTombstones() {
        val page = HistorySyncPage(1, "request-1", peerId, 0, emptyList(), null)
        invalid { HistoryPolicy.validatePageEnvelope(page, peerId) }
    }
    private fun event(
        schemaVersion: Int = 1,
        eventId: String = "event-1",
        peerId: String = this.peerId,
        createdUtc: Long = nowUtc,
        text: String? = "hello",
        files: List<HistoryFileMetadata> = emptyList()
    ) = HistorySyncEvent(schemaVersion, eventId, peerId, "device-a", createdUtc, "text", text, files, "sent")

    private fun invalid(block: () -> Unit) {
        try { block(); fail("Expected invalid history data to be rejected") }
        catch (_: IllegalArgumentException) { }
        catch (_: java.io.IOException) { }
    }
}