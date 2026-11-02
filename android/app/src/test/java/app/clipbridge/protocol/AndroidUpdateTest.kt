package app.clipbridge.protocol
import app.clipbridge.updates.UpdateProtocol
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.security.*
import java.util.Base64
class AndroidUpdateTest {
    private val keys=KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair()
    private val publicKey=Base64.getEncoder().encodeToString(keys.public.encoded)
    private fun metadata()=JSONObject().put("version","0.5.1").put("versionCode",50100).put("packageName","app.clipbridge").put("url","https://github.com/ailuo04/clipbridge/releases/download/v0.5.1/ClipBridge-0.5.1-android.apk").put("size",12000).put("sha256","a".repeat(64)).put("certificateSha256",UpdateProtocol.CERTIFICATE)
    private fun envelope(a: JSONObject): String {
        val data=JSONObject().put("schema",1).put("android",a).toString().toByteArray()
        val signature=Signature.getInstance("SHA256withRSA").apply { initSign(keys.private); update(data) }.sign()
        return JSONObject().put("payload",Base64.getEncoder().encodeToString(data)).put("signature",Base64.getEncoder().encodeToString(signature)).toString()
    }
    private fun rejects(block: ()->Unit) { try { block(); fail("invalid update accepted") } catch(_: Exception) { } }
    @Test fun signedNewerUpdateAcceptedAndInstalledVersionNotReinstalled() {
        val text=envelope(metadata()); assertEquals(50100L,UpdateProtocol.parse(text,50000,publicKey)!!.versionCode)
        assertNull(UpdateProtocol.parse(text,50100,publicKey))
    }
    @Test fun tamperedSignatureRejected() {
        val j=JSONObject(envelope(metadata())); j.put("payload",Base64.getEncoder().encodeToString("{}".toByteArray()))
        rejects { UpdateProtocol.parse(j.toString(),50000,publicKey) }
    }
    @Test fun unsafeMetadataRejectedEvenWhenSigned() {
        listOf("packageName" to "other.app","url" to "http://github.com/x","url" to "https://github.com.evil.test/x","sha256" to "broken","certificateSha256" to "0".repeat(64)).forEach { (field,value) -> rejects { UpdateProtocol.parse(envelope(metadata().put(field,value)),50000,publicKey) } }
        rejects { UpdateProtocol.parse(envelope(metadata().put("size",UpdateProtocol.MAX_PACKAGE+1)),50000,publicKey) }
    }
    @Test fun httpsRedirectsStayOnExpectedHosts() {
        assertTrue(UpdateProtocol.trustedUrl("https://release-assets.githubusercontent.com/a"))
        assertFalse(UpdateProtocol.trustedUrl("https://attacker@github.com/a"))
        assertFalse(UpdateProtocol.trustedUrl("https://github.com:444/a"))
        assertFalse(UpdateProtocol.trustedUrl("file:///sdcard/update.apk"))
    }
    @Test fun corruptedPayloadAndDifferentSignerCannotInstall() {
        val bytes="synthetic apk".toByteArray(); val file=java.io.File.createTempFile("clipbridge-update-test", ".apk")
        val hash=BridgeProtocol.hex(MessageDigest.getInstance("SHA-256").digest(bytes))
        val update=UpdateProtocol.parse(envelope(metadata().put("size",bytes.size).put("sha256",hash)),50000,publicKey)!!
        try {
            file.writeBytes(bytes); UpdateProtocol.verifyPayload(file,update)
            file.writeBytes(bytes+byteArrayOf(1)); rejects { UpdateProtocol.verifyPayload(file,update) }
            file.writeBytes(ByteArray(bytes.size)); rejects { UpdateProtocol.verifyPayload(file,update) }
            val pin=UpdateProtocol.CERTIFICATE
            UpdateProtocol.verifyIdentity("app.clipbridge",50100,50000,listOf(pin),listOf(pin),update)
            rejects { UpdateProtocol.verifyIdentity("app.clipbridge",50100,50000,listOf(pin),listOf("0".repeat(64)),update) }
            rejects { UpdateProtocol.verifyIdentity("other.app",50100,50000,listOf(pin),listOf(pin),update) }
            rejects { UpdateProtocol.verifyIdentity("app.clipbridge",50000,50000,listOf(pin),listOf(pin),update) }
        } finally { file.delete() }
    }
}
