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
import java.security.MessageDigest
import java.util.UUID

class ReceivedFiles(private val context: Context, private val offer: Offer) : AutoCloseable {
    private val directory = File(context.cacheDir, "receive-" + UUID.randomUUID()).apply { check(mkdirs()) }
    private val written = LongArray(offer.files.size)
    private var current = 0
    private var output: java.io.BufferedOutputStream? = null
    private var outputIndex = -1
    init { offer.files.indices.forEach { check(File(directory, it.toString()).createNewFile()) } }
    fun append(chunk: Chunk) {
        while (current < offer.files.size && written[current] == offer.files[current].size) current++
        require(chunk.index == current && chunk.index in offer.files.indices && chunk.offset == written[chunk.index] && chunk.data.size.toLong() <= offer.files[chunk.index].size - chunk.offset) { "文件分块顺序或大小错误" }
        if(outputIndex!=chunk.index) {
            output?.close()
            output=File(directory,chunk.index.toString()).outputStream().buffered(256*1024)
            outputIndex=chunk.index
        }
        output!!.write(chunk.data)
        written[chunk.index] += chunk.data.size
    }
    fun commit(checkActive: () -> Unit = {}): List<Uri> {
        output?.close(); output=null; outputIndex=-1
        offer.files.forEachIndexed { i, item ->
            require(written[i] == item.size) { "文件大小不匹配" }
            val digest = MessageDigest.getInstance("SHA-256")
            File(directory, i.toString()).inputStream().use { input -> val buffer = ByteArray(FileProtocol.CHUNK_SIZE); while (true) { checkActive(); val n = input.read(buffer); if (n < 0) break; digest.update(buffer, 0, n) } }
            require(BridgeProtocol.hex(digest.digest()).equals(item.sha256, true)) { "文件完整性校验失败" }
        }
        if(offer.files.any { it.relativePath!=null }) return commitTree(checkActive)
        val inserted = mutableListOf<Uri>()
        try {
            offer.files.forEachIndexed { i, item ->
                val values = ContentValues().apply { put(MediaStore.Downloads.DISPLAY_NAME, item.name); put(MediaStore.Downloads.MIME_TYPE, android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(item.name.substringAfterLast('.', "").lowercase(java.util.Locale.ROOT)) ?: "application/octet-stream"); put(MediaStore.Downloads.RELATIVE_PATH, "Download/ClipBridge"); put(MediaStore.Downloads.IS_PENDING, 1) }
                val uri = context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: error("无法创建接收文件")
                inserted.add(uri)
                val output = context.contentResolver.openOutputStream(uri, "w") ?: error("无法保存接收文件")
                output.use { dest -> File(directory, i.toString()).inputStream().use { source -> val buffer = ByteArray(FileProtocol.CHUNK_SIZE); while (true) { checkActive(); val n = source.read(buffer); if (n < 0) break; dest.write(buffer, 0, n) } } }
            }
            inserted.forEach { uri -> check(context.contentResolver.update(uri, ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) }, null, null) == 1) }
            return inserted
        } catch (e: Exception) { inserted.forEach { uri -> runCatching { context.contentResolver.delete(uri, null, null) } }; throw e }
    }
    private fun commitTree(checkActive:()->Unit):List<Uri> {
        val destination=ReceivedFileProvider.root(context)
        val stage=File(destination,".pending-"+UUID.randomUUID()).apply { check(mkdirs()) }
        val committed=mutableListOf<File>()
        try {
            offer.files.filter { it.isDirectory }.sortedBy { it.relativePath!!.count { c->c=='/' } }.forEach { check(File(stage,it.relativePath!!).mkdirs()) }
            offer.files.forEachIndexed { i,item->if(!item.isDirectory) { val dest=File(stage,item.relativePath!!);File(directory,i.toString()).inputStream().use { source->dest.outputStream().buffered(256*1024).use { output->val buffer=ByteArray(FileProtocol.CHUNK_SIZE);while(true){checkActive();val n=source.read(buffer);if(n<0)break;output.write(buffer,0,n)} } } } }
            val roots=mutableMapOf<String,File>()
            synchronized(ReceivedFiles::class.java) {
                offer.files.map { it.relativePath!!.substringBefore('/') }.distinct().forEach { name->
                    checkActive();val source=File(stage,name);var target=File(destination,name);var suffix=1
                    while(target.exists()){target=File(destination,name+" ("+suffix+++")")}
                    check(source.renameTo(target)) { "无法保存文件夹" };committed.add(target);roots[name]=target
                }
            }
            return offer.files.map { val path=it.relativePath!!;val root=roots.getValue(path.substringBefore('/'));ReceivedFileProvider.uri(context,if(path.contains('/')) File(root,path.substringAfter('/')) else root) }
        } catch(e:Exception) { committed.forEach { it.deleteRecursively() };throw e }
        finally { stage.deleteRecursively() }
    }
    override fun close() { try { output?.close() } finally { output=null; directory.deleteRecursively() } }
}
