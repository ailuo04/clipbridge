package app.clipbridge.protocol;

import org.json.JSONObject;
import org.junit.Test;
import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.List;
import java.util.UUID;
import static org.junit.Assert.*;
import static org.junit.Assume.assumeTrue;

public class DirectoryInteropTest {
    private static final BridgeProtocol BRIDGE=BridgeProtocol.INSTANCE;
    private static final FileProtocol PROTOCOL=FileProtocol.INSTANCE;
    private static JSONObject read(Socket socket,byte[] key)throws Exception {return new JSONObject(new String(BRIDGE.read(socket.getInputStream(),key,FileProtocol.MAX_CONTROL),StandardCharsets.UTF_8));}
    @Test public void encryptedFoldersRoundtripWithDotnet()throws Exception {
        String directory=System.getenv("CLIPBRIDGE_FOLDER_INTEROP_DIR");assumeTrue(directory!=null);
        JSONObject config=new JSONObject(new String(Files.readAllBytes(Path.of(directory,"server.json")),StandardCharsets.UTF_8));
        byte[] pairing=new byte[32];for(int i=0;i<32;i++)pairing[i]=(byte)i;byte[] key=PROTOCOL.deriveKey(pairing);
        byte[] bytes="目录原文 Kotlin → C#".getBytes(StandardCharsets.UTF_8);String hash=BRIDGE.hex(MessageDigest.getInstance("SHA-256").digest(bytes));String empty=FileProtocol.EMPTY_HASH;
        String id=UUID.randomUUID().toString().replace("-","");
        Offer sent=new Offer(id,"jvm",System.currentTimeMillis(),List.of(new FileItem("手机目录",0,empty,"手机目录",true),new FileItem("空目录",0,empty,"手机目录/空目录",true),new FileItem("原文.txt",bytes.length,hash,"手机目录/原文.txt",false)));
        try(Socket socket=new Socket("127.0.0.1",config.getInt("receivePort"))){socket.setSoTimeout(15000);BRIDGE.writeJson(socket.getOutputStream(),key,sent.json());PROTOCOL.requireReply(read(socket,key),id,"accept");BRIDGE.write(socket.getOutputStream(),key,PROTOCOL.chunk(id,2,0,bytes));BRIDGE.writeJson(socket.getOutputStream(),key,PROTOCOL.reply("finish",id));PROTOCOL.requireReply(read(socket,key),id,"complete");}
        try(ServerSocket listener=new ServerSocket(0)){listener.setSoTimeout(20000);try(Socket control=new Socket("127.0.0.1",config.getInt("controlPort"))){new DataOutputStream(control.getOutputStream()).writeInt(listener.getLocalPort());}
            try(Socket socket=listener.accept()){socket.setSoTimeout(15000);Offer received=PROTOCOL.parseOffer(read(socket,key));assertEquals(3,received.getFiles().size());assertEquals("电脑目录",received.getFiles().get(0).getRelativePath());assertTrue(received.getFiles().stream().anyMatch(f->f.isDirectory()&&f.getRelativePath().equals("电脑目录/空目录")));ByteArrayOutputStream[] output=new ByteArrayOutputStream[received.getFiles().size()];for(int i=0;i<output.length;i++)output[i]=new ByteArrayOutputStream();BRIDGE.writeJson(socket.getOutputStream(),key,PROTOCOL.reply("accept",received.getId()));while(true){byte[] plain=BRIDGE.read(socket.getInputStream(),key,FileProtocol.MAX_CONTROL);if(plain[0]==1){Chunk chunk=PROTOCOL.parseChunk(received.getId(),plain);assertEquals(output[chunk.getIndex()].size(),chunk.getOffset());output[chunk.getIndex()].write(chunk.getData());}else{PROTOCOL.requireReply(new JSONObject(new String(plain,StandardCharsets.UTF_8)),received.getId(),"finish");for(int i=0;i<output.length;i++){FileItem f=received.getFiles().get(i);assertEquals(f.getSize(),output[i].size());assertEquals(f.getSha256().toLowerCase(),BRIDGE.hex(MessageDigest.getInstance("SHA-256").digest(output[i].toByteArray())).toLowerCase());if(!f.isDirectory())assertEquals("目录原文 C# → Kotlin",output[i].toString(StandardCharsets.UTF_8));}BRIDGE.writeJson(socket.getOutputStream(),key,PROTOCOL.reply("complete",received.getId()));break;}}}
        }
    }
}
