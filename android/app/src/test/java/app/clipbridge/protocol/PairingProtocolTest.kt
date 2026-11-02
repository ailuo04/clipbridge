package app.clipbridge.protocol
import org.junit.Test
import org.junit.Assert.*
class PairingProtocolTest {
    @Test fun commitmentAndEcdhAgreeAcrossBothRoles() {
        PairingMaterial().use { a -> PairingMaterial().use { b ->
            val i="1".repeat(32); val r="2".repeat(32); val session="3".repeat(32)
            val t=PairingProtocol.transcript(session,i,"手机",a.publicKey,a.nonce,r,"电脑",b.publicKey,b.nonce)
            val ak=PairingProtocol.sessionKey(a,b.publicKey,t); val bk=PairingProtocol.sessionKey(b,a.publicKey,t)
            assertArrayEquals(ak,bk); assertEquals(PairingProtocol.code(ak,t),PairingProtocol.code(bk,t))
            val expected=PairingProtocol.commitment(session,"i",i,"手机",a.publicKey,a.nonce)
            assertThrows(IllegalArgumentException::class.java) { PairingProtocol.verify(expected,"0".repeat(64)) }
        } }
    }
    @Test fun routingAndPresenceDoNotTrustPublicDiscovery() {
        val a="1".repeat(32); val b="2".repeat(32); val key=ByteArray(32) { it.toByte() }
        val h=TrustedChannel.header(a,b,1,key); assertEquals(32,TrustedChannel.open(h,b,1,key).size)
        assertThrows(IllegalArgumentException::class.java) { TrustedChannel.open(h,a,1,key) }
        h[5]=(h[5].toInt() xor 1).toByte()
        assertThrows(Exception::class.java) { TrustedChannel.open(h,b,1,key) }
        val public=DiscoveryProtocol.create(a,"手机","android")
        assertEquals(a,DiscoveryProtocol.parse(DiscoveryProtocol.encode(public)).id)
        assertFalse(DiscoveryProtocol.authenticate(public,b,key))
        val p=DiscoveryProtocol.create(a,"手机","android",b,key)
        assertTrue(DiscoveryProtocol.authenticate(DiscoveryProtocol.parse(DiscoveryProtocol.encode(p)),b,key))
        assertFalse(DiscoveryProtocol.authenticate(p.copy(name="forged"),b,key))
    }
    @Test fun duplicateSessionAndStaleUiCannotApproveAnotherRequest() {
        val sessions=PairingSessions<Any>(); val id="d".repeat(32)
        val owner=Any(); val attacker=Any(); val original=Any(); val replacement=Any()
        assertTrue(sessions.reserve(id,owner)); sessions.publish(id,owner,original)
        assertFalse(sessions.reserve(id,attacker))
        sessions.release(id,attacker,replacement); assertTrue(sessions.isCurrent(id,original))
        assertThrows(IllegalStateException::class.java) { sessions.publish(id,owner,replacement) }
        sessions.release(id,owner,original); assertTrue(sessions.reserve(id,attacker)); sessions.publish(id,attacker,replacement)
        assertFalse(sessions.isCurrent(id,original)); assertTrue(sessions.isCurrent(id,replacement))
        sessions.release(id,owner,original); assertTrue(sessions.isCurrent(id,replacement))
    }
}
