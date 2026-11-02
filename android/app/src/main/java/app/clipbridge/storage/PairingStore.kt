package app.clipbridge.storage

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import app.clipbridge.protocol.BridgeProtocol
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class PairingStore(context: Context) {
    private val prefs = context.getSharedPreferences("pairing", Context.MODE_PRIVATE)
    private fun wrappingKey(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("clipbridge-pairing", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("clipbridge-pairing", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun load(): ByteArray? {
        val raw = prefs.getString("key", null) ?: return null
        return try {
            val encrypted = Base64.decode(raw, Base64.NO_WRAP)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, wrappingKey(), GCMParameterSpec(128, encrypted.copyOfRange(0, 12)))
            cipher.doFinal(encrypted, 12, encrypted.size - 12).also { require(it.size == 32) }
        } catch (_: Exception) { null }
    }
    fun save(text: String, peer: String) {
        val value = text.trim(); require(value.length == 64) { "请输入电脑显示的 64 位配对密钥" }
        val key = BridgeProtocol.unhex(value)
        require(peer.isBlank() || BridgeProtocol.isLocal(peer.trim())) { "请输入局域网 IPv4 地址" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE, wrappingKey())
        val encrypted = cipher.iv + cipher.doFinal(key)
        check(prefs.edit().putString("key", Base64.encodeToString(encrypted, Base64.NO_WRAP)).putString("peer", peer.trim()).commit()) { "配对信息保存失败" }
    }
    fun peer(): String = prefs.getString("peer", "") ?: ""
    fun deviceId(): String {
        prefs.getString("deviceId", null)?.let { return it }
        return UUID.randomUUID().toString().replace("-", "").also { check(prefs.edit().putString("deviceId", it).commit()) }
    }
    fun clear() { prefs.edit().remove("key").remove("peer").apply() }
}
