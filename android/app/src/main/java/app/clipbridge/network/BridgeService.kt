package app.clipbridge.network

import android.app.*
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.wifi.WifiManager
import android.os.IBinder
import android.util.Log
import app.clipbridge.MainActivity
import app.clipbridge.history.*
import app.clipbridge.protocol.*
import app.clipbridge.storage.PairingStore
import org.json.JSONObject
import java.net.*
import java.util.UUID
import java.util.concurrent.*
import java.util.concurrent.atomic.AtomicBoolean

data class Peer(val address: String,val name: String,val lastSeen: Long) { override fun toString() = "$name · $address" }
class BridgeService : Service() {
    companion object { @Volatile var current: BridgeService?=null; const val STOP="app.clipbridge.STOP" }
    val peers=ConcurrentHashMap<String,Peer>()
    val pending=ConcurrentHashMap<String,Pending>()
    val transfers=ConcurrentHashMap<String,Progress>()
    @Volatile var hub: DeviceHub?=null; private set
    @Volatile var status="正在搜索设备…"; private set
    @Volatile var receivedText: String?=null; private set
    @Volatile var historyRevision: Long=0; private set
    private val workers=Executors.newCachedThreadPool()
    @Volatile private var historyPruner: ScheduledExecutorService?=null
    private val running=AtomicBoolean(false)
    private val sending=AtomicBoolean(false)
    private val slots=Semaphore(4)
    private val sockets=ConcurrentHashMap.newKeySet<Socket>()
    private val socketPeers=ConcurrentHashMap<Socket,String>()
    private val seenText=ConcurrentHashMap<String,Long>()
    private val historySyncAttempts=ConcurrentHashMap.newKeySet<String>()
    private var textListener: ServerSocket?=null
    private var fileListener: ServerSocket?=null
    private var multicast: WifiManager.MulticastLock?=null
    private var networkCallback: ConnectivityManager.NetworkCallback?=null
    private val networkCallbackGate=Any()
    private var pairing: ByteArray?=null
    private var textKey: ByteArray?=null
    private lateinit var store: PairingStore
    private val history: HistoryStore by lazy(LazyThreadSafetyMode.SYNCHRONIZED) { HistoryStore(filesDir) }
    private lateinit var files: FileTransfers
    override fun onBind(intent: Intent?): IBinder?=null
    override fun onCreate() { super.onCreate(); current=this; store=PairingStore(this) }
    override fun onStartCommand(intent: Intent?,flags: Int,startId: Int): Int {
        if(intent?.action==STOP) { stopSelf(); return START_NOT_STICKY }
        getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel("connection","互传连接状态",NotificationManager.IMPORTANCE_LOW).apply { setSound(null,null); enableVibration(false); setShowBadge(false) })
        startForeground(1,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        if(running.compareAndSet(false,true)) { pairing=store.load(); textKey=pairing?.let(BridgeProtocol::deriveKey); workers.execute(::startNetwork) }
        return START_NOT_STICKY
    }
    private fun notification(): Notification {
        val open=PendingIntent.getActivity(this,0,Intent(this,MainActivity::class.java),PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stop=PendingIntent.getService(this,1,Intent(this,BridgeService::class.java).setAction(STOP),PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this,"connection").setSmallIcon(android.R.drawable.stat_sys_upload_done).setContentTitle("互传 · 手动发送").setContentText(status).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).addAction(Notification.Action.Builder(null,"停止连接",stop).build()).build()
    }
    private fun setStatus(value: String) { status=value; if(running.get()) runCatching { getSystemService(NotificationManager::class.java).notify(1,notification()) } }
    private fun startNetwork() {
        try {
            multicast=(applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager).createMulticastLock("clipbridge-discovery").apply { setReferenceCounted(false); acquire() }
            textListener=ServerSocket().apply { reuseAddress=true; bind(InetSocketAddress(BridgeProtocol.TEXT_PORT)) }
            fileListener=ServerSocket().apply { reuseAddress=true; bind(InetSocketAddress(FileProtocol.PORT)) }
            hub=DeviceHub(store,pairing,{ p -> if(peers.size<32 || peers.containsKey(p.address)) peers[p.address]=p },::setStatus)
            hub?.peerReady={ peer ->
                if(peer.historySyncVersion==HistorySyncProtocol.VERSION && store.deviceId()<peer.id) {
                    val epochKey="${peer.id}:${peer.connectionEpoch}"
                    if(historySyncAttempts.add(epochKey)) workers.execute { try { syncHistory(peer.address,peer.id) } finally { historySyncAttempts.remove(epochKey) } }
                }
            }
            hub?.nearby?.values?.forEach { hub?.peerReady?.invoke(it) }
            runCatching { watchNetworkChanges() }
            files=FileTransfers(this,pairing,store.deviceId(),{ p ->
                transfers[p.id]=p
                if(transfers.size>200) transfers.entries.firstOrNull { it.value.state !in listOf("传输中","准备文件","等待对方接受") && !pending.containsKey(it.key) }?.let { transfers.remove(it.key) }
            },{ p -> pending[p.offer.id]=p; setStatus("有文件待接收，请主动打开互传处理") },{ id -> pending.remove(id) },hub,{ history })
            hub?.revoked={ id -> socketPeers.filterValues { it==id }.keys.forEach { runCatching { it.close() } }; files.revoke(id) }
            workers.execute { acceptLoop(textListener!!,false) }; workers.execute { acceptLoop(fileListener!!,true) }
            setStatus("正在搜索同一网络里的设备")
            startHistoryPruner()
        } catch(e: Exception) { setStatus("连接服务启动失败："+(e.message ?: e.javaClass.simpleName)); running.set(false); closeNetwork(); stopSelf() }
    }
    private fun startHistoryPruner() {
        if(!running.get()) return
        val executor=Executors.newSingleThreadScheduledExecutor { runnable -> Thread(runnable,"clipbridge-history-prune").apply { isDaemon=true } }
        historyPruner=executor
        try {
            executor.scheduleWithFixedDelay({
                if(running.get()) {
                    try { history.prune(System.currentTimeMillis()); historyRevision++ }
                    catch(e: Exception) { Log.w("ClipBridge", "History prune failed", e) }
                }
            },0,1,TimeUnit.HOURS)
        } catch(e: Exception) {
            executor.shutdownNow()
            historyPruner=null
            Log.w("ClipBridge", "History prune scheduler could not start", e)
        }
    }
    private fun stopHistoryPruner() { historyPruner?.shutdownNow(); historyPruner=null }
    private fun watchNetworkChanges() {
        val manager=getSystemService(ConnectivityManager::class.java)
        val callback=object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) { hub?.refreshDiscovery() }
            override fun onLost(network: Network) { hub?.refreshDiscovery() }
            override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) { hub?.refreshDiscovery() }
        }
        synchronized(networkCallbackGate) { if(!running.get()) return; manager.registerDefaultNetworkCallback(callback); networkCallback=callback }
    }
    private fun acceptLoop(listener: ServerSocket,file: Boolean) {
        while(running.get()) {
            try {
                val socket=listener.accept()
                if(!BridgeProtocol.isLocal(socket.inetAddress.hostAddress ?: "") || !slots.tryAcquire()) { socket.close(); continue }
                sockets.add(socket)
                workers.execute { try { if(file) files.receive(socket) else receiveText(socket) } finally { runCatching { socket.close() }; sockets.remove(socket); socketPeers.remove(socket); slots.release() } }
            } catch(_: Exception) { if(!running.get()) return }
        }
    }
    private fun receiveText(socket: Socket) {
        try {
            socket.soTimeout=5000
            val route=hub!!.incoming(socket,1,textKey); route.source?.let { socketPeers[socket]=it }
            val payload=BridgeProtocol.read(route.input,route.key)
            val value=JSONObject(payload.toString(Charsets.UTF_8))
            if(value.optString("type").startsWith("historySync")) {
                val source=route.source ?: return
                if(hub?.isTrusted(source)!=true) return
                socket.soTimeout=120000
                try { handleHistorySync(socket,route.key,source,HistorySyncProtocol.decode(payload,source,System.currentTimeMillis())) }
                catch(e: Exception) { Log.w("ClipBridge", "History sync failed", e) }
                return
            }
            if(value.optString("Type")!="clipboard") return
            val id=value.getString("MessageId"); val origin=value.getString("OriginDeviceId"); val text=value.getString("Text"); val timestampUtc=value.getLong("TimestampUtc")
            require(value.getString("Type")=="clipboard" && id.length in 1..64 && origin.length in 1..64 && origin!=store.deviceId() && (route.source==null || route.source==origin) && text.isNotEmpty() && text.toByteArray().size<=BridgeProtocol.MAX_TEXT && BridgeProtocol.fresh(timestampUtc))
            require(!isSecret(text))
            if(route.source!=null) require(store.records().any { it.id==route.source && it.active })
            seenText.entries.removeIf { System.currentTimeMillis()-it.value>600000 }
            require(seenText.size<2048 && seenText.putIfAbsent(id,System.currentTimeMillis())==null)
            val historySaved=route.source?.takeIf { hub?.isTrusted(it)==true }?.let { peerId -> recordTextHistory(peerId,id,origin,timestampUtc,text) } ?: true
            receivedText=text
            val clipboardWritten=try { getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge",text)); true }
                catch(_: Exception) { false }
            setStatus(composeTextReceiveStatus(clipboardWritten,historySaved))
        } catch(_: Exception) { setStatus("一条文字未能接收，请检查配对和网络") }
    }
    fun localDeviceId(): String = store.deviceId()
    fun recentHistory(peerId: String): List<HistoryLocalRecord> = history.getRecent(peerId, System.currentTimeMillis())
    fun deleteHistory(peerId: String, eventId: String) {
        history.deleteRecord(peerId, eventId, System.currentTimeMillis())
        historyRevision++
    }

    internal fun syncHistory(host: String, peerId: String): Boolean {
        if(!BridgeProtocol.isLocal(host) || peerId.length!=32 || hub?.isTrusted(peerId)!=true) return false
        return try { exchangeHistory(host,peerId) } catch(_: Exception) { false }
    }

    private fun exchangeHistory(host: String, peerId: String): Boolean {
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host,BridgeProtocol.TEXT_PORT),5000); socket.soTimeout=120000
            val route=hub?.outgoingRoute(socket,1,textKey) ?: return false
            if(route.peerId!=peerId) return false
            val requestId=UUID.randomUUID().toString().replace("-","")
            val localPages=HistorySyncProtocol.createPages(requestId,store.deviceId(),history.getSyncSnapshot(peerId,System.currentTimeMillis()))
            writeHistoryMessage(socket,route.key,store.deviceId(),HistorySyncProtocol.createHello(requestId,store.deviceId(),localPages.size))
            val hello=readHistoryMessage(socket,route.key,peerId)
            if(hello.type!="historySyncHello" || hello.requestId!=requestId) return false
            val entries=mutableListOf<HistorySyncEvent>(); val tombstones=mutableListOf<HistoryTombstone>()
            repeat(hello.pageNumber) { number ->
                writeHistoryMessage(socket,route.key,store.deviceId(),HistorySyncProtocol.createRequest(requestId,store.deviceId(),number))
                val page=readHistoryMessage(socket,route.key,peerId)
                if(page.type!="historySyncPage" || page.requestId!=requestId || page.pageNumber!=number) return false
                entries+=page.entries!!; tombstones+=page.tombstones!!
                writeHistoryMessage(socket,route.key,store.deviceId(),HistorySyncProtocol.createAck(requestId,store.deviceId(),number))
            }
            history.merge(peerId,entries,tombstones,System.currentTimeMillis())
            historyRevision++
            localPages.forEachIndexed { number,page ->
                writeHistoryMessage(socket,route.key,store.deviceId(),page)
                val ack=readHistoryMessage(socket,route.key,peerId)
                if(ack.type!="historySyncAck" || ack.requestId!=requestId || ack.pageNumber!=number) return false
            }
            return true
        }
    }

    private fun handleHistorySync(socket: Socket,key: ByteArray,peerId: String,first: HistorySyncMessage) {
        if(first.type!="historySyncHello" || hub?.isTrusted(peerId)!=true) return
        val requestId=first.requestId
        val localPages=HistorySyncProtocol.createPages(requestId,store.deviceId(),history.getSyncSnapshot(peerId,System.currentTimeMillis()))
        writeHistoryMessage(socket,key,store.deviceId(),HistorySyncProtocol.createHello(requestId,store.deviceId(),localPages.size))
        for(number in localPages.indices) {
            val request=readHistoryMessage(socket,key,peerId)
            if(request.type!="historySyncRequest" || request.requestId!=requestId || request.pageNumber!=number) return
            writeHistoryMessage(socket,key,store.deviceId(),localPages[number])
            val ack=readHistoryMessage(socket,key,peerId)
            if(ack.type!="historySyncAck" || ack.requestId!=requestId || ack.pageNumber!=number) return
        }
        val entries=mutableListOf<HistorySyncEvent>(); val tombstones=mutableListOf<HistoryTombstone>()
        repeat(first.pageNumber) { number ->
            val page=readHistoryMessage(socket,key,peerId)
            if(page.type!="historySyncPage" || page.requestId!=requestId || page.pageNumber!=number) return
            entries+=page.entries!!; tombstones+=page.tombstones!!
            writeHistoryMessage(socket,key,store.deviceId(),HistorySyncProtocol.createAck(requestId,store.deviceId(),number))
        }
        history.merge(peerId,entries,tombstones,System.currentTimeMillis())
        historyRevision++
    }

    private fun writeHistoryMessage(socket: Socket,key: ByteArray,authenticatedSelfId: String,message: HistorySyncMessage) {
        BridgeProtocol.write(socket.getOutputStream(),key,HistorySyncProtocol.encode(message,authenticatedSelfId,System.currentTimeMillis()))
    }
    private fun readHistoryMessage(socket: Socket,key: ByteArray,authenticatedPeerId: String): HistorySyncMessage =
        HistorySyncProtocol.decode(BridgeProtocol.read(socket.getInputStream(),key),authenticatedPeerId,System.currentTimeMillis())

    private fun isSecret(text: String): Boolean = listOfNotNull(pairing).plus(store.records().map { it.key }).any { text.trim().equals(BridgeProtocol.hex(it),true) }
    fun sendText(host: String,text: String) {
        require(BridgeProtocol.isLocal(host) && text.isNotEmpty() && text.toByteArray().size<=BridgeProtocol.MAX_TEXT) { "文字或目标地址无效" }
        require(!isSecret(text)) { "不能发送配对密钥" }; check(sending.compareAndSet(false,true)) { "已有发送任务，请稍后重试" }
        workers.execute {
            try {
                var sentRecord: Triple<String,String,Long>? = null
                Socket().use { socket -> sockets.add(socket)
                    try {
                        socket.connect(InetSocketAddress(host,BridgeProtocol.TEXT_PORT),5000); socket.soTimeout=5000
                        val route=hub!!.outgoingRoute(socket,1,textKey)
                        route.peerId?.let { socketPeers[socket]=it }
                        val messageId=UUID.randomUUID().toString().replace("-","")
                        val timestampUtc=System.currentTimeMillis()
                        val origin=store.deviceId()
                        val message=JSONObject().put("Type","clipboard").put("MessageId",messageId).put("OriginDeviceId",origin).put("Text",text).put("TimestampUtc",timestampUtc)
                        BridgeProtocol.writeJson(socket.getOutputStream(),route.key,message)
                        if(route.peerId!=null) sentRecord=Triple(route.peerId,messageId,timestampUtc)
                    } finally { sockets.remove(socket); socketPeers.remove(socket) }
                }
                setStatus("文字已发送，请在目标设备确认")
                sentRecord?.let { (peerId,messageId,timestampUtc) ->
                    if(!recordTextHistory(peerId,messageId,store.deviceId(),timestampUtc,text)) Log.w("ClipBridge", "Text send completed but history was not saved")
                }
            } catch(_: Exception) { setStatus("文字发送失败，请检查设备、配对和网络") }
            finally { sending.set(false) }
        }
    }
    private fun recordTextHistory(peerId: String,eventId: String,senderDeviceId: String,createdUtc: Long,text: String): Boolean = try {
        history.upsert(HistoryLocalRecord(HistorySyncEvent(HistoryPolicy.SCHEMA_VERSION,eventId,peerId,senderDeviceId,createdUtc,"text",text,emptyList(),"sent")))
        historyRevision++
        true
    } catch(e: Exception) { Log.w("ClipBridge", "Text history save failed", e); false }

    fun sendFiles(host: String,uris: List<Uri>) {
        require(BridgeProtocol.isLocal(host)) { "请选择目标设备" }; check(::files.isInitialized && sending.compareAndSet(false,true)) { "服务准备中或已有发送任务" }
        workers.execute { try { files.send(host,uris) } finally { sending.set(false) } }
    }
    fun receivedFiles(id: String): List<Uri> = if(::files.isInitialized) files.received[id] ?: emptyList() else emptyList()
    fun cancel(id: String) { pending[id]?.decision?.complete(false); if(::files.isInitialized) files.cancel(id) }
    private fun closeNetwork() {
        stopHistoryPruner()
        synchronized(networkCallbackGate) { networkCallback?.let { callback -> runCatching { getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(callback) } }; networkCallback=null }
        runCatching { hub?.close() }; hub=null; runCatching { textListener?.close() }; runCatching { fileListener?.close() }
        sockets.forEach { runCatching { it.close() } }; sockets.clear(); if(::files.isInitialized) files.stop()
        pending.values.forEach { it.decision.complete(false) }; pending.clear(); runCatching { multicast?.takeIf { it.isHeld }?.release() }
    }
    override fun onDestroy() { running.set(false); stopHistoryPruner(); closeNetwork(); workers.shutdownNow(); if(current===this) current=null; super.onDestroy() }
}

internal fun composeTextReceiveStatus(clipboardWritten: Boolean,historySaved: Boolean): String {
    val receive=if(clipboardWritten) "已接收文字并写入剪贴板" else "已接收文字，请打开互传点击复制"
    return if(historySaved) receive else "$receive（历史记录保存失败）"
}
