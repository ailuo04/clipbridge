package app.clipbridge.history

import app.clipbridge.protocol.BridgeProtocol
import org.junit.Assert.*
import org.junit.Test
import org.junit.Rule
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.charset.StandardCharsets

class HistoryTests {
    private val nowUtc = 1_800_000_000_000L
    private val peerId = "peer-a"
    @Rule @JvmField val temporaryFolder = TemporaryFolder()

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
    @Test fun mergeRejectsConflictingEventIdsAtomically() {
        val now = System.currentTimeMillis()
        repeat(2) { order ->
            val root = temporaryFolder.newFolder("history-conflict-order-$order")
            val store = HistoryStore(root)
            val existing = event(eventId = "existing", createdUtc = now)
            store.upsert(HistoryLocalRecord(existing))
            val first = event(eventId = "duplicate", createdUtc = now, text = "first payload")
            val second = event(eventId = "duplicate", createdUtc = now, text = "second payload")
            val batch = if (order == 0) listOf(first, second) else listOf(second, first)

            invalid { store.merge(peerId, batch, listOf(HistoryTombstone("existing", peerId, now)), now) }
            assertEquals(listOf("existing"), store.getRecent(peerId, now).map { it.event.eventId })
            assertTrue(store.getSyncSnapshot(peerId, now).tombstones.isEmpty())
        }

        val root = temporaryFolder.newFolder("history-conflict-stored")
        val store = HistoryStore(root)
        val original = event(eventId = "stored", createdUtc = now, text = "original payload")
        store.upsert(HistoryLocalRecord(original))
        val conflicting = event(eventId = "stored", createdUtc = now, text = "changed payload")
        invalid {
            store.merge(peerId, listOf(event(eventId = "must-not-append", createdUtc = now), conflicting),
                listOf(HistoryTombstone("stored", peerId, now)), now)
        }
        assertEquals(listOf("stored"), store.getRecent(peerId, now).map { it.event.eventId })
        assertEquals("original payload", store.getRecent(peerId, now).single().event.text)
        assertTrue(store.getSyncSnapshot(peerId, now).tombstones.isEmpty())

        invalid { store.upsert(HistoryLocalRecord(conflicting)) }
        assertEquals("original payload", store.getRecent(peerId, now).single().event.text)
    }
    @Test fun failedCompactionPreservesMemoryAndDurableState() {
        val root = temporaryFolder.newFolder("history-failed-prune")
        val now = System.currentTimeMillis()
        HistoryStore(root).upsert(HistoryLocalRecord(event(eventId = "will-expire", createdUtc = now)))
        val failing = HistoryStore(root) { _, _ -> throw java.io.IOException("injected atomic replace failure") }

        try {
            failing.prune(now + HistoryPolicy.SEVEN_DAYS_MILLIS + 1)
            fail("Expected injected atomic replace failure")
        } catch (_: java.io.IOException) { }
        assertTrue(failing.getRecent(peerId, now).any { it.event.eventId == "will-expire" })
        assertTrue(HistoryStore(root).getRecent(peerId, now).any { it.event.eventId == "will-expire" })
    }
    @Test fun storeInstancesReloadBeforeCompaction() {
        val root = temporaryFolder.newFolder("history-instances")
        val now = System.currentTimeMillis()
        val first = HistoryStore(root)
        val second = HistoryStore(root)
        first.upsert(HistoryLocalRecord(event(eventId = "a-survivor", createdUtc = now)))
        first.upsert(HistoryLocalRecord(event(eventId = "expired", createdUtc = now - HistoryPolicy.SEVEN_DAYS_MILLIS - 1)))
        second.upsert(HistoryLocalRecord(event(eventId = "b-survivor", createdUtc = now)))

        first.prune(now)
        assertEquals(setOf("a-survivor", "b-survivor"), HistoryStore(root).getRecent(peerId, now).map { it.event.eventId }.toSet())
    }

