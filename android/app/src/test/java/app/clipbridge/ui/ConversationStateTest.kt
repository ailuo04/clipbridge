package app.clipbridge.ui

import org.junit.Assert.*
import org.junit.Test

class ConversationStateTest {
    @Test fun completionBelongsToOriginalPeer() {
        val state=ConversationState<String>();state.texts["a"]="A text";state.texts["b"]="B draft";state.attachments["a"]=mutableListOf("original.png")
        assertTrue(state.begin("a"));assertFalse(state.begin("a"));assertTrue(state.begin("b"))
        state.textSent("a","A text");state.complete("a",listOf("original.png"),true)
        assertEquals("",state.texts["a"]);assertEquals("B draft",state.texts["b"]);assertTrue(state.isSending("b"));assertFalse(state.isSending("a"));assertTrue(state.attachments["a"]!!.isEmpty())
    }
    @Test fun failureRetainsOnlyUnsentContents() {
        val state=ConversationState<String>();state.texts["a"]="sent";state.attachments["a"]=mutableListOf("folder");state.begin("a");state.textSent("a","sent");state.complete("a",listOf("folder"),false)
        assertEquals("",state.texts["a"]);assertEquals(listOf("folder"),state.attachments["a"]);assertFalse(state.isSending("a"))
    }
}
