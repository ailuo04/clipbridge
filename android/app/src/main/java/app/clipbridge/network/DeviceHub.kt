package app.clipbridge.network

import app.clipbridge.protocol.*
import app.clipbridge.storage.*
import org.json.JSONObject
import java.io.*
import java.net.*
import java.security.SecureRandom
import java.util.Base64
import java.util.UUID
import java.util.concurrent.*
import java.util.concurrent.atomic.AtomicBoolean

data class NearbyDevice(val id: String,val name: String,val platform: String,val address: String,val seen: Long,val authenticated: Boolean,val historySyncVersion: Int = 0,val connectionEpoch: Long = 0)
data class PairingRequest(val session: String,val id: String,val name: String,val address: String,val code: String,val expires: Long,val decision: CompletableFuture<Boolean> = CompletableFuture())
data class SessionRoute(val key: ByteArray,val source: String?,val input: InputStream)
data class OutgoingSessionRoute(val key: ByteArray,val peerId: String?)
class DeviceHub(private val store: PairingStore,private val legacy: ByteArray?,private val legacyFound: (Peer)->Unit,private val status: (String)->Unit) : AutoCloseable {
    val nearby = ConcurrentHashMap<String,NearbyDevice>()
    private val sessions=PairingSessions<PairingRequest>()
    val pending get()=sessions.pending
    private val running = AtomicBoolean(true)
    private val wake = DiscoveryWake()
    private val workers = Executors.newCachedThreadPool()
    private val sockets = ConcurrentHashMap.newKeySet<Socket>()
    private val replay = ConcurrentHashMap<String,Long>()
    private val rates = ConcurrentHashMap<String,Long>()
    private val replies = ConcurrentHashMap<String,Long>()
    private val slots = Semaphore(4)
    private val pairIds=ConcurrentHashMap.newKeySet<String>()
    private val epochs=ConcurrentHashMap<String,Int>()
    private val peerEpochs=ConcurrentHashMap<String,Long>()
    private val trustGate=Any()
    private val timer=Executors.newSingleThreadScheduledExecutor()
    private val udp = DatagramSocket(null).apply { reuseAddress=true; broadcast=true; bind(InetSocketAddress(BridgeProtocol.DISCOVERY_PORT)); soTimeout=2000 }
    private val listener = try { ServerSocket().apply { reuseAddress=true; bind(InetSocketAddress(PairingProtocol.PORT)) } } catch(e: Exception) { udp.close(); throw e }
    var revoked: ((String)->Unit)? = null
    var peerReady: ((NearbyDevice)->Unit)? = null
    init { workers.execute(::discover); workers.execute(::announceLoop); workers.execute(::acceptLoop) }
    private fun remember(value: String): Boolean {
        val now=System.currentTimeMillis(); replay.entries.removeIf { now-it.value>240000 }
        return replay.size<4096 && replay.putIfAbsent(value,now)==null
    }
    private fun send(data: ByteArray,host: String) { udp.send(DatagramPacket(data,data.size,InetAddress.getByName(host),BridgeProtocol.DISCOVERY_PORT)) }
    private fun publicBytes() = DiscoveryProtocol.encode(DiscoveryProtocol.create(store.deviceId(),store.name(),"android"))
    private fun broadcasts(): List<String> = NetworkInterface.getNetworkInterfaces().toList().filter { it.isUp && !it.isLoopback }.flatMap { it.interfaceAddresses }.mapNotNull { it.broadcast?.hostAddress }.plus("255.255.255.255").distinct()
    fun refreshDiscovery() { wake.signal() }
    private fun announceLoop() {
        while(running.get()) {
            runCatching {
                val bytes=publicBytes(); broadcasts().forEach { runCatching { send(bytes,it) } }
                val now=System.currentTimeMillis(); nearby.entries.removeIf { now-it.value.seen>15000 }
                store.records().forEach { t -> if(BridgeProtocol.isLocal(t.address)) runCatching { send(DiscoveryProtocol.encode(DiscoveryProtocol.create(store.deviceId(),store.name(),"android",t.id,t.key)),t.address) } }
                legacy?.let { key ->
                    val old=BridgeProtocol.encrypt(BridgeProtocol.deriveKey(key),JSONObject().put("Type","announce").put("DeviceId",store.deviceId()).put("Name",store.name()).put("Port",BridgeProtocol.TEXT_PORT).put("TimestampUtc",now).toString().toByteArray())
                    broadcasts().plus(store.peer()).filter { it.isNotBlank() }.forEach { runCatching { send(old,it) } }
                }
            }.onFailure { status("搜索暂不可用，请检查同一 Wi-Fi 或热点与网络权限") }
            try { wake.await(3000) } catch(_: InterruptedException) { return }
        }
    }
    private fun discover() {
        while(running.get()) {
            try {
                val packet=DatagramPacket(ByteArray(4097),4097); udp.receive(packet)
                val host=packet.address.hostAddress ?: continue
                if(!BridgeProtocol.isLocal(host) || packet.length>4096) continue
                val bytes=packet.data.copyOf(packet.length)
                if(!DiscoveryProtocol.isPublic(bytes)) {
                    val key=legacy ?: continue
                    val p=JSONObject(BridgeProtocol.decrypt(BridgeProtocol.deriveKey(key),bytes).toString(Charsets.UTF_8)); val id=p.getString("DeviceId")
                    if(p.getString("Type")=="announce" && id!=store.deviceId() && id.length in 1..64 && p.getInt("Port")==BridgeProtocol.TEXT_PORT && BridgeProtocol.fresh(p.getLong("TimestampUtc"))) legacyFound(Peer(host,p.optString("Name","旧版设备").take(80),System.currentTimeMillis()))
                    continue
                }
                val p=DiscoveryProtocol.parse(bytes); if(p.id==store.deviceId()) continue
                val trust=store.records().firstOrNull { it.id==p.id }
                val authenticated=trust!=null && DiscoveryProtocol.authenticate(p,store.deviceId(),trust.key) && remember("presence:${p.id}:${p.nonce}")
                if(p.type=="presence" && !authenticated) continue
                val now=System.currentTimeMillis(); if(nearby.size>=32 && !nearby.containsKey(p.id)) continue
                if(authenticated) {
                    val saved=synchronized(trustGate) { val current=store.records().firstOrNull { it.id==p.id }; if(current==null || !current.key.contentEquals(trust!!.key)) false else { store.save(current.copy(name=p.name,address=host,active=true)); true } }
                    if(saved) {
                        val prior=nearby[p.id]
                        val wasReady=prior?.authenticated==true && prior.historySyncVersion==p.historySyncVersion && now-prior.seen<=15000
                        val epoch=if(wasReady) prior!!.connectionEpoch else peerEpochs.compute(p.id) { _,value -> (value ?: 0L)+1L }!!
                        val ready=NearbyDevice(p.id,p.name,p.platform,host,now,true,p.historySyncVersion,epoch)
                        nearby[p.id]=ready
                        if(!wasReady) peerReady?.invoke(ready)
                    }
                } else if(nearby[p.id]?.authenticated!=true) nearby[p.id]=NearbyDevice(p.id,p.name,p.platform,host,now,false,p.historySyncVersion)
                replies.entries.removeIf { now-it.value>30000 }
                if(replies.size<128 && now-(replies[host] ?: 0)>2500) {
                    replies[host]=now; send(publicBytes(),host)
                    if(trust!=null) send(DiscoveryProtocol.encode(DiscoveryProtocol.create(store.deviceId(),store.name(),"android",trust.id,trust.key)),host)
                }
            } catch(_: SocketTimeoutException) {} catch(_: Exception) { if(!running.get()) return }
        }
    }
    fun outgoing(socket: Socket,kind: Int,legacyKey: ByteArray?): ByteArray = outgoingRoute(socket,kind,legacyKey).key
    fun outgoingRoute(socket: Socket,kind: Int,legacyKey: ByteArray?): OutgoingSessionRoute {
        val records=store.records()
        val host=socket.inetAddress.hostAddress
        val now=System.currentTimeMillis()
        val peer=nearby.values.firstOrNull { candidate -> candidate.address==host && candidate.authenticated && now-candidate.seen<=15000 && records.any { trust -> trust.id==candidate.id && trust.active } }
        val trust=records.firstOrNull { it.id==peer?.id && it.active }
        if(trust==null) { check(records.none { it.address==host }) { "目标设备尚未通过在线认证" }; return OutgoingSessionRoute(legacyKey ?: error("请先与设备配对"),null) }
        val header=TrustedChannel.header(store.deviceId(),trust.id,kind,trust.key)
        socket.getOutputStream().write(header); socket.getOutputStream().flush()
        return OutgoingSessionRoute(TrustedChannel.open(header,trust.id,kind,trust.key),trust.id)
    }
    fun incoming(socket: Socket,kind: Int,legacyKey: ByteArray?): SessionRoute {
        val input=socket.getInputStream(); val first=ByteArray(4).also { DataInputStream(input).readFully(it) }
        if(!first.contentEquals(TrustedChannel.MAGIC)) return SessionRoute(legacyKey ?: error("旧版连接尚未配对"),null,SequenceInputStream(ByteArrayInputStream(first),input))
        val header=first+ByteArray(61).also { DataInputStream(input).readFully(it) }
        val trust=store.records().firstOrNull { it.id==TrustedChannel.source(header) && it.active } ?: error("未知设备")
        val key=TrustedChannel.open(header,store.deviceId(),kind,trust.key)
        require(remember("route:"+BridgeProtocol.hex(header))) { "重复会话" }
        return SessionRoute(key,trust.id,input)
    }
    fun isTrusted(id: String) = store.records().any { it.id==id && it.active }
    fun decide(request: PairingRequest,accept: Boolean) { if(sessions.isCurrent(request.session,request) && System.currentTimeMillis()<request.expires) request.decision.complete(accept) }
    fun revoke(id: String) { synchronized(trustGate) { epochs[id]=(epochs[id] ?: 0)+1; store.remove(id) }; nearby.remove(id); pending.values.filter { it.id==id }.forEach { it.decision.complete(false) }; revoked?.invoke(id) }
    fun pairInvitation(invitation: PairingInvitation): Boolean {
        val trust=store.records().firstOrNull { it.id==invitation.deviceId && it.active }
        if(trust==null) {
            pair(NearbyDevice(invitation.deviceId,"扫描的电脑","windows",invitation.address,0,false))
            return true
        }
        workers.execute {
            try {
                send(publicBytes(),invitation.address)
                send(DiscoveryProtocol.encode(DiscoveryProtocol.create(store.deviceId(),store.name(),"android",trust.id,trust.key)),invitation.address)
                status("设备已配对，正在重新连接")
            } catch(e: Exception) { status(PairingErrors.connect(e)) }
        }
        return false
    }
    fun pairAddress(host: String) { require(BridgeProtocol.isLocal(host)) { "请输入局域网 IPv4 地址" }; pair(NearbyDevice("",host,"windows",host,0,false)) }
    fun pair(peer: NearbyDevice) {
        check(slots.tryAcquire()) { "已有配对请求，请稍后重试" }
        workers.execute {
            Socket().use { socket -> sockets.add(socket)
                try {
                    try { socket.connect(InetSocketAddress(peer.address,PairingProtocol.PORT),5000) }
                    catch(e: Exception) { status(PairingErrors.connect(e)); return@execute }
                    socket.soTimeout=120000; pairSession(socket,true,peer.id.takeIf(PairingProtocol::validId))
                }
                catch(_: Exception) { status("配对未完成，可能是对方取消或连接中断，请重试") }
                finally { sockets.remove(socket); slots.release() }
            }
        }
    }
    private fun acceptLoop() {
        while(running.get()) {
            try {
                val socket=listener.accept(); val host=socket.inetAddress.hostAddress ?: ""; val now=System.currentTimeMillis()
                rates.entries.removeIf { now-it.value>30000 }
                if(!BridgeProtocol.isLocal(host) || rates.size>=128 || now-(rates[host] ?: 0)<5000 || !slots.tryAcquire()) { socket.close(); continue }
                rates[host]=now; sockets.add(socket)
                workers.execute { socket.use { try { socket.soTimeout=120000; pairSession(socket,false,null) } catch(_: Exception) { status("一项配对已取消或超时") } finally { sockets.remove(socket); slots.release() } } }
            } catch(_: Exception) { if(!running.get()) return }
        }
    }
    private fun write(socket: Socket,value: JSONObject,key: ByteArray? = null) {
        val bytes=value.toString().toByteArray().let { if(key==null) it else BridgeProtocol.encrypt(key,it) }; require(bytes.size<=4096)
        DataOutputStream(socket.getOutputStream()).apply { writeInt(bytes.size); write(bytes); flush() }
    }
    private fun read(socket: Socket,key: ByteArray? = null): JSONObject {
        val input=DataInputStream(socket.getInputStream()); val length=input.readInt(); require(length in 2..4096)
        val bytes=ByteArray(length).also(input::readFully)
        return JSONObject((if(key==null) bytes else BridgeProtocol.decrypt(key,bytes)).toString(Charsets.UTF_8))
    }
    private fun pairSession(socket: Socket,initiator: Boolean,expected: String?) {
        var session=UUID.randomUUID().toString().replace("-",""); val host=socket.inetAddress.hostAddress ?: error("无效地址")
        var key: ByteArray?=null
        var reserved: String?=null; var epoch=0
        val sessionOwner=Any(); var sessionReserved=false; var published: PairingRequest?=null
        val startedNanos=System.nanoTime()
        val deadline=timer.schedule({ runCatching { socket.close() } },120,TimeUnit.SECONDS)
        PairingMaterial().use { material ->
            val role=if(initiator) "i" else "r"
            fun hello(type: String) = JSONObject().put("Type",type).put("Session",session).put("Id",store.deviceId()).put("Name",store.name()).put("Platform","android").put("Commit",PairingProtocol.commitment(session,role,store.deviceId(),store.name(),material.publicKey,material.nonce))
            fun reveal() = JSONObject().put("Type","reveal").put("Public",material.publicKey).put("Nonce",material.nonce)
            try {
                if(initiator) { check(sessions.reserve(session,sessionOwner)) { "重复的配对会话" }; sessionReserved=true; write(socket,hello("hello")) }
                val h=read(socket); require(h.getString("Type")==if(initiator) "commit" else "hello")
                if(initiator) require(h.getString("Session")==session) else session=h.getString("Session")
                val remote=h.getString("Id"); val name=h.getString("Name"); val platform=h.getString("Platform")
                require(PairingProtocol.validId(session) && PairingProtocol.validId(remote) && remote!=store.deviceId() && (expected==null || expected==remote) && name.length in 1..80 && platform in listOf("windows","android") && store.records().none { it.id==remote && it.active })
                if(!initiator) { check(sessions.reserve(session,sessionOwner)) { "重复的配对会话" }; sessionReserved=true }
                check(pairIds.add(remote)) { "该设备已有配对会话" }; reserved=remote; epoch=epochs.getOrPut(remote) { 0 }
                if(!initiator) write(socket,hello("commit"))
                if(initiator) write(socket,reveal())
                val r=read(socket); require(r.getString("Type")=="reveal"); val pub=r.getString("Public"); val nonce=r.getString("Nonce")
                PairingProtocol.verify(h.getString("Commit"),PairingProtocol.commitment(session,if(initiator) "r" else "i",remote,name,pub,nonce))
                if(!initiator) write(socket,reveal())
                val transcript=if(initiator) PairingProtocol.transcript(session,store.deviceId(),store.name(),material.publicKey,material.nonce,remote,name,pub,nonce) else PairingProtocol.transcript(session,remote,name,pub,nonce,store.deviceId(),store.name(),material.publicKey,material.nonce)
                key=PairingProtocol.sessionKey(material,pub,transcript)
                val request=PairingRequest(session,remote,name,host,PairingProtocol.code(key!!,transcript),System.currentTimeMillis()+100000); sessions.publish(session,sessionOwner,request); published=request
                status("有设备等待配对，请主动打开互传核对验证码")
                val remainingMillis=(120000-(System.nanoTime()-startedNanos)/1000000).coerceIn(1,100000)
                val accepted=try { request.decision.get(remainingMillis,TimeUnit.MILLISECONDS) } catch(_: Exception) { false }
                write(socket,JSONObject().put("Type","confirm").put("Session",session).put("Accept",accepted),key); check(accepted)
                val confirmed=read(socket,key); require(confirmed.getString("Type")=="confirm" && confirmed.getString("Session")==session && confirmed.getBoolean("Accept"))
                val pairing: ByteArray
                if(initiator) {
                    pairing=ByteArray(32).also(SecureRandom()::nextBytes); write(socket,JSONObject().put("Type","key").put("Key",Base64.getEncoder().encodeToString(pairing)),key)
                    require(read(socket,key).getString("Type")=="ready"); savePair(DeviceTrust(remote,name,platform,pairing,host,false),epoch)
                    write(socket,JSONObject().put("Type","commit"),key); require(read(socket,key).getString("Type")=="done")
                } else {
                    val v=read(socket,key); require(v.getString("Type")=="key"); pairing=Base64.getDecoder().decode(v.getString("Key")); require(pairing.size==32)
                    savePair(DeviceTrust(remote,name,platform,pairing,host,false),epoch); write(socket,JSONObject().put("Type","ready"),key)
                    require(read(socket,key).getString("Type")=="commit"); write(socket,JSONObject().put("Type","done"),key)
                }
                savePair(DeviceTrust(remote,name,platform,pairing,host,true),epoch); send(DiscoveryProtocol.encode(DiscoveryProtocol.create(store.deviceId(),store.name(),"android",remote,pairing)),host)
                status("设备已配对，正在认证连接")
            } finally { deadline.cancel(false); reserved?.let { pairIds.remove(it) }; if(sessionReserved) sessions.release(session,sessionOwner,published); key?.fill(0) }
        }
    }
    private fun savePair(record: DeviceTrust,epoch: Int) { synchronized(trustGate) { check(running.get() && (epochs[record.id] ?: 0)==epoch) { "配对已撤销" }; store.save(record) } }
    override fun close() { synchronized(trustGate) { running.set(false) }; udp.close(); listener.close(); pending.values.forEach { it.decision.complete(false) }; sockets.forEach { runCatching { it.close() } }; workers.shutdownNow(); timer.shutdownNow() }
}
