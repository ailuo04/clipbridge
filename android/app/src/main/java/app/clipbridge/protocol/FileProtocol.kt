package app.clipbridge.protocol

import org.json.JSONArray
import org.json.JSONObject
import java.nio.ByteBuffer
import java.util.Locale

data class FileItem(val name: String, val size: Long, val sha256: String) {
    fun json(): JSONObject = JSONObject().put("Name", name).put("Size", size).put("Sha256", sha256)
}
data class Offer(val id: String, val origin: String, val timestamp: Long, val files: List<FileItem>) {
    val total: Long get() = files.sumOf { it.size }
    fun json(): JSONObject = JSONObject().put("Type", "fileOffer").put("TransferId", id).put("OriginDeviceId", origin).put("TimestampUtc", timestamp).put("Files", JSONArray().apply { files.forEach { put(it.json()) } })
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
        require(json.getString("Type") == "fileOffer")
        val list = json.getJSONArray("Files")
        require(list.length() in 1..256) { "文件数量超限" }
        val files = (0 until list.length()).map { val f = list.getJSONObject(it); FileItem(f.getString("Name"), f.getLong("Size"), f.getString("Sha256")) }
        return Offer(json.getString("TransferId"), json.getString("OriginDeviceId"), json.getLong("TimestampUtc"), files).also(::validateOffer)
    }
    fun validateOffer(offer: Offer) {
        require(offer.id.matches(Regex("[0-9a-fA-F]{32}")) && offer.origin.length in 1..64 && BridgeProtocol.fresh(offer.timestamp) && offer.files.size in 1..256) { "无效文件请求" }
        var total = 0L
        offer.files.forEach {
            validateName(it.name)
            require(it.size in 0..MAX_TOTAL && it.sha256.matches(Regex("[0-9a-fA-F]{64}")))
            total = Math.addExact(total, it.size)
            require(total <= MAX_TOTAL) { "文件总大小超过限制" }
        }
    }
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
