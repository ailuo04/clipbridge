package app.clipbridge.protocol

import app.clipbridge.network.DiscoveryWake
import org.junit.Assert.*
import org.junit.Test

class DiscoveryWakeTest {
    @Test fun refreshRequestsAreImmediateAndCoalesced() {
        val wake=DiscoveryWake()
        repeat(5) { wake.signal() }
        assertTrue(wake.await(50))
        assertFalse(wake.await(20))
        val thread=Thread { Thread.sleep(20); wake.signal() }; thread.start()
        assertTrue(wake.await(1000)); thread.join()
    }
}