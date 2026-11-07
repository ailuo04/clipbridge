package app.clipbridge.network

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import app.clipbridge.protocol.*
import app.clipbridge.history.*
import android.util.Log
import app.clipbridge.storage.ReceivedFiles
import org.json.JSONObject
import java.io.File
import java.net.InetSocketAddress
import java.net.Socket
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

data class Progress(val id: String, val direction: String, val state: String, val done: Long = 0, val total: Long = 0,val name: String = "",val address: String = "")
data class Pending(val offer: Offer, val address: String, val decision: java.util.concurrent.CompletableFuture<Boolean>)

internal fun fileHistoryEvent(peerId: String, offer: Offer, state: String): HistorySyncEvent =
    HistorySyncEvent(HistoryPolicy.SCHEMA_VERSION, offer.id, peerId, offer.origin, offer.timestamp, "file", null,
        offer.files.map { HistoryFileMetadata(it.name, it.size, it.sha256) }, state)

class FileTransfers(private val context: Context, pairing: ByteArray?, private val origin: String, private val update: (Progress) -> Unit, private val offerReceived: (Pending) -> Unit, private val offerDone: (String) -> Unit, private val hub: DeviceHub? = null, private val historyProvider: (() -> HistoryStore?)? = null) {
    private val legacyKey = pairing?.let(FileProtocol::deriveKey)
    private val activePeers = ConcurrentHashMap<String,String>()
    val received = ConcurrentHashMap<String,List<Uri>>()
    private val descriptions=ConcurrentHashMap<String,Pair<String,String>>()
    private fun publish(progress: Progress) { val info=descriptions[progress.id]; update(progress.copy(name=info?.first ?: "",address=info?.second ?: "")) }
    fun revoke(peer: String) { activePeers.filterValues { it==peer }.keys.forEach(::cancel) }
    private val active = ConcurrentHashMap<String, Socket>()
    private val cancelled = ConcurrentHashMap.newKeySet<String>()
    private val seen = ConcurrentHashMap<String, Long>()
    private val alive = AtomicBoolean(true)
    fun cancel(id: String) { cancelled.add(id); active.remove(id)?.runCatching { close() } }
    fun stop() { alive.set(false); active.values.forEach { runCatching { it.close() } }; active.clear() }
    private fun checkActive(id: String) { check(alive.get() && !cancelled.contains(id) && (activePeers[id]?.let { peer -> hub?.isTrusted(peer) } != false)) { "已取消" } }
    private fun json(socket: Socket, key: ByteArray, input: java.io.InputStream = socket.getInputStream()): JSONObject = JSONObject(BridgeProtocol.read(input, key, FileProtocol.MAX_CONTROL).toString(Charsets.UTF_8))
    private fun write(socket: Socket, key: ByteArray, value: JSONObject) = BridgeProtocol.writeJson(socket.getOutputStream(), key, value)

