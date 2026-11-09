package app.clipbridge.protocol

import org.json.JSONArray
import org.json.JSONObject
import java.nio.ByteBuffer
import java.util.Locale

data class FileItem @JvmOverloads constructor(val name: String, val size: Long, val sha256: String, val relativePath: String? = null, val isDirectory: Boolean = false) {
    fun json(): JSONObject = JSONObject().put("Name", name).put("Size", size).put("Sha256", sha256).apply { relativePath?.let { put("RelativePath",it) }; if(isDirectory) put("IsDirectory",true) }
}
data class Offer(val id: String, val origin: String, val timestamp: Long, val files: List<FileItem>) {
    val total: Long get() = files.sumOf { it.size }
    fun json(): JSONObject = JSONObject().put("Type", if(files.any { it.relativePath!=null }) "fileTreeOffer" else "fileOffer").put("TransferId", id).put("OriginDeviceId", origin).put("TimestampUtc", timestamp).put("Files", JSONArray().apply { files.forEach { put(it.json()) } })
}
data class Chunk(val index: Int, val offset: Long, val data: ByteArray)

object FileProtocol {
    const val PORT = 45823
    const val CHUNK_SIZE = 65536
    const val MAX_CONTROL = 262144
    const val MAX_TOTAL = 1099511627776L
    fun deriveKey(pairing: ByteArray) = BridgeProtocol.derive(pairing, "ClipBridge/files/v1\u0000")
    fun validateName(name: String) {
        require(name.isNotBlank() && name != "." && name != ".." && !name.endsWith('.') && !name.endsWith(' ') && name.toByteArray().size <= 220 && name.none { it.code < 32 || it in "<>:\"/\\|?*" }) { "不安全的文件名" }
        val stem = name.substringBefore('.').uppercase(Locale.ROOT)
        require(stem !in setOf("CON", "PRN", "AUX", "NUL", "CLOCK$") && !stem.matches(Regex("(COM|LPT)[0-9]"))) { "不支持的文件名" }
    }
    fun parseOffer(json: JSONObject): Offer {
        val tree=json.getString("Type")=="fileTreeOffer"
        require(tree || json.getString("Type")=="fileOffer")
        val list = json.getJSONArray("Files")
        require(list.length() in 1..256) { "文件数量超限" }
        val files = (0 until list.length()).map { val f = list.getJSONObject(it); FileItem(f.getString("Name"), f.getLong("Size"), f.getString("Sha256"),if(f.has("RelativePath")) f.getString("RelativePath") else null,f.optBoolean("IsDirectory",false)) }
        require(tree || files.none { it.relativePath!=null || it.isDirectory })
        require(!tree || files.all { it.relativePath!=null })
        return Offer(json.getString("TransferId"), json.getString("OriginDeviceId"), json.getLong("TimestampUtc"), files).also(::validateOffer)
    }
    fun validateOffer(offer: Offer) {
        require(offer.id.matches(Regex("[0-9a-fA-F]{32}")) && offer.origin.length in 1..64 && BridgeProtocol.fresh(offer.timestamp) && offer.files.size in 1..256) { "无效文件请求" }
        val tree=offer.files.any { it.relativePath!=null || it.isDirectory }
        val paths=mutableMapOf<String,FileItem>()
        var total = 0L
        offer.files.forEach {
            validateName(it.name)
            if(tree) { val path=requireNotNull(it.relativePath); validateRelativePath(path);require(path.substringAfterLast('/')==it.name);require(paths.put(path.lowercase(Locale.ROOT),it)==null) { "目录条目重复" };require(!it.isDirectory || (it.size==0L && it.sha256.equals(EMPTY_HASH,true))) }
            require(it.size in 0..MAX_TOTAL && it.sha256.matches(Regex("[0-9a-fA-F]{64}")))
            total = Math.addExact(total, it.size)
            require(total <= MAX_TOTAL) { "文件总大小超过限制" }
        }
        if(tree) offer.files.forEach { val path=it.relativePath!!; if(path.contains('/')) require(paths[path.substringBeforeLast('/').lowercase(Locale.ROOT)]?.isDirectory==true) { "目录父级不存在" } }
    }
    const val EMPTY_HASH="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    fun validateRelativePath(path:String) { require(path.toByteArray().size<=4096 && path.split('/').size<=64); path.split('/').forEach(::validateName) }
    fun reply(type: String, id: String): JSONObject = JSONObject().put("Type", type).put("TransferId", id)
    fun requireReply(json: JSONObject, id: String, type: String) {
        require(json.getString("TransferId") == id && json.getString("Type") == type) { if (json.optString("Type") == "reject") "对方拒绝或未及时接受" else "传输未得到确认" }
    }
    fun chunk(id: String, index: Int, offset: Long, data: ByteArray): ByteArray {
        require(data.size in 1..CHUNK_SIZE)
        return ByteBuffer.allocate(29 + data.size).put(1).put(BridgeProtocol.unhex(id)).putInt(index).putLong(offset).put(data).array()
    }
    fun parseChunk(id: String, data: ByteArray): Chunk {
        require(data.size in 30..CHUNK_SIZE + 29)
        val b = ByteBuffer.wrap(data); require(b.get().toInt() == 1)
        val identity = ByteArray(16).also(b::get); require(identity.contentEquals(BridgeProtocol.unhex(id)))
        return Chunk(b.int, b.long, ByteArray(b.remaining()).also(b::get))
    }
}
