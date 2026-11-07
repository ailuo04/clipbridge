package app.clipbridge.protocol
import app.clipbridge.compat.PlatformCompat
import org.json.JSONObject
import org.junit.Test
import org.junit.Assert.*
class CompatibilityTest {
    @Test fun lanPermissionIsCheckedOnAndroid17EvenForOlderTargets() {
        assertFalse(PlatformCompat.needsLanPermission(36,36))
        assertTrue(PlatformCompat.needsLanPermission(37,36))
        assertFalse(PlatformCompat.needsLanPermission(36,37))
        assertTrue(PlatformCompat.needsLanPermission(37,37))
        assertTrue(PlatformCompat.needsLanPermission(38,38))
    }
    @Test fun historyCapabilityIsOptionalForOldDiscoveryPacketsAndVersionedForNewOnes() {
        val id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        val old = JSONObject().put("Type", "discover").put("Id", id).put("Name", "Old peer")
            .put("Platform", "windows").put("Port", PairingProtocol.PORT).put("Time", System.currentTimeMillis())
            .put("Nonce", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").put("Target", "").put("Mac", "")
        val oldPacket = DiscoveryProtocol.parse("CBD2".toByteArray() + old.toString().toByteArray())
        assertEquals(0, oldPacket.historySyncVersion)

        val current = DiscoveryProtocol.create(id, "Current peer", "android")
        assertEquals(DiscoveryProtocol.SUPPORTED_HISTORY_SYNC_VERSION, current.historySyncVersion)
        val encoded = DiscoveryProtocol.encode(current)
        assertEquals(DiscoveryProtocol.SUPPORTED_HISTORY_SYNC_VERSION, DiscoveryProtocol.parse(encoded).historySyncVersion)
        assertEquals("discover", JSONObject(encoded.copyOfRange(4, encoded.size).toString(Charsets.UTF_8)).getString("Type"))
    }
}