    fun send(host: String, uris: List<Uri>) {
        require(BridgeProtocol.isLocal(host) && uris.size in 1..256)
        val id = UUID.randomUUID().toString().replace("-", "")
        val temporary = File(context.cacheDir, "send-$id").apply { check(mkdirs()) }
        var done = 0L; var total = 0L
        var offer: Offer? = null
        var authenticatedPeerId: String? = null
        var terminalHistoryRecorded = false
        try {
            val files = uris.mapIndexed { index, uri ->
                checkActive(id); publish(Progress(id, "发送", "准备文件", index.toLong(), uris.size.toLong()))
                var name = "文件-$index"
                context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) name = it.getString(0) ?: name }
                FileProtocol.validateName(name)
                descriptions[id]=Pair(name+(if(uris.size>1) " 等 ${uris.size} 个文件" else ""),host)
                val file = File(temporary, index.toString()); val digest = MessageDigest.getInstance("SHA-256"); var size = 0L
                val input = context.contentResolver.openInputStream(uri) ?: error("无法读取选中的文件")
                input.use { source -> file.outputStream().use { output ->
                    val buffer = ByteArray(FileProtocol.CHUNK_SIZE)
                    while (true) { checkActive(id); val n = source.read(buffer); if (n < 0) break; size += n; require(size <= FileProtocol.MAX_TOTAL - total) { "文件过大" }; output.write(buffer, 0, n); digest.update(buffer, 0, n) }
                } }
                total += size; FileItem(name, size, BridgeProtocol.hex(digest.digest()))
            }
            val currentOffer = Offer(id, origin, System.currentTimeMillis(), files).also(FileProtocol::validateOffer)
            offer = currentOffer
            Socket().use { socket ->
                active[id] = socket; checkActive(id); socket.connect(InetSocketAddress(host, FileProtocol.PORT), 5000); socket.soTimeout = 120000
                val route=hub?.outgoingRoute(socket,2,legacyKey) ?: OutgoingSessionRoute(checkNotNull(legacyKey) { "请先配对" },null)
                val key=route.key
                authenticatedPeerId=route.peerId
                authenticatedPeerId?.let { activePeers[id]=it }
                write(socket, key, currentOffer.json()); publish(Progress(id, "发送", "等待对方接受", 0, total))
                val acceptance=json(socket,key)
                if(acceptance.optString("Type")=="reject" && acceptance.optString("TransferId")==id) {
                    recordFileHistory(authenticatedPeerId,currentOffer,"rejected")
                    terminalHistoryRecorded=true
                }
                FileProtocol.requireReply(acceptance, id, "accept"); socket.soTimeout = 30000
                files.forEachIndexed { index, item ->
                    File(temporary, index.toString()).inputStream().use { input ->
                        val buffer = ByteArray(FileProtocol.CHUNK_SIZE); var offset = 0L
                        while (offset < item.size) {
                            checkActive(id); val n = input.read(buffer, 0, minOf(buffer.size.toLong(), item.size - offset).toInt()); check(n > 0)
                            BridgeProtocol.write(socket.getOutputStream(), key, FileProtocol.chunk(id, index, offset, buffer.copyOf(n)))
                            offset += n; done += n; publish(Progress(id, "发送", "传输中", done, total))
                        }
                    }
                }
                write(socket, key, FileProtocol.reply("finish", id)); socket.soTimeout = 120000
                FileProtocol.requireReply(json(socket,key), id, "complete")
                recordFileHistory(authenticatedPeerId,currentOffer,"sent")
                terminalHistoryRecorded=true
                publish(Progress(id, "发送", "对方已保存并校验", total, total))
            }
        } catch (e: Exception) {
            if(!terminalHistoryRecorded) offer?.let { recordFileHistory(authenticatedPeerId,it,"failed") }
            publish(Progress(id, "发送", if (cancelled.contains(id) || !alive.get()) "已取消" else "失败：" + (e.message ?: e.javaClass.simpleName), done, total))
        }
        finally { active.remove(id); activePeers.remove(id); descriptions.remove(id); cancelled.remove(id); temporary.deleteRecursively() }
    }

    private fun recordFileHistory(peerId: String?, offer: Offer, state: String, localReferences: Map<String,String> = emptyMap()) {
        if(peerId==null) return
        val history=try { historyProvider?.invoke() } catch(e: Exception) { Log.w("ClipBridge", "File history store unavailable", e); null } ?: return
        try { history.upsert(HistoryLocalRecord(fileHistoryEvent(peerId,offer,state),localReferences)) }
        catch(e: Exception) { Log.w("ClipBridge", "File history save failed", e) }
    }

    fun receive(socket: Socket) {
        var id = ""; var done = 0L; var total = 0L
        var offer: Offer? = null
        var authenticatedPeerId: String? = null
        var terminalHistoryRecorded = false
        var localFileReferences: Map<String,String> = emptyMap()
        try {
            socket.use {
                socket.soTimeout = 30000
                val route=hub?.incoming(socket,2,legacyKey) ?: SessionRoute(checkNotNull(legacyKey),null,socket.getInputStream())
                val key=route.key
                val currentOffer = FileProtocol.parseOffer(json(socket,key,route.input));
                require(route.source==null || route.source==currentOffer.origin) { "文件来源不符" }
                offer=currentOffer
                authenticatedPeerId=route.source
                authenticatedPeerId?.let { activePeers[currentOffer.id]=it }; id = currentOffer.id; total = currentOffer.total
                descriptions[id]=Pair(currentOffer.files.first().name+(if(currentOffer.files.size>1) " 等 ${currentOffer.files.size} 个文件" else ""),socket.inetAddress.hostAddress ?: "")
                seen.entries.removeIf { System.currentTimeMillis() - it.value > 600000 }
                require(seen.size < 2048 && seen.putIfAbsent(id, System.currentTimeMillis()) == null) { "重复的文件请求" }
                active[id] = socket
                val pending = Pending(currentOffer, socket.inetAddress.hostAddress ?: "", java.util.concurrent.CompletableFuture())
                offerReceived(pending); publish(Progress(id, "接收", "待接受", 0, total))
                val accepted = try { pending.decision.get(100, TimeUnit.SECONDS) } catch (_: Exception) { false }
                offerDone(id); checkActive(id); write(socket, key, FileProtocol.reply(if (accepted) "accept" else "reject", id))
                if (!accepted) {
                    recordFileHistory(authenticatedPeerId,currentOffer,"rejected")
                    terminalHistoryRecorded=true
                    publish(Progress(id, "接收", "已拒绝或超时", 0, total)); return
                }
                ReceivedFiles(context, currentOffer).use { store ->
                    while (true) {
                        checkActive(id)
                        val plain = BridgeProtocol.read(socket.getInputStream(), key, FileProtocol.MAX_CONTROL)
                        if (plain.isNotEmpty() && plain[0].toInt() == 1) {
                            val chunk = FileProtocol.parseChunk(id, plain); store.append(chunk); done += chunk.data.size
                            publish(Progress(id, "接收", "传输中", done, total))
                        } else {
                            FileProtocol.requireReply(JSONObject(plain.toString(Charsets.UTF_8)), id, "finish")
                            val committedUris=store.commit { checkActive(id) }
                            received[id]=committedUris
                            localFileReferences=committedUris.mapIndexed { index, uri -> index.toString() to uri.toString() }.toMap()
                            if(received.size>200) received.keys.firstOrNull { it!=id }?.let { received.remove(it) }; write(socket, key, FileProtocol.reply("complete", id))
                            recordFileHistory(authenticatedPeerId,currentOffer,"sent",localFileReferences)
                            terminalHistoryRecorded=true
                            publish(Progress(id, "接收", "已保存到 Download/ClipBridge", total, total)); break
                        }
                    }
                }
            }
        } catch (e: Exception) {
            if(id.isNotEmpty()) {
                if(!terminalHistoryRecorded) offer?.let { recordFileHistory(authenticatedPeerId,it,"failed",localFileReferences) }
                publish(Progress(id, "接收", if (cancelled.contains(id) || !alive.get()) "已取消" else "失败：" + (e.message ?: e.javaClass.simpleName), done, total))
            }
        }
        finally { if (id.isNotEmpty()) { offerDone(id); active.remove(id); activePeers.remove(id); descriptions.remove(id); cancelled.remove(id) } }
    }
}
