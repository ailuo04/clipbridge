package app.clipbridge.network

import android.app.*
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import app.clipbridge.MainActivity
import app.clipbridge.protocol.*
import app.clipbridge.storage.PairingStore
import org.json.JSONObject
import java.net.*
import java.util.UUID
import java.util.concurrent.*
import java.util.concurrent.atomic.AtomicBoolean

data class Peer(val address: String, val name: String, val lastSeen: Long) { override fun toString() = "$name · $address" }

class BridgeService : Service() {
    companion object { @Volatile var current: BridgeService? = null; const val STOP = "app.clipbridge.STOP" }
    val peers = ConcurrentHashMap<String, Peer>()
    val pending = ConcurrentHashMap<String, Pending>()
    val transfers = ConcurrentHashMap<String, Progress>()
    @Volatile var status = "正在连接…"; private set
    @Volatile var receivedText: String? = null; private set
    private val workers = Executors.newCachedThreadPool()
    private val running = AtomicBoolean(false)
    private val sending = AtomicBoolean(false)
    private val slots = Semaphore(4)
    private val sockets = ConcurrentHashMap.newKeySet<Socket>()
    private val seenText = ConcurrentHashMap<String, Long>()
    private var discovery: DatagramSocket? = null
    private var textListener: ServerSocket? = null
    private var fileListener: ServerSocket? = null
    private var multicast: WifiManager.MulticastLock? = null
    private lateinit var pairing: ByteArray
    private lateinit var textKey: ByteArray
    private lateinit var store: PairingStore
    private lateinit var fileTransfers: FileTransfers

