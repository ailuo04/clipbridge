package app.clipbridge.protocol

import org.json.JSONObject
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.Inet4Address
import java.net.InetAddress
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

object BridgeProtocol {
    const val DISCOVERY_PORT = 45821
    const val TEXT_PORT = 45822
    const val MAX_TEXT = 262144
    const val MAX_FRAME = 1581056
    private val random = SecureRandom()
    fun deriveKey(pairing: ByteArray): ByteArray = derive(pairing, "ClipBridge/v1\u0000")
    fun derive(pairing: ByteArray, domain: String): ByteArray {
        require(pairing.size == 32) { "配对密钥必须为 64 位十六进制" }
        return MessageDigest.getInstance("SHA-256").digest(domain.toByteArray() + pairing)
    }
    fun encrypt(key: ByteArray, plain: ByteArray): ByteArray {
        val nonce = ByteArray(12).also(random::nextBytes)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce))
        return nonce + cipher.doFinal(plain)
    }
    fun decrypt(key: ByteArray, data: ByteArray): ByteArray {
        require(data.size in 28..MAX_FRAME) { "无效的加密帧长度" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, data.copyOfRange(0, 12)))
        return cipher.doFinal(data, 12, data.size - 12)
    }
    fun write(output: OutputStream, key: ByteArray, plain: ByteArray) {
        require(plain.size <= MAX_FRAME - 28)
        val encrypted = encrypt(key, plain)
        DataOutputStream(output).apply { writeInt(encrypted.size); write(encrypted); flush() }
    }
    fun writeJson(output: OutputStream, key: ByteArray, json: JSONObject) = write(output, key, json.toString().toByteArray())
    fun read(input: InputStream, key: ByteArray, maxPlain: Int = MAX_FRAME - 28): ByteArray {
        val stream = DataInputStream(input)
        val length = stream.readInt()
        require(length in 28..(maxPlain + 28)) { "消息长度超限" }
        return decrypt(key, ByteArray(length).also(stream::readFully))
    }
    fun fresh(time: Long): Boolean = time >= System.currentTimeMillis() - 120000 && time <= System.currentTimeMillis() + 120000
    fun isLocal(host: String): Boolean {
        val parts = host.split('.')
        if (parts.size != 4 || parts.any { it.isEmpty() || it.length > 3 || it.any { c -> c !in '0'..'9' } || it.toIntOrNull() !in 0..255 }) return false
        val p = parts.map(String::toInt)
        return p[0] == 10 || p[0] == 127 || p[0] == 192 && p[1] == 168 || p[0] == 172 && p[1] in 16..31 || p[0] == 169 && p[1] == 254
    }
    fun hex(bytes: ByteArray): String = bytes.joinToString("") { "%02x".format(it.toInt() and 255) }
    fun unhex(text: String): ByteArray {
        require(text.length % 2 == 0 && text.all { it in '0'..'9' || it.lowercaseChar() in 'a'..'f' })
        return text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    }
}
