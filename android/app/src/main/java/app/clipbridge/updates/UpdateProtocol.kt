package app.clipbridge.updates

import org.json.JSONObject
import java.net.URI
import java.security.KeyFactory
import java.security.Signature
import java.security.spec.X509EncodedKeySpec
import java.util.Base64

data class AndroidUpdate(val version: String,val versionCode: Long,val packageName: String,val url: String,val size: Long,val sha256: String,val certificate: String,val notes: String)
object UpdateProtocol {
    const val RAW="https://raw.githubusercontent.com/ailuo04/clipbridge/master/latest-v2.json"
    const val API="https://api.github.com/repos/ailuo04/clipbridge/contents/latest-v2.json?ref=master"
    const val CERTIFICATE="ce45d562d468c2bd9d5426fd0efdaae2b273ec87c48a55d68d340b682309fc17"
    const val MAX_MANIFEST=1048576
    const val MAX_PACKAGE=209715200L
    const val PUBLIC_KEY="MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA7ZiIKz0PFtIOdcHzdkBkX/GWuCKOua/duJlyZsQRXGIFbq0eKvuZBT9ybNRnaaEX8gmpFcIckMHZLJhlgPV+LJbtaogtHgG/BQz6Ax/OGGAli96Cg37ArKRctrrvIuVTiYJe+omcRCOqvdDHFdiL+oPGIHeG008AoVzz+5cSAtzcIfmfSQLe4J2PztvUM1Ju2s2ufKfyeR7dgP8xxG/szqaJDy5sYhCbQsawCep8fQI5XPQZsjHbS1bLP+BwkpKy3t0+AA1PS2KKXFCEKguKttxhTvaT+AEVoX1iXrKtLJ4mBuHZUjHjbp36QVYgf4rujgz7XHcT/12f2MYsf5XF1icW84+VbrlqRnnn4ikg9Cj8CeHynC7SmjIZVsGT03Rn6PJBMRbYAe5IwMKtZOFw3gQ/ZY+caTbfHtoCxvlzgu8T1SvalOsoR3oJPA2tNSWvtVe3G7GqtNxC/JUyZzXLQ+i4h9VdGW1+BaSnxS99WnNE6XXzCArcd0FQd7ZXlF0VAgMBAAE="
    fun trustedUrl(value: String): Boolean = runCatching {
        val uri=URI(value)
        uri.scheme=="https" && uri.rawUserInfo==null && uri.fragment==null && (uri.port == -1 || uri.port == 443) && uri.host in setOf("raw.githubusercontent.com","api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com")
    }.getOrDefault(false)
    fun verifyPayload(file: java.io.File,update: AndroidUpdate) {
        require(file.length()==update.size) { "安装包长度校验失败" }
        val digest=java.security.MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input -> val buffer=ByteArray(65536); while(true) { val n=input.read(buffer); if(n<0) break; digest.update(buffer,0,n) } }
        require(app.clipbridge.protocol.BridgeProtocol.hex(digest.digest())==update.sha256) { "安装包哈希校验失败" }
    }
    fun verifyIdentity(packageName: String,code: Long,currentCode: Long,installedPins: List<String>,archivePins: List<String>,update: AndroidUpdate) {
        require(packageName=="app.clipbridge" && code==update.versionCode && code>currentCode) { "APK 包名或版本不符" }
        require(archivePins==listOf(update.certificate) && installedPins.sorted()==archivePins.sorted()) { "APK 签名身份不符" }
    }
    fun parse(envelope: String,currentCode: Long,publicKey: String=PUBLIC_KEY): AndroidUpdate? {
        require(envelope.toByteArray().size<=MAX_MANIFEST) { "更新清单过大" }
        val root=JSONObject(envelope); val payload=Base64.getDecoder().decode(root.getString("payload")); val signature=Base64.getDecoder().decode(root.getString("signature"))
        require(payload.size<=MAX_MANIFEST)
        val rsa=KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(Base64.getDecoder().decode(publicKey)))
        require(Signature.getInstance("SHA256withRSA").apply { initVerify(rsa); update(payload) }.verify(signature)) { "更新签名无效" }
        val j=JSONObject(payload.toString(Charsets.UTF_8)); require(j.getInt("schema")==1)
        if(!j.has("android") || j.isNull("android")) return null
        val a=j.getJSONObject("android")
        val version=a.getString("version"); val code=a.getLong("versionCode"); val packageName=a.getString("packageName"); val url=a.getString("url"); val size=a.getLong("size"); val hash=a.getString("sha256").lowercase(); val cert=a.getString("certificateSha256").lowercase()
        require(version.matches(Regex("[0-9]+\\.[0-9]+\\.[0-9]+")) && code in 1..Int.MAX_VALUE.toLong() && packageName=="app.clipbridge" && trustedUrl(url) && size in 1..MAX_PACKAGE && hash.matches(Regex("[0-9a-f]{64}")) && cert==CERTIFICATE) { "更新包信息无效" }
        return if(code<=currentCode) null else AndroidUpdate(version,code,packageName,url,size,hash,cert,j.optJSONObject("windows")?.optString("notes","")?.take(4000) ?: "")
    }
}
