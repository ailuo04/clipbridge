using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

internal static class PairingQrPanelSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo { public int Size; public NativeRect Item; public NativeRect Button; public uint ButtonState; public IntPtr Combo; public IntPtr ItemWindow; public IntPtr ListWindow; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboBoxInfo info);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    private static void Pump()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL inline QR: " + message);
        Console.WriteLine("PASS inline QR: " + message);
    }
    private static string Hash(Image image)
    {
        using var buffer = new MemoryStream();
        image.Save(buffer, ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }
    internal static void Run(Assembly app, bool capture)
    {
        Type panelType = app.GetType("ClipBridge.Windows.PairingQrPanel")!;
        Type addressType = app.GetType("ClipBridge.Windows.PairingNetworkAddress")!;
        Type arrayType = addressType.MakeArrayType();
        Type delegateType = typeof(Func<>).MakeGenericType(arrayType);
        Array options = Array.CreateInstance(addressType, 0);
        bool unavailable = false;
        Func<Array> source = () => unavailable ? throw new NetworkInformationException() : options;
        Delegate provider = Expression.Lambda(delegateType, Expression.Convert(Expression.Invoke(Expression.Constant(source)), arrayType)).Compile();
        object Address(string adapter, string ip, bool gateway) => Activator.CreateInstance(addressType, adapter, IPAddress.Parse(ip), gateway)!;
        Array Addresses(params object[] entries)
        {
            var result = Array.CreateInstance(addressType, entries.Length);
            for (int i = 0; i < entries.Length; i++) result.SetValue(entries[i], i);
            return result;
        }
        Type uiType = app.GetType("ClipBridge.Windows.Ui")!;
        Type themeType = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        void Theme(string value) => uiType.GetMethod("ApplyTheme", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { Enum.Parse(themeType, value) });
        Theme("Light");
        using var host = new Form { ClientSize = new Size(640, 300), StartPosition = FormStartPosition.CenterScreen, Text = "ClipBridge QR smoke" };
        var panel = (Panel)Activator.CreateInstance(panelType, "0123456789abcdef0123456789abcdef", provider)!;
        panel.Dock = DockStyle.Fill;
        host.Controls.Add(panel);
        host.Show(); Pump();
        void Refresh() { panelType.GetMethod("RefreshAddresses")!.Invoke(panel, null); Pump(); }
        var image = (PictureBox)panel.Controls.Find("PairingQrImage", true).Single();
        var selector = (ComboBox)panel.Controls.Find("PairingQrAddresses", true).Single();
        Check(selector.DrawMode == DrawMode.OwnerDrawFixed && selector.FlatStyle == FlatStyle.Flat, "network address selector uses the app's flat themed field style");
        MethodInfo drawAddress = panelType.GetMethod("DrawAddress", BindingFlags.Static | BindingFlags.NonPublic)!;
        Control status = panel.Controls.Find("PairingQrStatus", true).Single();
        Refresh();
        Check(image.Image == null && !selector.Enabled && status.Text.Contains("连接"), "opening offline gives an explicit recovery message and no stale QR");
        options = Addresses(Address("VPN", "10.1.0.9", false), Address("WLAN", "10.51.2.197", true), Address("Public", "8.8.8.8", true), Address("Loopback", "127.0.0.1", true));
        Refresh();
        Check(selector.Items.Count == 2 && selector.SelectedItem!.ToString()!.StartsWith("10.51.2.197"), "only local addresses appear and the network with a gateway is selected");
        Color selectedFill = (Color)uiType.GetField("AccentSoftSolid", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using (var rendered = new Bitmap(200, selector.ItemHeight))
        using (Graphics graphics = Graphics.FromImage(rendered))
        {
            var args = new DrawItemEventArgs(graphics, selector.Font, new Rectangle(0, 0, rendered.Width, rendered.Height), selector.SelectedIndex, DrawItemState.Selected);
            drawAddress.Invoke(null, new object[] { selector, args });
            Check(rendered.GetPixel(rendered.Width - 2, rendered.Height / 2).ToArgb() == selectedFill.ToArgb(), "selected network address uses the application accent instead of system blue");
        }
        Check(image.Image != null && !panel.Controls.Find("PairingQrUnavailable", true).Single().Visible, "a usable network automatically renders a QR");
        selector.DroppedDown = true; Pump();
        var comboInfo = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        bool hasRoundedPopup = false;
        if (GetComboBoxInfo(selector.Handle, ref comboInfo) && comboInfo.ListWindow != IntPtr.Zero)
        {
            IntPtr probe = CreateRectRgn(0, 0, 1, 1);
            try { hasRoundedPopup = GetWindowRgn(comboInfo.ListWindow, probe) != 0; }
            finally { DeleteObject(probe); }
        }
        selector.DroppedDown = false; Pump();
        Check(hasRoundedPopup, "network address dropdown clips to a rounded app-style popup");
        Image original = image.Image!;
        string originalHash = Hash(original);
        Refresh();
        Check(ReferenceEquals(image.Image, original), "unchanged networks keep the same bitmap");
        if (capture)
        {
            string path = Path.Combine(Path.GetTempPath(), "clipbridge-ui-smoke", "inline-qr-rendered.png");
            using var rendered = new Bitmap(image.Width, image.Height);
            image.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            rendered.Save(path, ImageFormat.Png);
            Console.WriteLine("SCREENSHOT " + path);
        }
        selector.SelectedIndex = 1; Pump();
        Check(Hash(image.Image!) != originalHash, "choosing a different local address updates the QR");
        Image selected = image.Image!;
        options = Addresses(Address("New Ethernet", "192.168.1.40", true), Address("WLAN", "10.51.2.197", true), Address("VPN", "10.1.0.9", false));
        Refresh();
        Check(selector.SelectedItem!.ToString()!.StartsWith("10.1.0.9") && ReferenceEquals(image.Image, selected), "a network refresh preserves the chosen address and bitmap");
        Theme("Dark"); panelType.GetMethod("ApplyTheme")!.Invoke(panel, null); Pump();
        Check(image.BackColor == Color.White && ((Bitmap)image.Image!).GetPixel(0, 0).ToArgb() == Color.White.ToArgb() && ReferenceEquals(image.Image, selected), "dark theme keeps the QR white, scannable, and unchanged");
        options = Addresses(Address("WLAN", "10.51.2.197", true)); Refresh();
        Check(selector.SelectedItem!.ToString()!.StartsWith("10.51.2.197") && Hash(image.Image!) == originalHash, "losing the selected network switches to a remaining usable address");
        unavailable = true; Refresh();
        Check(image.Image == null && !selector.Enabled && status.Text.Contains("网络"), "network enumeration failure clears the QR and explains recovery");
        unavailable = false; Refresh();
        Check(image.Image != null && selector.Enabled, "network recovery restores the QR automatically");
        options = Addresses(); Refresh();
        Check(image.Image == null && !selector.Enabled, "disconnecting clears the previous QR");
        host.Close();
        Theme("Light");
        Console.WriteLine("INLINE QR TESTS PASSED");
    }
}
