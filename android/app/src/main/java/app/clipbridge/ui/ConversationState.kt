package app.clipbridge.ui

/** Main-thread conversation state; callbacks always address the original peer. */
class ConversationState<T> {
    val texts=mutableMapOf<String,String>()
    val attachments=mutableMapOf<String,MutableList<T>>()
    private val sending=mutableSetOf<String>()
    fun begin(peer:String)=sending.add(peer)
    fun isSending(peer:String)=peer in sending
    fun textSent(peer:String,text:String){if(texts[peer]==text)texts[peer]=""}
    fun complete(peer:String,items:List<T>,success:Boolean){if(success)attachments[peer]?.removeAll(items.toSet());sending.remove(peer)}
    fun forget(peer:String){texts.remove(peer);attachments.remove(peer)}
}
