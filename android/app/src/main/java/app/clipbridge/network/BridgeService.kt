package app.clipbridge.network

import android.app.*
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.net.wifi.WifiManager
import android.os.IBinder
import app.clipbridge.MainActivity
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
    private val workers=Executors.newCachedThreadPool()
    private val running=AtomicBoolean(false)
    private val sending=AtomicBoolean(false)
    private val slots=Semaphore(4)
    private val sockets=ConcurrentHashMap.newKeySet<Socket>()
    private val socketPeers=ConcurrentHashMap<Socket,String>()
    private val seenText=ConcurrentHashMap<String,Long>()
    private var textListener: ServerSocket?=null
    private var fileListener: ServerSocket?=null
    private var multicast: WifiManager.MulticastLock?=null
    private var pairing: ByteArray?=null
    private var textKey: ByteArray?=null
    private lateinit var store: PairingStore
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
            files=FileTransfers(this,pairing,store.deviceId(),{ p ->
                transfers[p.id]=p
                if(transfers.size>200) transfers.entries.firstOrNull { it.value.state !in listOf("传输中","准备文件","等待对方接受") && !pending.containsKey(it.key) }?.let { transfers.remove(it.key) }
            },{ p -> pending[p.offer.id]=p; setStatus("有文件待接收，请主动打开互传处理") },{ id -> pending.remove(id) },hub)
            hub?.revoked={ id -> socketPeers.filterValues { it==id }.keys.forEach { runCatching { it.close() } }; files.revoke(id) }
            workers.execute { acceptLoop(textListener!!,false) }; workers.execute { acceptLoop(fileListener!!,true) }
            setStatus("正在搜索同一网络里的设备")
        } catch(e: Exception) { setStatus("连接服务启动失败："+(e.message ?: e.javaClass.simpleName)); running.set(false); closeNetwork(); stopSelf() }
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
            val value=JSONObject(BridgeProtocol.read(route.input,route.key).toString(Charsets.UTF_8))
            val id=value.getString("MessageId"); val origin=value.getString("OriginDeviceId"); val text=value.getString("Text")
            require(value.getString("Type")=="clipboard" && id.length in 1..64 && origin.length in 1..64 && origin!=store.deviceId() && (route.source==null || route.source==origin) && text.isNotEmpty() && text.toByteArray().size<=BridgeProtocol.MAX_TEXT && BridgeProtocol.fresh(value.getLong("TimestampUtc")))
            require(!isSecret(text))
            if(route.source!=null) require(store.records().any { it.id==route.source && it.active })
            seenText.entries.removeIf { System.currentTimeMillis()-it.value>600000 }
            require(seenText.size<2048 && seenText.putIfAbsent(id,System.currentTimeMillis())==null)
            receivedText=text
            try { getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge",text)); setStatus("已接收文字并写入剪贴板") }
            catch(_: Exception) { setStatus("已接收文字，请打开互传点击复制") }
        } catch(_: Exception) { setStatus("一条文字未能接收，请检查配对和网络") }
    }
    private fun isSecret(text: String): Boolean = listOfNotNull(pairing).plus(store.records().map { it.key }).any { text.trim().equals(BridgeProtocol.hex(it),true) }
    fun sendText(host: String,text: String) {
        require(BridgeProtocol.isLocal(host) && text.isNotEmpty() && text.toByteArray().size<=BridgeProtocol.MAX_TEXT) { "文字或目标地址无效" }
        require(!isSecret(text)) { "不能发送配对密钥" }; check(sending.compareAndSet(false,true)) { "已有发送任务，请稍后重试" }
        workers.execute {
            try {
                Socket().use { socket -> sockets.add(socket)
                    try {
                        socket.connect(InetSocketAddress(host,BridgeProtocol.TEXT_PORT),5000); socket.soTimeout=5000
                        hub?.nearby?.values?.firstOrNull { it.address==host && it.authenticated }?.let { socketPeers[socket]=it.id }
                        val key=hub!!.outgoing(socket,1,textKey)
                        val message=JSONObject().put("Type","clipboard").put("MessageId",UUID.randomUUID().toString().replace("-","")).put("OriginDeviceId",store.deviceId()).put("Text",text).put("TimestampUtc",System.currentTimeMillis())
                        BridgeProtocol.writeJson(socket.getOutputStream(),key,message)
                    } finally { sockets.remove(socket); socketPeers.remove(socket) }
                }
                setStatus("文字已发送，请在目标设备确认")
            } catch(_: Exception) { setStatus("文字发送失败，请检查设备、配对和网络") }
            finally { sending.set(false) }
        }
    }
    fun sendFiles(host: String,uris: List<Uri>) {
        require(BridgeProtocol.isLocal(host)) { "请选择目标设备" }; check(::files.isInitialized && sending.compareAndSet(false,true)) { "服务准备中或已有发送任务" }
        workers.execute { try { files.send(host,uris) } finally { sending.set(false) } }
    }
    fun receivedFiles(id: String): List<Uri> = if(::files.isInitialized) files.received[id] ?: emptyList() else emptyList()
    fun cancel(id: String) { pending[id]?.decision?.complete(false); if(::files.isInitialized) files.cancel(id) }
    private fun closeNetwork() {
        runCatching { hub?.close() }; hub=null; runCatching { textListener?.close() }; runCatching { fileListener?.close() }
        sockets.forEach { runCatching { it.close() } }; sockets.clear(); if(::files.isInitialized) files.stop()
        pending.values.forEach { it.decision.complete(false) }; pending.clear(); runCatching { multicast?.takeIf { it.isHeld }?.release() }
    }
    override fun onDestroy() { running.set(false); closeNetwork(); workers.shutdownNow(); if(current===this) current=null; super.onDestroy() }
}
