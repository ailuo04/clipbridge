using QRCoder;

namespace ClipBridge.Windows;

internal static class PairingQr
{
    public static byte[] RenderPng(string invitation)
    {
        _ = PairingInvitationCodec.Parse(invitation);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(invitation, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(8);
    }
}