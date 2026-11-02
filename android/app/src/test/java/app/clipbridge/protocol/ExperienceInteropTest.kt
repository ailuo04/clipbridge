package app.clipbridge.protocol
import org.junit.Test
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.json.JSONObject
import java.io.*
import java.net.ServerSocket
import java.net.Socket
import java.nio.file.*
import java.security.MessageDigest
import java.util.Base64
import java.util.UUID
class ExperienceInteropTest {
    private fun write(s: Socket,j: JSONObject,key: ByteArray?=null) {
        val bytes=j.toString().toByteArray().let { if(key==null) it else BridgeProtocol.encrypt(key,it) }
        DataOutputStream(s.getOutputStream()).apply { writeInt(bytes.size); write(bytes); flush() }
    }
    private fun read(s: Socket,key: ByteArray?=null): JSONObject {
        val input=DataInputStream(s.getInputStream()); val n=input.readInt(); require(n in 2..262172)
        val bytes=ByteArray(n).also(input::readFully)
        return JSONObject((if(key==null) bytes else BridgeProtocol.decrypt(key,bytes)).toString(Charsets.UTF_8))
    }
    @Test fun realWindowsPairingAndV2ChannelsBothDirections() {
        val location=System.getenv("CLIPBRIDGE_EXPERIENCE_INTEROP_DIR"); assumeTrue(location!=null)
        val dir=Path.of(location!!); val config=JSONObject(String(Files.readAllBytes(dir.resolve("experience.json")),Charsets.UTF_8))
        val self="a".repeat(32); val remote=config.getString("id"); val session=UUID.randomUUID().toString().replace("-",""); val name="Interop Android"
        lateinit var pairing: ByteArray
        PairingMaterial().use { local -> Socket("127.0.0.1",config.getInt("pairPort")).use { s ->
            s.soTimeout=20000
            write(s,JSONObject().put("Type","hello").put("Session",session).put("Id",self).put("Name",name).put("Platform","android").put("Commit",PairingProtocol.commitment(session,"i",self,name,local.publicKey,local.nonce)))
            val h=read(s); assertEquals("commit",h.getString("Type"))
            write(s,JSONObject().put("Type","reveal").put("Public",local.publicKey).put("Nonce",local.nonce))
            val reveal=read(s); val pub=reveal.getString("Public"); val nonce=reveal.getString("Nonce")
            PairingProtocol.verify(h.getString("Commit"),PairingProtocol.commitment(session,"r",remote,h.getString("Name"),pub,nonce))
            val transcript=PairingProtocol.transcript(session,self,name,local.publicKey,local.nonce,remote,h.getString("Name"),pub,nonce)
            val key=PairingProtocol.sessionKey(local,pub,transcript)
            val code=dir.resolve("pair-code.txt"); repeat(500) { if(!Files.exists(code)) Thread.sleep(10) }
            assertEquals(String(Files.readAllBytes(code),Charsets.UTF_8),PairingProtocol.code(key,transcript)); Files.write(dir.resolve("approve-code"),"explicit synthetic confirmation".toByteArray())
            write(s,JSONObject().put("Type","confirm").put("Session",session).put("Accept",true),key)
            assertTrue(read(s,key).getBoolean("Accept"))
            pairing=ByteArray(32) { (it+91).toByte() }; write(s,JSONObject().put("Type","key").put("Key",Base64.getEncoder().encodeToString(pairing)),key)
            assertEquals("ready",read(s,key).getString("Type")); write(s,JSONObject().put("Type","commit"),key); assertEquals("done",read(s,key).getString("Type"))
        } }
        fun open(s: Socket,kind: Int): ByteArray { val header=TrustedChannel.header(self,remote,kind,pairing); s.getOutputStream().write(header); return TrustedChannel.open(header,remote,kind,pairing) }
        Socket("127.0.0.1",config.getInt("textPort")).use { s -> val key=open(s,1); BridgeProtocol.writeJson(s.getOutputStream(),key,JSONObject().put("Type","clipboard").put("MessageId",UUID.randomUUID().toString()).put("OriginDeviceId",self).put("Text","JVM → Windows v2 中文🙂").put("TimestampUtc",System.currentTimeMillis())) }
        Socket("127.0.0.1",config.getInt("filePort")).use { s ->
            s.soTimeout=20000; val key=open(s,2); val id=UUID.randomUUID().toString().replace("-",""); val data="JVM v2 file 中文🙂".toByteArray()
            val file=FileItem("来自手机v2.txt",data.size.toLong(),BridgeProtocol.hex(MessageDigest.getInstance("SHA-256").digest(data)))
            BridgeProtocol.writeJson(s.getOutputStream(),key,Offer(id,self,System.currentTimeMillis(),listOf(file)).json())
            FileProtocol.requireReply(read(s,key),id,"accept"); BridgeProtocol.write(s.getOutputStream(),key,FileProtocol.chunk(id,0,0,data)); BridgeProtocol.writeJson(s.getOutputStream(),key,FileProtocol.reply("finish",id)); FileProtocol.requireReply(read(s,key),id,"complete")
        }
        ServerSocket(0).use { textServer -> ServerSocket(0).use { fileServer ->
            textServer.soTimeout=20000; fileServer.soTimeout=20000
            Socket("127.0.0.1",config.getInt("controlPort")).use { s -> DataOutputStream(s.getOutputStream()).apply { writeInt(textServer.localPort); writeInt(fileServer.localPort); flush() } }
            fun incoming(s: Socket,kind: Int): ByteArray { val h=ByteArray(65).also { DataInputStream(s.getInputStream()).readFully(it) }; assertEquals(remote,TrustedChannel.source(h)); return TrustedChannel.open(h,self,kind,pairing) }
            textServer.accept().use { s -> s.soTimeout=20000; val key=incoming(s,1); assertEquals("Windows → JVM v2 中文🙂",read(s,key).getString("Text")) }
            fileServer.accept().use { s ->
                s.soTimeout=20000; val key=incoming(s,2); val offer=FileProtocol.parseOffer(read(s,key)); assertEquals(remote,offer.origin)
                BridgeProtocol.writeJson(s.getOutputStream(),key,FileProtocol.reply("accept",offer.id)); val bytes=ByteArrayOutputStream()
                while(true) { val plain=BridgeProtocol.read(s.getInputStream(),key,FileProtocol.MAX_CONTROL); if(plain[0].toInt()==1) { val chunk=FileProtocol.parseChunk(offer.id,plain); assertEquals(bytes.size().toLong(),chunk.offset); bytes.write(chunk.data) } else { FileProtocol.requireReply(JSONObject(plain.toString(Charsets.UTF_8)),offer.id,"finish"); break } }
                assertEquals("Windows v2 file 中文🙂",bytes.toString("UTF-8")); assertEquals(offer.files[0].sha256.lowercase(),BridgeProtocol.hex(MessageDigest.getInstance("SHA-256").digest(bytes.toByteArray())))
                BridgeProtocol.writeJson(s.getOutputStream(),key,FileProtocol.reply("complete",offer.id))
            }
        } }
    }
}
