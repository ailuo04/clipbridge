package app.clipbridge.protocol

data class PairingInvitation(val deviceId: String, val address: String)

object PairingInvitationCodec {
    private const val PREFIX = "clipbridge:pair:1:"

    fun parse(value: String): PairingInvitation {
        require(value.length <= 96 && value.startsWith(PREFIX)) { "无法识别此配对二维码" }
        val parts = value.removePrefix(PREFIX).split(':')
        require(parts.size == 2 && PairingProtocol.validId(parts[0])) { "无效的设备标识" }
        val address = parts[1]
        val octets = address.split('.')
        require(octets.size == 4 && octets.all { octet ->
            octet.isNotEmpty() && (octet.length == 1 || !octet.startsWith('0')) &&
                octet.all { it in '0'..'9' } && octet.toIntOrNull() in 0..255
        } && !address.startsWith("127.") && BridgeProtocol.isLocal(address)) { "二维码地址不是有效的局域网地址" }
        return PairingInvitation(parts[0], address)
    }
}