package app.clipbridge.protocol

import org.json.JSONObject
import java.util.concurrent.atomic.AtomicReference

data class ClipboardReadResult(val status:String,val text:String="",val timestampUtc:Long=0,val messageId:String="")
class ClipboardSyncState {
    private val target=AtomicReference<String?>(null)
    private val suppressed=AtomicReference<String?>(null)
    private val lastSent=AtomicReference<String?>(null)
    val targetPeerId:String? get()=target.get()
    fun enable(peerId:String) { require(peerId.isNotBlank()); lastSent.set(null); target.set(peerId) }
    fun disable() { target.set(null); lastSent.set(null) }
    fun suppressIncoming(text:String) { suppressed.set(text) }
    fun isSuppressed(text:String):Boolean { val value=suppressed.get() ?: return false; if(value==text) return true; suppressed.compareAndSet(value,null); return false }
    fun shouldSend(text:String):Boolean {
        if(target.get()==null || text.isEmpty() || text.toByteArray().size>BridgeProtocol.MAX_TEXT) return false
        while(true) { val prior=lastSent.get(); if(prior==text) return false; if(lastSent.compareAndSet(prior,text)) return true }
    }
}
object ClipboardSyncProtocol {
    fun request(id:String,origin:String,target:String,now:Long)=JSONObject().put("Type","clipboardRequest").put("MessageId",id).put("OriginDeviceId",origin).put("TargetDeviceId",target).put("TimestampUtc",now)
    fun response(id:String,origin:String,result:ClipboardReadResult,now:Long)=JSONObject().put("Type","clipboardResponse").put("MessageId",id).put("OriginDeviceId",origin).put("TimestampUtc",now).put("Status",result.status).put("Text",if(result.status=="ok") result.text else "")
    fun readRequest(value:JSONObject,expectedPeerId:String,localId:String):String {
        val id=value.optString("MessageId"); val origin=value.optString("OriginDeviceId")
        require(value.optString("Type")=="clipboardRequest" && id.length in 1..64 && origin==expectedPeerId && value.optString("TargetDeviceId")==localId && BridgeProtocol.fresh(value.optLong("TimestampUtc",0)))
        return id
    }
    fun readResponse(value:JSONObject,expectedId:String,expectedPeerId:String):ClipboardReadResult {
        require(value.optString("Type")=="clipboardResponse" && value.optString("MessageId")==expectedId && value.optString("OriginDeviceId")==expectedPeerId && BridgeProtocol.fresh(value.optLong("TimestampUtc",0)))
        val status=value.optString("Status"); val text=value.optString("Text")
        require(status in setOf("ok","empty","foreground","disabled","error") && if(status=="ok") text.isNotEmpty() && text.toByteArray().size<=BridgeProtocol.MAX_TEXT else text.isEmpty())
        return ClipboardReadResult(status,text,value.optLong("TimestampUtc",0),expectedId)
    }
}
