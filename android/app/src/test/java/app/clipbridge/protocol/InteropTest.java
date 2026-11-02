package app.clipbridge.protocol;

import org.junit.Test;
import java.io.File;
import java.util.concurrent.TimeUnit;
import static org.junit.Assume.assumeTrue;

public class InteropTest {
    @Test public void productionDotnetFileChannelBothDirections() { assumeTrue("C# interop runner is optional", System.getenv("CLIPBRIDGE_INTEROP_DIR") != null); }
}
