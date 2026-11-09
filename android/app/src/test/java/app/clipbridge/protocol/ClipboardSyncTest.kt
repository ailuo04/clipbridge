package app.clipbridge.protocol

import org.junit.Assert.*
import org.junit.Test

class ClipboardSyncTest {
    @Test fun explicitSessionSuppressesEchoAndStopsWhenDisabled() {
        val state=ClipboardSyncState()
        assertFalse(state.shouldSend("local"))
        state.enable("peer-a")
        assertTrue(state.shouldSend("local"))
        assertFalse(state.shouldSend("local"))
        state.suppressIncoming("remote")
        assertTrue(state.isSuppressed("remote"))
        assertTrue(state.isSuppressed("remote"))
        assertFalse(state.isSuppressed("different"))
        assertTrue(state.shouldSend("other"))
        assertTrue(state.shouldSend("local"))
        assertFalse(state.shouldSend(""))
        assertFalse(state.shouldSend("界".repeat(100000)))
        state.disable()
        assertNull(state.targetPeerId)
        assertFalse(state.shouldSend("next"))
        state.enable("peer-b")
        assertTrue(state.shouldSend("remote"))
    }
    @Test fun requestAndResponseMatchTheWindowsWireContract() {
        val now=System.currentTimeMillis()
        val request=ClipboardSyncProtocol.request("req-1","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",now)
        assertEquals("clipboardRequest",request.getString("Type"))
        assertEquals("req-1",request.getString("MessageId"))
        val responder="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        val response=ClipboardSyncProtocol.response("req-1",responder,ClipboardReadResult("ok","中文🙂"),now)
        val parsed=ClipboardSyncProtocol.readResponse(response,"req-1",responder)
        assertEquals("中文🙂",parsed.text)
        assertEquals("req-1",parsed.messageId)
        assertEquals(now,parsed.timestampUtc)
        assertEquals("req-1",ClipboardSyncProtocol.readRequest(request,"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",responder))
        assertFalse(ClipboardSyncProtocol.response("req-1",responder,ClipboardReadResult("foreground"),now).has("Text") && ClipboardSyncProtocol.response("req-1",responder,ClipboardReadResult("foreground"),now).getString("Text").isNotEmpty())
        assertThrows(IllegalArgumentException::class.java) { ClipboardSyncProtocol.readResponse(response,"different","$responder") }
    }
}
