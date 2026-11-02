package app.clipbridge.storage

import app.clipbridge.protocol.PairingProtocol
import org.json.JSONArray
import org.json.JSONObject
import java.util.Base64

data class DeviceTrust(val id: String,val name: String,val platform: String,val key: ByteArray,val address: String,val active: Boolean = true) {
    fun json() = JSONObject().put("id",id).put("name",name).put("platform",platform).put("key",Base64.getEncoder().encodeToString(key)).put("address",address).put("active",active)
    companion object {
        fun parse(j: JSONObject): DeviceTrust = DeviceTrust(j.getString("id"),j.getString("name"),j.getString("platform"),Base64.getDecoder().decode(j.getString("key")),j.getString("address"),j.getBoolean("active")).also { require(PairingProtocol.validId(it.id) && it.key.size == 32) }
    }
}
