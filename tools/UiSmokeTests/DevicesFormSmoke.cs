using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// DispatchProxy keeps the synthetic store in this assembly; no real DPAPI files are read.
public class MemoryDeviceStoreProxy : DispatchProxy
{
    public Type RecordType = null!;
    public readonly List<object> Items = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_DeviceId": return new string('a', 32);
            case "get_Name": return "UI smoke computer";
            case "Records":
                Array records = Array.CreateInstance(RecordType, Items.Count);
                for (int i = 0; i < Items.Count; i++) records.SetValue(Items[i], i);
                return records;
            case "Save": Items.Add(args![0]!); return null;
            case "Remove": Items.RemoveAll(record => (string)RecordType.GetProperty("Id")!.GetValue(record)! == (string)args![0]!); return null;
            default: throw new InvalidOperationException(method.Name);
        }
    }
}

internal static class DevicesFormSmoke
{
    private static void Pump(int ms = 120)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL devices: " + message);
        Console.WriteLine("PASS devices: " + message);
    }
    private static Control Find(Form form, string name) => form.Controls.Find(name, true).Single();
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    private static void CheckStack(Form form)
    {
        var scroll = (ScrollableControl)Find(form, "DevicesScroll");
        var rows = Find(form, "DevicesContent").Controls.Cast<Control>().Where(control => control.Visible).ToArray();
        for (int i = 1; i < rows.Length; i++)
            Check(rows[i - 1].Bottom <= rows[i].Top, $"sections do not overlap: {rows[i - 1].Name} / {rows[i].Name}");
        Check(Find(form, "TrustedDevices").Height > 0, "trusted devices remain visible after resizing");
    }
    private static void Capture(Form form, string name)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke");
        System.IO.Directory.CreateDirectory(directory);
        Control workspace = Find(form, "DevicesWorkspace");
        using var bitmap = new Bitmap(workspace.Width, workspace.Height);
        workspace.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        string path = System.IO.Path.Combine(directory, name + ".png");
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine("SCREENSHOT " + path);
    }
    internal static void Run(Assembly app, bool capture)
    {
        PairingQrPanelSmoke.Run(app, capture);
        Type hubType = app.GetType("ClipBridge.Windows.DeviceHub")!;
        Type storeType = app.GetType("ClipBridge.Windows.IDeviceStore")!;
        Type trustType = app.GetType("ClipBridge.Windows.DeviceTrust")!;
        Type peerType = app.GetType("ClipBridge.Windows.NearbyDevice")!;
        Type requestType = app.GetType("ClipBridge.Windows.PairingRequest")!;
        Type formType = app.GetType("ClipBridge.Windows.DevicesForm")!;
        Type uiType = app.GetType("ClipBridge.Windows.Ui")!;
        Type colorType = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        void Theme(string value) => uiType.GetMethod("ApplyTheme", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { Enum.Parse(colorType, value) });
        object store = DispatchProxy.Create(storeType, typeof(MemoryDeviceStoreProxy));
        var memory = (MemoryDeviceStoreProxy)store;
        memory.RecordType = trustType;
        string phone = new string('b', 32), computer = new string('c', 32), nearbyId = new string('d', 32);
        object Trust(string id, string name, string platform) => Activator.CreateInstance(trustType, id, name, platform, new byte[32], "192.168.1.20", true)!;
        memory.Items.Add(Trust(phone, "PLK110", "android"));
        memory.Items.Add(Trust(computer, "工作电脑", "windows"));
        using IDisposable hub = (IDisposable)Activator.CreateInstance(hubType, store, new byte[32], 0, false)!;
        object nearby = hubType.GetField("Nearby")!.GetValue(hub)!;
        void Peer(string id, string name, string platform, bool authenticated)
        {
            object peer = Activator.CreateInstance(peerType, id, name, platform, "192.168.1.21", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), authenticated, 1, 1L)!;
            nearby.GetType().GetProperty("Item")!.SetValue(nearby, peer, new object[] { id });
        }
        Peer(computer, "工作电脑", "windows", true);
        Peer(nearbyId, "Pixel 9", "android", false);
        Theme("Light");
        using Form form = (Form)Activator.CreateInstance(formType, hub)!;
        void Refresh() { formType.GetMethod("RefreshRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null); Pump(); }
        form.Show(); Pump(220);
        Size initialClientSize = form.ClientSize;
        Check(!Descendants(form).OfType<Button>().Any(button => button.Name == "ShowPairingQrButton"), "QR is displayed inline without a separate display button");
        Check(Descendants(form).OfType<PictureBox>().Any(image => image.Name == "PairingQrImage" && image.Visible), "QR area is visible immediately after opening");
        Check(Descendants(form).Where(control => control.GetType().Name == "IconTile").All(control => control.BackColor.A == 255), "device icons paint against an opaque themed surface");
        Check(!Descendants(form).OfType<TextBox>().Single().Visible, "manual IP input is hidden by default");
        Button? manualToggle = Descendants(form).OfType<Button>().FirstOrDefault(button => button.Name == "ManualPairingToggle");
        Check(manualToggle != null && manualToggle.Text.Contains("备用"), "IP pairing is explicitly marked as a backup option");
        manualToggle!.PerformClick(); Pump();
        var input = Descendants(form).OfType<TextBox>().Single();
        input.Text = "192.168.1.88";
        input.Focus(); input.Select(4, 3);
        Peer(new string('e', 32), "另一台平板", "android", false);
        Refresh();
        Check(ReferenceEquals(input, Descendants(form).OfType<TextBox>().Single()) && input.Text == "192.168.1.88", "discovery refresh preserves the manual input control and its text");
        Check(input.Focused && input.SelectionStart == 4 && input.SelectionLength == 3, "discovery refresh preserves keyboard focus and selection");
        input.Text = "8.8.8.8";
        ((Button)Find(form, "ManualPairingButton")).PerformClick(); Pump();
        Check(Find(form, "DevicesStatus").Text.Contains("局域网 IPv4"), "manual pairing rejects a public IP without opening a connection");
        input.Text = "192.168.1.88";
        manualToggle.PerformClick(); Pump();
        Check(!input.Visible, "manual input folds away");
        Check(Descendants(form).Any(control => control.Text.Contains("已连接")) && Descendants(form).Any(control => control.Text.Contains("离线")), "online and offline devices have distinct states");
        Peer(nearbyId, "已改名的手机", "android", false); Refresh();
        Check(Descendants(form).Any(control => control.Text == "已改名的手机"), "device names update without an address or authentication change");
        Peer(nearbyId, new string('长', 80), "android", false); Refresh();
        foreach (Size size in new[] { new Size(640, 640), new Size(500, 520), new Size(980, 740) })
        {
            form.ClientSize = size; Pump();
            var scroll = (ScrollableControl)Find(form, "DevicesScroll");
            Check(!scroll.HorizontalScroll.Visible, $"no horizontal scrollbar with long names at {form.ClientSize.Width}px");
            var qrImage = (PictureBox)Find(form, "PairingQrImage");
            Check(qrImage.Width == qrImage.Height && qrImage.Left >= 0 && qrImage.Right <= qrImage.Parent!.ClientSize.Width, "inline QR remains square and inside its panel");
            foreach (Button button in Descendants(form).OfType<Button>().Where(button => button.Visible))
                Check(button.Left >= 0 && button.Right <= button.Parent!.ClientSize.Width, $"action fits its row: {button.Name}");
            if (capture && size.Width == 500) Capture(form, "devices-minimum");
        }
        form.ClientSize = initialClientSize; Pump();
        var viewport = (ScrollableControl)Find(form, "DevicesScroll");
        viewport.AutoScrollPosition = new Point(0, 160); Pump();
        int previousScroll = viewport.AutoScrollPosition.Y;
        Peer(nearbyId, "Pixel 9", "android", false); Refresh();
        Check(viewport.AutoScrollPosition.Y == previousScroll, "device refresh retains the scroll position");
        viewport.AutoScrollPosition = Point.Empty; Pump();
        Action<string>? statusHandler = (Action<string>?)hubType.GetField("Status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub);
        Task.Run(() => statusHandler?.Invoke("后台配对状态测试")).GetAwaiter().GetResult(); Pump();
        Check(Find(form, "DevicesStatus").Text == "后台配对状态测试", "background hub status is marshalled to the UI");
        ((Button)Find(form, "RefreshDiscoveryButton")).PerformClick(); Pump();
        CheckStack(form);
        if (capture) Capture(form, "devices-light");
        manualToggle.PerformClick(); Pump();
        if (capture) Capture(form, "devices-manual");
        input.Focus(); input.Select(3, 2);
        Theme("Dark"); formType.GetMethod("ApplyTheme")!.Invoke(form, null); Pump();
        Check(ReferenceEquals(input, Descendants(form).OfType<TextBox>().Single()) && input.Text == "192.168.1.88" && input.SelectionStart == 3, "theme switching retains the IP field and selection");
        Color ink = (Color)uiType.GetField("Ink", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(input.ForeColor == ink && form.BackColor == (Color)uiType.GetField("Canvas", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, "dark palette reaches the window and native input");
        manualToggle.PerformClick(); Pump();
        if (capture) Capture(form, "devices-dark");
        object pending = hubType.GetProperty("Pending")!.GetValue(hub)!;
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string session = new string('f', 32);
        object request = Activator.CreateInstance(requestType, session, nearbyId, "Pixel 9", "192.168.1.21", "123456", DateTimeOffset.UtcNow.AddSeconds(60), decision)!;
        pending.GetType().GetProperty("Item")!.SetValue(pending, request, new object[] { session }); Refresh();
        var approve = (Button)Find(form, "ConfirmPairing-" + session);
        Control[] requestStatuses = Descendants(form).Where(control => control.Name == "PairingRequestStatusCapsule").ToArray();
        Check(requestStatuses.Length == 1, "pending request has one status capsule");
        Control requestStatus = requestStatuses.Single();
        Check(requestStatus.AccessibleDescription!.Contains("待确认") && requestStatus.AccessibleDescription.Contains("Pixel 9"), "pending capsule identifies state and device");
        Check(approve.Enabled && Descendants(form).Any(control => control.Text.Replace(" ", "") == "123456"), "pending request displays its code and confirmation action");
        CheckStack(form);
        if (capture) Capture(form, "devices-request");
        decision.TrySetResult(true); Refresh();
        Check(!Descendants(form).OfType<Button>().Any(button => button.Name == "ConfirmPairing-" + session && button.Enabled), "confirmed request cannot be approved twice");
        requestStatus = Descendants(form).Single(control => control.Name == "PairingRequestStatusCapsule");
        Check(requestStatus.AccessibleDescription!.Contains("本机已确认") && requestStatus.AccessibleDescription.Contains("等待另一台设备"), "accepted request does not claim pairing is complete");
        var expiredDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        request = Activator.CreateInstance(requestType, session, nearbyId, "Pixel 9", "192.168.1.21", "654321", DateTimeOffset.UtcNow.AddMilliseconds(180), expiredDecision)!;
        pending.GetType().GetProperty("Item")!.SetValue(pending, request, new object[] { session }); Refresh(); Pump(1100);
        Check(!Descendants(form).OfType<Button>().Any(button => button.Name == "ConfirmPairing-" + session && button.Enabled), "confirmation expires even when no discovery snapshot changes");
        requestStatus = Descendants(form).Single(control => control.Name == "PairingRequestStatusCapsule");
        Check(requestStatus.AccessibleDescription!.Contains("请求已过期"), "expired request is clearly labeled in its capsule");
        Theme("Light"); formType.GetMethod("ApplyTheme")!.Invoke(form, null); Pump();
        if (capture) Capture(form, "devices-request-light");
        Theme("Dark"); formType.GetMethod("ApplyTheme")!.Invoke(form, null); Pump();
        ((Button)Find(form, "RevokeDevice-" + phone)).PerformClick(); Pump(1100);
        Check(!memory.Items.Any(record => (string)trustType.GetProperty("Id")!.GetValue(record)! == phone), "cancel pairing removes the chosen device from the synthetic store");
        pending.GetType().GetMethod("Clear")!.Invoke(pending, null);
        memory.Items.Clear(); nearby.GetType().GetMethod("Clear")!.Invoke(nearby, null); Refresh();
        Theme("Light"); formType.GetMethod("ApplyTheme")!.Invoke(form, null); Pump();
        Check(Descendants(form).Any(control => control.Text.Contains("还没有配对设备")) && Descendants(form).Any(control => control.Text.Contains("正在搜索")), "empty lists explain pairing and discovery");
        if (capture) Capture(form, "devices-empty");
        form.Close(); Pump();
        var remainingStatusHandlers = (Action<string>?)hubType.GetField("Status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub);
        Check(remainingStatusHandlers == null || remainingStatusHandlers.GetInvocationList().All(handler => !ReferenceEquals(handler.Target, form)), "closing unsubscribes hub status notifications");
        Console.WriteLine("DEVICE WINDOW TESTS PASSED");
    }
}