    override fun onBind(intent: Intent?): IBinder? = null
    override fun onCreate() { super.onCreate(); current = this; store = PairingStore(this) }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) { stopSelf(); return START_NOT_STICKY }
        val channel = NotificationChannel("connection", "互传连接状态", NotificationManager.IMPORTANCE_LOW).apply { setSound(null, null); enableVibration(false); setShowBadge(false) }
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
        startForeground(1, notification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        if (running.compareAndSet(false, true)) {
            val saved = store.load()
            if (saved == null) { setStatus("请先配对"); stopSelf(); return START_NOT_STICKY }
            pairing = saved; textKey = BridgeProtocol.deriveKey(saved)
            fileTransfers = FileTransfers(this, saved, store.deviceId(), { p ->
                transfers[p.id] = p
                if (transfers.size > 200) transfers.entries.firstOrNull { it.value.state != "传输中" && !pending.containsKey(it.key) }?.let { transfers.remove(it.key) }
            }, { p -> pending[p.offer.id] = p; setStatus("有文件待接收，请主动打开互传处理") }, { id -> pending.remove(id) })
            workers.execute { startNetwork() }
        }
        return START_NOT_STICKY
    }
    private fun notification(): Notification {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stop = PendingIntent.getService(this, 1, Intent(this, BridgeService::class.java).setAction(STOP), PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, "connection").setSmallIcon(android.R.drawable.stat_sys_upload_done).setContentTitle("互传 · 手动发送").setContentText(status).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).addAction(Notification.Action.Builder(null, "停止连接", stop).build()).build()
    }
    private fun setStatus(text: String) {
        status = text
        if (running.get()) runCatching { getSystemService(NotificationManager::class.java).notify(1, notification()) }
    }
    private fun startNetwork() {
        try {
            multicast = (applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager).createMulticastLock("clipbridge-discovery").apply { setReferenceCounted(false); acquire() }
            discovery = DatagramSocket(null).apply { reuseAddress = true; broadcast = true; bind(InetSocketAddress(BridgeProtocol.DISCOVERY_PORT)); soTimeout = 2000 }
            textListener = ServerSocket().apply { reuseAddress = true; bind(InetSocketAddress(BridgeProtocol.TEXT_PORT)) }
            fileListener = ServerSocket().apply { reuseAddress = true; bind(InetSocketAddress(FileProtocol.PORT)) }
            workers.execute { discover() }; workers.execute { announceLoop() }
            workers.execute { acceptLoop(textListener!!, false) }; workers.execute { acceptLoop(fileListener!!, true) }
            setStatus("就绪 · 只有点击发送才读取剪贴板")
        } catch (e: Exception) { setStatus("连接服务启动失败，请停止后重试：" + (e.message ?: e.javaClass.simpleName)); running.set(false); closeNetwork(); stopSelf() }
    }
    private fun announceBytes(): ByteArray = BridgeProtocol.encrypt(textKey, JSONObject().put("Type", "announce").put("DeviceId", store.deviceId()).put("Name", Build.MODEL).put("Port", BridgeProtocol.TEXT_PORT).put("TimestampUtc", System.currentTimeMillis()).toString().toByteArray())
    private fun sendAnnouncement(host: String) {
        val data = announceBytes(); discovery?.send(DatagramPacket(data, data.size, InetAddress.getByName(host), BridgeProtocol.DISCOVERY_PORT))
    }
    private fun announceLoop() {
        while (running.get()) {
            runCatching { sendAnnouncement("255.255.255.255") }
            store.peer().takeIf { BridgeProtocol.isLocal(it) }?.let { runCatching { sendAnnouncement(it) } }
            peers.entries.removeIf { System.currentTimeMillis() - it.value.lastSeen > 15000 }
            try { Thread.sleep(3000) } catch (_: InterruptedException) { return }
        }
    }
    private fun discover() {
        while (running.get()) {
            try {
                val packet = DatagramPacket(ByteArray(4097), 4097); discovery?.receive(packet) ?: return
                val host = packet.address.hostAddress ?: continue
                if (!BridgeProtocol.isLocal(host) || packet.length !in 28..4096) continue
                val value = JSONObject(BridgeProtocol.decrypt(textKey, packet.data.copyOf(packet.length)).toString(Charsets.UTF_8))
                val id = value.getString("DeviceId")
                if (value.getString("Type") != "announce" || id == store.deviceId() || id.length !in 1..64 || value.getInt("Port") != BridgeProtocol.TEXT_PORT || !BridgeProtocol.fresh(value.getLong("TimestampUtc"))) continue
                val added = !peers.containsKey(host)
                if (added && peers.size >= 32) continue
                peers[host] = Peer(host, value.optString("Name", "配对设备").take(80), System.currentTimeMillis())
                if (added) runCatching { sendAnnouncement(host) }
            } catch (_: SocketTimeoutException) { } catch (_: Exception) { if (!running.get()) return }
        }
    }
    private fun acceptLoop(listener: ServerSocket, files: Boolean) {
        while (running.get()) {
            try {
                val socket = listener.accept()
                if (!BridgeProtocol.isLocal(socket.inetAddress.hostAddress ?: "") || !slots.tryAcquire()) { socket.close(); continue }
                sockets.add(socket)
                workers.execute { try { if (files) fileTransfers.receive(socket) else receiveText(socket) } finally { runCatching { socket.close() }; sockets.remove(socket); slots.release() } }
            } catch (_: Exception) { if (!running.get()) return }
        }
    }
    private fun receiveText(socket: Socket) {
        try {
            socket.soTimeout = 5000
            val value = JSONObject(BridgeProtocol.read(socket.getInputStream(), textKey).toString(Charsets.UTF_8))
            val id = value.getString("MessageId"); val origin = value.getString("OriginDeviceId"); val text = value.getString("Text")
            require(value.getString("Type") == "clipboard" && id.length in 1..64 && origin.length in 1..64 && origin != store.deviceId() && text.isNotEmpty() && text.toByteArray().size <= BridgeProtocol.MAX_TEXT && BridgeProtocol.fresh(value.getLong("TimestampUtc")))
            require(!text.trim().equals(BridgeProtocol.hex(pairing), true))
            seenText.entries.removeIf { System.currentTimeMillis() - it.value > 600000 }
            require(seenText.size < 2048 && seenText.putIfAbsent(id, System.currentTimeMillis()) == null)
            receivedText = text
            try { getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge", text)); setStatus("已接收文字并写入剪贴板") }
            catch (_: Exception) { setStatus("已接收文字，请打开互传点击复制") }
        } catch (_: Exception) { setStatus("一条文字未能接收，请检查配对和网络") }
    }
    fun sendText(host: String, text: String) {
        require(BridgeProtocol.isLocal(host) && text.isNotEmpty() && text.toByteArray().size <= BridgeProtocol.MAX_TEXT) { "文字或目标地址无效" }
        require(!text.trim().equals(BridgeProtocol.hex(pairing), true)) { "不能发送配对密钥" }
        check(sending.compareAndSet(false, true)) { "已有发送任务，请稍后重试" }
        workers.execute {
            try {
                Socket().use { socket ->
                    sockets.add(socket)
                    try {
                        socket.connect(InetSocketAddress(host, BridgeProtocol.TEXT_PORT), 5000); socket.soTimeout = 5000
                        val message = JSONObject().put("Type", "clipboard").put("MessageId", UUID.randomUUID().toString().replace("-", "")).put("OriginDeviceId", store.deviceId()).put("Text", text).put("TimestampUtc", System.currentTimeMillis())
                        BridgeProtocol.writeJson(socket.getOutputStream(), textKey, message)
                    } finally { sockets.remove(socket) }
                }
                setStatus("文字已发送，请在目标设备确认")
            } catch (_: Exception) { setStatus("文字发送失败，请检查电脑是否运行互传、配对和防火墙") }
            finally { sending.set(false) }
        }
    }
    fun sendFiles(host: String, uris: List<Uri>) {
        require(BridgeProtocol.isLocal(host)) { "请选择目标设备或输入局域网 IPv4 地址" }
        check(sending.compareAndSet(false, true)) { "已有发送任务，请稍后重试" }
        workers.execute { try { fileTransfers.send(host, uris) } finally { sending.set(false) } }
    }
    fun cancel(id: String) { pending[id]?.decision?.complete(false); if (::fileTransfers.isInitialized) fileTransfers.cancel(id) }
    private fun closeNetwork() {
        runCatching { discovery?.close() }; runCatching { textListener?.close() }; runCatching { fileListener?.close() }
        sockets.forEach { runCatching { it.close() } }; sockets.clear()
        if (::fileTransfers.isInitialized) fileTransfers.stop()
        pending.values.forEach { it.decision.complete(false) }; pending.clear()
        runCatching { multicast?.takeIf { it.isHeld }?.release() }
    }
    override fun onDestroy() {
        running.set(false); closeNetwork(); workers.shutdownNow()
        if (current === this) current = null
        super.onDestroy()
    }
}
