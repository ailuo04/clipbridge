package app.clipbridge.protocol
import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.security.*
import java.security.spec.*
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import java.nio.ByteBuffer
import java.util.Base64
class PairingMaterial : AutoCloseable {
    val key: KeyPair = KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }.generateKeyPair()
    val publicKey: String get() = Base64.getEncoder().encodeToString(key.public.encoded)
    val nonce: String = BridgeProtocol.hex(ByteArray(32).also(SecureRandom()::nextBytes))
    override fun close() { runCatching { key.private.destroy() } }
}
object PairingProtocol {
    const val PORT = 45824
    fun validId(id: String) = id.length == 32 && id.all { it in '0'..'9' || it in 'a'..'f' }
    fun fields(vararg values: String): ByteArray = ByteArrayOutputStream().also { output -> DataOutputStream(output).use { data -> values.forEach { val bytes = it.toByteArray(Charsets.UTF_8); data.writeInt(bytes.size); data.write(bytes) } } }.toByteArray()
    fun hmac(key: ByteArray, data: ByteArray): ByteArray = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(key, "HmacSHA256")) }.doFinal(data)
    fun hkdf(secret: ByteArray, salt: ByteArray, info: ByteArray) = hmac(hmac(salt, secret), info + byteArrayOf(1))
    fun commitment(session: String, role: String, id: String, name: String, publicKey: String, nonce: String): String {
        require(validId(session) && validId(id) && name.length in 1..80 && name.none { it.isISOControl() } && nonce.length == 64 && role in listOf("i","r"))
        return BridgeProtocol.hex(MessageDigest.getInstance("SHA-256").digest(fields("ClipBridge/pair/v2",session,role,id,name,publicKey,nonce)))
    }
    fun verify(expected: String, actual: String) { require(expected.length == 64 && actual.length == 64 && MessageDigest.isEqual(expected.toByteArray(),actual.toByteArray())) { "配对承诺校验失败" } }
    fun transcript(session: String, i: String, iName: String, iPublic: String, iNonce: String, r: String, rName: String, rPublic: String, rNonce: String) = fields("ClipBridge/pair/transcript/v2",session,i,iName,iPublic,iNonce,r,rName,rPublic,rNonce)
    fun sessionKey(local: PairingMaterial, remotePublic: String, transcript: ByteArray): ByteArray {
        val encoded = Base64.getDecoder().decode(remotePublic); require(encoded.size <= 160)
        val remote = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(encoded)) as java.security.interfaces.ECPublicKey
        val p = (local.key.public as java.security.interfaces.ECPublicKey).params
        require(remote.params.curve == p.curve && remote.params.generator == p.generator && remote.params.order == p.order)
        val secret = KeyAgreement.getInstance("ECDH").apply { init(local.key.private); doPhase(remote,true) }.generateSecret()
        return try { hkdf(secret,MessageDigest.getInstance("SHA-256").digest(transcript),"ClipBridge/pair/session/v2".toByteArray()) } finally { secret.fill(0) }
    }
    fun code(key: ByteArray, transcript: ByteArray) = ((ByteBuffer.wrap(hmac(key,transcript)).int.toLong() and 0xffffffffL) % 1000000).toString().padStart(6,'0')
}
