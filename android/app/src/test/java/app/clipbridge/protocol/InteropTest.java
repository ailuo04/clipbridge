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

public class InteropTest {
    private static final BridgeProtocol BRIDGE = BridgeProtocol.INSTANCE;
    private static final FileProtocol FILES = FileProtocol.INSTANCE;
    private static JSONObject read(Socket socket, byte[] key) throws Exception {
        return new JSONObject(new String(BRIDGE.read(socket.getInputStream(), key, FileProtocol.MAX_CONTROL), StandardCharsets.UTF_8));
    }
    @Test public void productionDotnetFileChannelBothDirections() throws Exception {
        String directory = System.getenv("CLIPBRIDGE_INTEROP_DIR");
        assumeTrue("Run TransferTests --interop and set CLIPBRIDGE_INTEROP_DIR", directory != null);
        JSONObject config = new JSONObject(new String(Files.readAllBytes(Path.of(directory, "server.json")), StandardCharsets.UTF_8));
        byte[] pairing = new byte[32]; for (int i = 0; i < pairing.length; i++) pairing[i] = (byte)i;
        byte[] key = FILES.deriveKey(pairing);
        String id = UUID.randomUUID().toString().replace("-", "");
        byte[] data = "Kotlin → C# 中文🙂".getBytes(StandardCharsets.UTF_8);
        FileItem item = new FileItem("来自手机.txt", data.length, BRIDGE.hex(MessageDigest.getInstance("SHA-256").digest(data)));
        try (Socket socket = new Socket("127.0.0.1", config.getInt("receivePort"))) {
            socket.setSoTimeout(15000);
            Offer offer = new Offer(id, "jvm-test", System.currentTimeMillis(), List.of(item));
            BRIDGE.writeJson(socket.getOutputStream(), key, offer.json());
            FILES.requireReply(read(socket, key), id, "accept");
            BRIDGE.write(socket.getOutputStream(), key, FILES.chunk(id, 0, 0, data));
            BRIDGE.writeJson(socket.getOutputStream(), key, FILES.reply("finish", id));
            FILES.requireReply(read(socket, key), id, "complete");
        }
        try (ServerSocket listener = new ServerSocket(0)) {
            listener.setSoTimeout(20000);
            try (Socket control = new Socket("127.0.0.1", config.getInt("controlPort"))) {
                DataOutputStream output = new DataOutputStream(control.getOutputStream()); output.writeInt(listener.getLocalPort()); output.flush();
            }
            try (Socket socket = listener.accept()) {
                socket.setSoTimeout(15000);
                Offer offer = FILES.parseOffer(read(socket, key));
                assertEquals(2, offer.getFiles().size());
                BRIDGE.writeJson(socket.getOutputStream(), key, FILES.reply("accept", offer.getId()));
                ByteArrayOutputStream[] contents = {new ByteArrayOutputStream(), new ByteArrayOutputStream()};
                while (true) {
                    byte[] plain = BRIDGE.read(socket.getInputStream(), key, FileProtocol.MAX_CONTROL);
                    if (plain[0] == 1) {
                        Chunk chunk = FILES.parseChunk(offer.getId(), plain);
                        assertEquals(contents[chunk.getIndex()].size(), chunk.getOffset());
                        contents[chunk.getIndex()].write(chunk.getData());
                    } else {
                        FILES.requireReply(new JSONObject(new String(plain, StandardCharsets.UTF_8)), offer.getId(), "finish");
                        for (int i = 0; i < contents.length; i++) {
                            FileItem file = offer.getFiles().get(i);
                            assertEquals(file.getSize(), contents[i].size());
                            assertTrue(file.getSha256().equalsIgnoreCase(BRIDGE.hex(MessageDigest.getInstance("SHA-256").digest(contents[i].toByteArray()))));
                        }
                        assertEquals("C# → Kotlin 中文🙂", contents[0].toString(StandardCharsets.UTF_8));
                        assertEquals(0, contents[1].size());
                        BRIDGE.writeJson(socket.getOutputStream(), key, FILES.reply("complete", offer.getId())); break;
                    }
                }
            }
        }
    }
}
