package app.clipbridge.protocol

import app.clipbridge.network.PairingErrors
import java.net.ConnectException
import java.net.SocketTimeoutException
import org.junit.Assert.*
import org.junit.Test

class PairingErrorTest {
    @Test fun networkFailuresDoNotBlameTheVerificationCode() {
        val timeout=PairingErrors.connect(SocketTimeoutException())
        val refused=PairingErrors.connect(ConnectException())
        val blocked=PairingErrors.connect(SecurityException())
        assertTrue(timeout.contains("超时")); assertFalse(timeout.contains("验证码"))
        assertTrue(refused.contains("连接")); assertFalse(refused.contains("验证码"))
        assertTrue(blocked.contains("权限")); assertFalse(blocked.contains("验证码"))
    }
}