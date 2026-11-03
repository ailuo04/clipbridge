package app.clipbridge.protocol

import org.junit.Assert.*
import org.junit.Test

class PairingInvitationTest {
    private val id = "0123456789abcdef0123456789abcdef"
    @Test fun validInvitationPinsDeviceAndLocalAddress() {
        val invitation=PairingInvitationCodec.parse("clipbridge:pair:1:$id:10.51.2.197")
        assertEquals(id,invitation.deviceId)
        assertEquals("10.51.2.197",invitation.address)
    }
    @Test fun invalidOrUnsafeInvitationsAreRejected() {
        val values = listOf("", "x".repeat(200), "clipbridge:pair:2:$id:10.0.0.1",
            "clipbridge:pair:1:${id.uppercase()}:10.0.0.1", "clipbridge:pair:1:bad:10.0.0.1",
            "clipbridge:pair:1:$id:127.0.0.1", "clipbridge:pair:1:$id:8.8.8.8",
            "clipbridge:pair:1:$id:224.0.0.1", "clipbridge:pair:1:$id:10.0.0.256",
            "clipbridge:pair:1:$id:010.0.0.1", "clipbridge:pair:1:$id:host.local",
            "clipbridge:pair:1:$id:10.0.0.1:45824")
        values.forEach { value ->
            try { PairingInvitationCodec.parse(value); fail("unexpectedly accepted: $value") }
            catch (_: IllegalArgumentException) { }
        }
    }
}