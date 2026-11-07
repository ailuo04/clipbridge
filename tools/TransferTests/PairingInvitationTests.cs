using System.IO;
using System.Net;
using System.Net.Sockets;
using ClipBridge.Windows;

internal static class PairingInvitationTests
{
    public static void Run()
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const string text = "clipbridge:pair:1:0123456789abcdef0123456789abcdef:10.51.2.197";
        Program.Check(PairingInvitationCodec.Format(id, IPAddress.Parse("10.51.2.197")) == text, "QR format contains only identity and address");
        var invitation = PairingInvitationCodec.Parse(text);
        Program.Check(invitation.DeviceId == id && invitation.Address.ToString() == "10.51.2.197", "QR preserves expected identity and local address");
        Program.Check(PairingErrors.Connect(new SocketException(10060)).Contains("连接") && !PairingErrors.Connect(new SocketException(10060)).Contains("验证码"), "network failure is not blamed on verification codes");
        byte[] png = PairingQr.RenderPng(text);
        Program.Check(png.Length > 100 && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "QR invitation renders as PNG");
        foreach (string invalid in new[] { "", new string('x', 200), $"clipbridge:pair:2:{id}:10.0.0.1", $"clipbridge:pair:1:{id.ToUpperInvariant()}:10.0.0.1", $"clipbridge:pair:1:bad:10.0.0.1", $"clipbridge:pair:1:{id}:127.0.0.1", $"clipbridge:pair:1:{id}:8.8.8.8", $"clipbridge:pair:1:{id}:224.0.0.1", $"clipbridge:pair:1:{id}:10.0.0.256", $"clipbridge:pair:1:{id}:010.0.0.1", $"clipbridge:pair:1:{id}:host.local", $"clipbridge:pair:1:{id}:10.0.0.1:45824" })
            Program.Rejects<InvalidDataException>(() => PairingInvitationCodec.Parse(invalid), "unsafe or malformed invitation rejected");
        Program.Rejects<InvalidDataException>(() => PairingInvitationCodec.Format(id, IPAddress.Loopback), "loopback address cannot be advertised");
    }
}