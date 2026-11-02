package app.clipbridge.protocol
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
object TrustedChannel {
    val MAGIC = "CBV2".toByteArray(Charsets.US_ASCII)
    const val HEADER_SIZE = 65
    private fun routeKey(pairing: ByteArray) = PairingProtocol.hkdf(pairing,ByteArray(32),"ClipBridge/route/v2".toByteArray())
    fun header(source: String, target: String, kind: Int, pairing: ByteArray): ByteArray {
        require(PairingProtocol.validId(source) && PairingProtocol.validId(target) && kind in 1..2)
        val header = MAGIC + byteArrayOf(kind.toByte()) + BridgeProtocol.unhex(source) + BridgeProtocol.unhex(target)
        val nonce = ByteArray(12).also(SecureRandom()::nextBytes)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE,SecretKeySpec(routeKey(pairing),"AES"),GCMParameterSpec(128,nonce)); updateAAD(header) }
        return header + nonce + cipher.doFinal(ByteArray(0))
    }
    fun source(header: ByteArray) = BridgeProtocol.hex(header.copyOfRange(5,21))
    fun open(header: ByteArray, target: String, kind: Int, pairing: ByteArray): ByteArray {
        require(header.size == HEADER_SIZE && header.copyOfRange(0,4).contentEquals(MAGIC) && header[4].toInt() == kind && header.copyOfRange(21,37).contentEquals(BridgeProtocol.unhex(target))) { "会话目标不符" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE,SecretKeySpec(routeKey(pairing),"AES"),GCMParameterSpec(128,header.copyOfRange(37,49))); updateAAD(header.copyOfRange(0,37)) }
        cipher.doFinal(header.copyOfRange(49,65))
        return PairingProtocol.hkdf(pairing,header.copyOfRange(37,49),header.copyOfRange(0,37))
    }
}