    @Test fun storeReloadsPerPeerAndKeepsFilesWhenDeleting() {
        val root = temporaryFolder.newFolder("history-reload")
        val logPath = File(root, "history/events-v1.jsonl")
        val localFile = File(root, "received.txt").apply { writeText("keep these bytes") }
        val now = System.currentTimeMillis()
        val first = event(eventId = "first", createdUtc = now)
        val otherPeer = event(eventId = "other-peer", peerId = "peer-b", createdUtc = now)
        val references = mapOf(first.eventId to localFile.absolutePath)

        HistoryStore(root).apply {
            upsert(HistoryLocalRecord(first, references))
            upsert(HistoryLocalRecord(first, references))
            upsert(HistoryLocalRecord(otherPeer))
        }

        val reopened = HistoryStore(root)
        val recent = reopened.getRecent(peerId, now)
        assertEquals(1, recent.size)
        assertEquals("first", recent.single().event.eventId)
        assertEquals(localFile.absolutePath, recent.single().localFileReferences[first.eventId])
        assertEquals("other-peer", reopened.getRecent("peer-b", now).single().event.eventId)
        val beforeDeleteSnapshot = reopened.getSyncSnapshot(peerId, now)
        assertEquals(listOf("first"), beforeDeleteSnapshot.entries.map { it.eventId })
        assertFalse(beforeDeleteSnapshot.toString().contains(localFile.absolutePath))

        reopened.deleteRecord(peerId, first.eventId, now)
        assertTrue(reopened.getRecent(peerId, now).isEmpty())
        assertEquals("keep these bytes", localFile.readText())
        assertTrue(reopened.getSyncSnapshot(peerId, now).tombstones.any { it.eventId == first.eventId })
        reopened.upsert(HistoryLocalRecord(first, references))
        assertTrue(reopened.getRecent(peerId, now).isEmpty())

        val remoteDeleted = event(eventId = "remote-deleted", createdUtc = now)
        reopened.upsert(HistoryLocalRecord(remoteDeleted, mapOf(remoteDeleted.eventId to localFile.absolutePath)))
        reopened.merge(peerId, listOf(remoteDeleted), listOf(HistoryTombstone(remoteDeleted.eventId, peerId, now)), now)
        assertTrue(reopened.getRecent(peerId, now).isEmpty())
        assertEquals("keep these bytes", localFile.readText())

        reopened.upsert(HistoryLocalRecord(event(eventId = "expired-after-tombstones", createdUtc = now)))
        reopened.prune(now + HistoryPolicy.SEVEN_DAYS_MILLIS)

        val snapshot = HistoryStore(root).getSyncSnapshot(peerId, now)
        assertEquals(setOf("first", "remote-deleted"), snapshot.tombstones.map { it.eventId }.toSet())
        assertTrue("compaction retains tombstones", snapshot.tombstones.isNotEmpty())
        assertTrue(HistoryStore(root).getRecent(peerId, now).isEmpty())
    }

    @Test fun storeRecoversNoncanonicalTrailingJsonLine() {
        val root = temporaryFolder.newFolder("history-case-recovery")
        val now = System.currentTimeMillis()
        val logFile = File(File(root, "history"), "events-v1.jsonl")
        HistoryStore(root).upsert(HistoryLocalRecord(event(eventId = "canonical", createdUtc = now)))
        val canonicalBytes = logFile.readBytes()
        val noncanonicalLine = canonicalBytes.toString(StandardCharsets.UTF_8).trimEnd('\n').replace("\"schemaVersion\"", "\"SchemaVersion\"")
        logFile.appendText(noncanonicalLine, StandardCharsets.UTF_8)

        assertEquals("canonical", HistoryStore(root).getRecent(peerId, now).single().event.eventId)
        assertArrayEquals(canonicalBytes, logFile.readBytes())
    }
    @Test fun storePrunesExpiredRecordsAndRecoversTruncatedTail() {
        val root = temporaryFolder.newFolder("history-prune")
        val logPath = File(root, "history/events-v1.jsonl")
        val localFile = File(root, "received.bin").apply { writeText("keep after prune") }
        val now = System.currentTimeMillis()
        val expired = event(eventId = "expired", createdUtc = now - HistoryPolicy.SEVEN_DAYS_MILLIS)
        val visible = event(eventId = "visible", createdUtc = now - HistoryPolicy.SEVEN_DAYS_MILLIS + 1)
        val store = HistoryStore(root)
        store.upsert(HistoryLocalRecord(expired, mapOf(expired.eventId to localFile.absolutePath)))
        store.upsert(HistoryLocalRecord(visible, mapOf(visible.eventId to localFile.absolutePath)))
        store.prune(now)
        assertEquals(listOf("visible"), store.getRecent(peerId, now).map { it.event.eventId })
        assertEquals("keep after prune", localFile.readText())
        assertEquals("visible", HistoryStore(root).getRecent(peerId, now).single().event.eventId)

        val validLog = logPath.readBytes()
        logPath.appendBytes("{\"schemaVersion\":1,\"operation\":\"upsert\",\"record\":".toByteArray(StandardCharsets.UTF_8))
        val recovered = HistoryStore(root)
        assertEquals("visible", recovered.getRecent(peerId, now).single().event.eventId)
        assertArrayEquals(validLog, logPath.readBytes())
        recovered.upsert(HistoryLocalRecord(event(eventId = "after-recovery", createdUtc = now)))
        assertEquals(2, HistoryStore(root).getRecent(peerId, now).size)
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