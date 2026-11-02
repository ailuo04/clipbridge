package app.clipbridge.protocol;

import org.junit.Test;
import java.util.Arrays;
import static org.junit.Assert.*;

public class BridgeProtocolTest {
    private final byte[] pairing = new byte[32];
    { for (int i = 0; i < pairing.length; i++) pairing[i] = (byte)i; }
    @Test public void windowsCryptoVector() {
        BridgeProtocol bridge = BridgeProtocol.INSTANCE;
        assertEquals("d9202a642d557b019c9620bd24781958a89e4a95543e9352576a9839fb72871b", bridge.hex(bridge.deriveKey(pairing)));
        byte[] frame = bridge.unhex("000102030405060708090a0ba2863c8171fd10de14a29c87777e13be29dfaa28530c92ee0aef");
        assertEquals("中文🙂", new String(bridge.decrypt(bridge.deriveKey(pairing), frame), java.nio.charset.StandardCharsets.UTF_8));
        frame[frame.length - 1] ^= 1;
        try { bridge.decrypt(bridge.deriveKey(pairing), frame); fail("tampering accepted"); } catch (Exception expected) { }
    }
    @Test public void rejectUnsafeNamesAndSizes() {
        FileProtocol files = FileProtocol.INSTANCE;
        for (String name : new String[]{"../x", "a/b", "a\\b", "CON", "NUL.txt", "x.", "a:b"}) {
            try { files.validateName(name); fail("unsafe name accepted: " + name); } catch (Exception expected) { }
        }
        files.validateName("照片🙂.txt");
        assertFalse(BridgeProtocol.INSTANCE.isLocal("8.8.8.8"));
        assertTrue(BridgeProtocol.INSTANCE.isLocal("192.168.1.2"));
        assertFalse(BridgeProtocol.INSTANCE.isLocal("192.168.1"));
    }
    @Test public void fileChunksAndKeySeparation() {
        FileProtocol files = FileProtocol.INSTANCE;
        String id = "00112233445566778899aabbccddeeff";
        byte[] data = "文件".getBytes(java.nio.charset.StandardCharsets.UTF_8);
        Chunk chunk = files.parseChunk(id, files.chunk(id, 3, 77, data));
        assertEquals(3, chunk.getIndex()); assertEquals(77, chunk.getOffset()); assertArrayEquals(data, chunk.getData());
        assertFalse(Arrays.equals(BridgeProtocol.INSTANCE.deriveKey(pairing), files.deriveKey(pairing)));
    }
}
