package app.clipbridge.network

import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketTimeoutException

object PairingErrors {
    fun connect(error: Exception): String = when (error) {
        is SecurityException -> "本地网络权限被拒绝，请在手机应用权限中允许后重试"
        is SocketTimeoutException -> "连接超时，请确认两台设备在可互访的同一网络"
        is ConnectException, is NoRouteToHostException -> "无法连接对方，请确认对方已打开互传且网络允许互访"
        else -> "无法建立连接，请检查网络和互传状态后重试"
    }
}