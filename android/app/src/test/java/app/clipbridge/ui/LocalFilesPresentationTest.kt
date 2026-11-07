package app.clipbridge.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class LocalFilesPresentationTest {
    @Test fun downloadsHandlerIsPreferredBeforeMediaStoreFallback() {
        assertEquals("system-downloads", UiPresentation.localFilesFallback(true))
        assertEquals("clipbridge-mediastore", UiPresentation.localFilesFallback(false))
    }
}
