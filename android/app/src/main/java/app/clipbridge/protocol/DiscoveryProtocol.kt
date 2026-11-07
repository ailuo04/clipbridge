package app.clipbridge.protocol
import org.json.JSONObject
import java.security.MessageDigest
import java.util.UUID
data class DiscoveryPacket(val type: String,val id: String,val name: String,val platform: String,val port: Int,val time: Long,val nonce: String,val target: String = "",val mac: String = "",val historySyncVersion: Int = 0)
object DiscoveryProtocol {
    const val SUPPORTED_HISTORY_SYNC_VERSION = 1
    private val prefix = "CBD2".toByteArray(Charsets.US_ASCII)
    fun isPublic(data: ByteArray) = data.size >= 4 && data.copyOfRange(0,4).contentEquals(prefix)
    fun encode(p: DiscoveryPacket): ByteArray = prefix + JSONObject().put("Type",p.type).put("Id",p.id).put("Name",p.name).put("Platform",p.platform).put("Port",p.port).put("Time",p.time).put("Nonce",p.nonce).put("Target",p.target).put("Mac",p.mac).put("historySyncVersion",p.historySyncVersion).toString().toByteArray()
    fun create(id: String,name: String,platform: String,target: String = "",key: ByteArray? = null): DiscoveryPacket {
        val p = DiscoveryPacket(if (target.isEmpty()) "discover" else "presence",id,name,platform,PairingProtocol.PORT,System.currentTimeMillis(),UUID.randomUUID().toString().replace("-",""),target,historySyncVersion = SUPPORTED_HISTORY_SYNC_VERSION)
        return if (key == null) p else p.copy(mac = sign(p,key))
    }
    fun parse(data: ByteArray): DiscoveryPacket {
        require(data.size <= 2048 && isPublic(data))
        val j = JSONObject(data.copyOfRange(4,data.size).toString(Charsets.UTF_8))
        val p = DiscoveryPacket(j.getString("Type"),j.getString("Id"),j.getString("Name"),j.getString("Platform"),j.getInt("Port"),j.getLong("Time"),j.getString("Nonce"),j.getString("Target"),j.getString("Mac"),j.optInt("historySyncVersion",0))
        require(p.type in listOf("discover","presence") && PairingProtocol.validId(p.id) && p.name.length in 1..80 && p.name.none { it.isISOControl() } && p.platform in listOf("windows","android") && p.historySyncVersion >= 0 && p.port == PairingProtocol.PORT && BridgeProtocol.fresh(p.time) && PairingProtocol.validId(p.nonce))
        require(if (p.type == "presence") PairingProtocol.validId(p.target) && p.mac.length == 64 else p.target.isEmpty() && p.mac.isEmpty())
        return p
    }
    private fun sign(p: DiscoveryPacket,key: ByteArray) = BridgeProtocol.hex(PairingProtocol.hmac(key,PairingProtocol.fields("ClipBridge/presence/v2",p.id,p.target,p.name,p.platform,p.port.toString(),p.time.toString(),p.nonce)))
    fun authenticate(p: DiscoveryPacket,self: String,key: ByteArray) = p.type == "presence" && p.target == self && MessageDigest.isEqual(p.mac.toByteArray(),sign(p,key).toByteArray())
}
