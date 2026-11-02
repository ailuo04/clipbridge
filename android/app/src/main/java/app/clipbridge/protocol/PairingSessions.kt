package app.clipbridge.protocol
import java.util.concurrent.ConcurrentHashMap
/** Wire session IDs do not authorize replacing a live request or approving another request instance. */
class PairingSessions<T: Any> {
    private val owners=ConcurrentHashMap<String,Any>()
    val pending=ConcurrentHashMap<String,T>()
    fun reserve(session: String,owner: Any) = PairingProtocol.validId(session) && owners.putIfAbsent(session,owner)==null
    fun publish(session: String,owner: Any,request: T) { check(owners[session]===owner && pending.putIfAbsent(session,request)==null) { "重复的配对会话" } }
    fun isCurrent(session: String,request: T) = pending[session]===request
    fun release(session: String,owner: Any,request: T?) {
        if(owners[session]!==owner) return
        if(request!=null) pending.remove(session,request)
        owners.remove(session,owner)
    }
}
