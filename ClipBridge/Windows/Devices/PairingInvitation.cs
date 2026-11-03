using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ClipBridge.Windows;

internal sealed record PairingInvitation(string DeviceId, IPAddress Address);

internal static class PairingInvitationCodec
{
    private const string Prefix = "clipbridge:pair:1:";

    public static string Format(string deviceId, IPAddress address)
    {
        if (!PairingProtocol.ValidId(deviceId) || !IsSafeAddress(address))
            throw new InvalidDataException("无效的配对二维码");
        return Prefix + deviceId + ":" + address;
    }

    public static PairingInvitation Parse(string value)
    {
        if (value == null || value.Length > 96 || !value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidDataException("无法识别此配对二维码");
        string[] parts = value.Substring(Prefix.Length).Split(':');
        if (parts.Length != 2 || !PairingProtocol.ValidId(parts[0]) ||
            !IPAddress.TryParse(parts[1], out var address) || !IsSafeAddress(address) ||
            !string.Equals(address.ToString(), parts[1], StringComparison.Ordinal))
            throw new InvalidDataException("无效的配对二维码");
        return new PairingInvitation(parts[0], address);
    }

    private static bool IsSafeAddress(IPAddress address) => address != null &&
        address.AddressFamily == AddressFamily.InterNetwork &&
        !IPAddress.IsLoopback(address) && SharedProtocol.IsLocalAddress(address);
}