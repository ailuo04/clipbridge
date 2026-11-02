package app.clipbridge.storage

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.provider.MediaStore
import app.clipbridge.protocol.BridgeProtocol
import app.clipbridge.protocol.Chunk
import app.clipbridge.protocol.FileProtocol
import app.clipbridge.protocol.Offer
import java.io.File
import java.io.RandomAccessFile
import java.security.MessageDigest
import java.util.UUID

class ReceivedFiles(private val context: Context, private val offer: Offer) : AutoCloseable {
    private val directory = File(context.cacheDir, "receive-" + UUID.randomUUID()).apply { check(mkdirs()) }
    private val written = LongArray(offer.files.size)
    private var current = 0
    init { offer.files.indices.forEach { check(File(directory, it.toString()).createNewFile()) } }
    fun append(chunk: Chunk) {
        while (current < offer.files.size && written[current] == offer.files[current].size) current++
        require(chunk.index == current && chunk.index in offer.files.indices && chunk.offset == written[chunk.index] && chunk.data.size.toLong() <= offer.files[chunk.index].size - chunk.offset) { "文件分块顺序或大小错误" }
        RandomAccessFile(File(directory, chunk.index.toString()), "rw").use { it.seek(chunk.offset); it.write(chunk.data) }
        written[chunk.index] += chunk.data.size
    }
    fun commit(checkActive: () -> Unit = {}): List<Uri> {
        offer.files.forEachIndexed { i, item ->
            require(written[i] == item.size) { "文件大小不匹配" }
            val digest = MessageDigest.getInstance("SHA-256")
            File(directory, i.toString()).inputStream().use { input -> val buffer = ByteArray(FileProtocol.CHUNK_SIZE); while (true) { checkActive(); val n = input.read(buffer); if (n < 0) break; digest.update(buffer, 0, n) } }
            require(BridgeProtocol.hex(digest.digest()).equals(item.sha256, true)) { "文件完整性校验失败" }
        }
        val inserted = mutableListOf<Uri>()
        try {
            offer.files.forEachIndexed { i, item ->
                val values = ContentValues().apply { put(MediaStore.Downloads.DISPLAY_NAME, item.name); put(MediaStore.Downloads.MIME_TYPE, "application/octet-stream"); put(MediaStore.Downloads.RELATIVE_PATH, "Download/ClipBridge"); put(MediaStore.Downloads.IS_PENDING, 1) }
                val uri = context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: error("无法创建接收文件")
                inserted.add(uri)
                val output = context.contentResolver.openOutputStream(uri, "w") ?: error("无法保存接收文件")
                output.use { dest -> File(directory, i.toString()).inputStream().use { source -> val buffer = ByteArray(FileProtocol.CHUNK_SIZE); while (true) { checkActive(); val n = source.read(buffer); if (n < 0) break; dest.write(buffer, 0, n) } } }
            }
            inserted.forEach { uri -> check(context.contentResolver.update(uri, ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) }, null, null) == 1) }
            return inserted
        } catch (e: Exception) { inserted.forEach { uri -> runCatching { context.contentResolver.delete(uri, null, null) } }; throw e }
    }
    override fun close() { directory.deleteRecursively() }
}
