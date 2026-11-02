package app.clipbridge.protocol
import app.clipbridge.compat.PlatformCompat
import org.junit.Test
import org.junit.Assert.*
class CompatibilityTest {
    @Test fun lanPermissionFollowsDeviceAndTargetVersionTogether() {
        assertFalse(PlatformCompat.needsLanPermission(36,36))
        assertFalse(PlatformCompat.needsLanPermission(37,36))
        assertFalse(PlatformCompat.needsLanPermission(36,37))
        assertTrue(PlatformCompat.needsLanPermission(37,37))
        assertTrue(PlatformCompat.needsLanPermission(38,38))
    }
}
