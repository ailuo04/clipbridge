package app.clipbridge.protocol

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class DirectoryProtocolTest {
    private fun offer(path:String="素材"):JSONObject = JSONObject().put("Type","fileTreeOffer").put("TransferId","a".repeat(32)).put("OriginDeviceId","source").put("TimestampUtc",System.currentTimeMillis()).put("Files",org.json.JSONArray().put(JSONObject().put("Name","素材").put("Size",0).put("Sha256","e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855").put("RelativePath",path).put("IsDirectory",true)))
    @Test fun directoryOfferRetainsTreeMetadata() { val decoded=FileProtocol.parseOffer(offer()); assertTrue(decoded.files.first().json().getBoolean("IsDirectory")); assertEquals("素材",decoded.files.first().json().getString("RelativePath")) }
    @Test fun traversalIsRejected() { for(path in listOf("../x","/x","a/../x","a\\x","a//x")) { try { FileProtocol.parseOffer(offer(path)); fail(path) } catch(_:IllegalArgumentException) {} } }
}
