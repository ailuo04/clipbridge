using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

internal static class UpdateDialogSmoke
{
    private static void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL " + description); Console.WriteLine("PASS " + description); }
    private static void Pump(int milliseconds = 100) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static object Prop(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;

    public static void Run(Assembly app, bool capture)
    {
        Type? type = app.GetType("ClipBridge.Windows.UpdateDialog");
        Check(type != null, "dedicated update dialog exists");
        Type ui = app.GetType("ClipBridge.Windows.Ui")!;
        Type theme = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        foreach (string mode in new[] { "Light", "Dark" })
        {
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, mode) });
            Color canvas = (Color)ui.GetField("Canvas", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Color surface = (Color)ui.GetField("Surface", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Color ink = (Color)ui.GetField("Ink", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Color muted = (Color)ui.GetField("Muted", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            using Form owner = new() { Text = "ClipBridge test workspace", ClientSize = new Size(1180, 800), BackColor = canvas, ForeColor = ink, StartPosition = FormStartPosition.Manual };
            owner.Location = new Point(30, 30);
            owner.Controls.Add(new Label { Text = "ClipBridge", Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold), ForeColor = ink, Location = new Point(34, 24), AutoSize = true });
            owner.Controls.Add(new Label { Text = "网络与安全                                                                 设备", Font = new Font("Microsoft YaHei UI", 10f), ForeColor = muted, Location = new Point(40, 117), AutoSize = true });
            owner.Controls.Add(new Panel { BackColor = surface, Location = new Point(28, 96), Size = new Size(340, 520) });
            owner.Controls.Add(new Panel { BackColor = surface, Location = new Point(384, 96), Size = new Size(760, 520) });
            owner.Controls.Add(new Label { Text = "等待设备连接", Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), ForeColor = ink, Location = new Point(610, 300), AutoSize = true });
            owner.Controls.Add(new Label { Text = "同一局域网内的设备会自动出现在这里", Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = muted, Location = new Point(586, 344), AutoSize = true });
            owner.Controls.Add(new Panel { BackColor = surface, Location = new Point(40, 600), Size = new Size(1120, 150) });
            owner.Show(); Pump(120);

            using Form dialog = (Form)Activator.CreateInstance(type!, new object[] { "0.5.7", "更新界面更轻更流畅\n改进设备连接与传输稳定性\n修复若干已知问题", 12L * 1024 * 1024, false })!;
            Control Find(string name) => dialog.Controls.Find(name, true)[0];
            dialog.Show(owner); Pump(180);
            Control card = Find("UpdateCard");
            Control backdrop = Find("UpdateBackdrop");
            Check(backdrop.GetType().Name == "ModalBackdropSurface", mode + " update dialog uses the shared modal backdrop surface");
            Check(dialog.Bounds == owner.RectangleToScreen(owner.ClientRectangle), mode + " update overlay covers the whole ClipBridge window");
            Check(Math.Abs(card.Left + card.Width / 2 - dialog.ClientSize.Width / 2) <= 2 && Math.Abs(card.Top + card.Height / 2 - dialog.ClientSize.Height / 2) <= 2, mode + " glass card is centred inside the software window");
            Check((int)Prop(backdrop, "Builds") == 1, mode + " workspace backdrop is captured and blurred once");
            using (var blurred = new Bitmap((Bitmap)Field(backdrop, "_blurred")!))
            {
                var levels = new System.Collections.Generic.HashSet<int>();
                double sum = 0, sumSquares = 0; int samples = 0, minAlpha = 255;
                for (int y = 0; y < blurred.Height; y += 9)
                for (int x = 0; x < blurred.Width; x += 9)
                {
                    Color pixel = blurred.GetPixel(x, y);
                    minAlpha = Math.Min(minAlpha, pixel.A);
                    double luminance = pixel.R * .299 + pixel.G * .587 + pixel.B * .114;
                    levels.Add((int)luminance / 10);
                    sum += luminance; sumSquares += luminance * luminance; samples++;
                }
                double deviation = Math.Sqrt(Math.Max(0, sumSquares / samples - Math.Pow(sum / samples, 2)));
                Check(levels.Count >= 4 && deviation > 3.5, mode + " frosted backdrop keeps the workspace's tonal structure (" + levels.Count + " bands, sigma " + deviation.ToString("0.0") + ")");
                Check(minAlpha == 255, mode + " captured backdrop remains opaque at rounded card corners");
            }
            Rectangle cardSource = dialog.RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
            Bitmap capturedBackdrop = (Bitmap)Prop(backdrop, "Image");
            Bitmap cardChrome = (Bitmap)Field(card, "_chrome")!;
            Check(cardChrome.GetPixel(5, 5).ToArgb() == capturedBackdrop.GetPixel(cardSource.X + 5, cardSource.Y + 5).ToArgb(), mode + " glass card samples the backdrop in owner-client coordinates");
            Check(card.GetType().GetMethod("ConfigureAvailable", BindingFlags.Instance | BindingFlags.NonPublic) != null && dialog.Controls.Find("UpdateNotes", true).Length == 0, mode + " update content uses one owner-drawn glass surface instead of stacked cards");
            Check(dialog.AccessibleName == "软件更新" && dialog.AccessibleDescription!.Contains("0.5.7") && dialog.AccessibleDescription.Contains("12.0 MB"), mode + " compact dialog exposes version and size to assistive tech");
            Check(!(bool)Prop(card, "ProgressVisible") && dialog.Height > 500, mode + " ready dialog hides inactive progress without an empty block");
            Check(dialog.FormBorderStyle == FormBorderStyle.None, mode + " updater uses a custom overlay frame");
            Check(Find("UpdateCloseButton").AccessibleName == "关闭更新窗口", "custom close remains accessible");
            Check(ReferenceEquals(Find("UpdateInstallButton").Parent, card) && ReferenceEquals(Find("UpdateCancelButton").Parent, card) && ReferenceEquals(Find("UpdateCloseButton").Parent, card), mode + " update actions paint over the glass surface instead of the flat form backdrop");
            int bakedChrome = (int)Prop(card, "ChromeBuilds");
            Check(bakedChrome >= 1, mode + " glass chrome is baked before first paint (" + bakedChrome + " builds)");
            Check(Field(card, "_backdrop") != null, mode + " glass card itself composites the frosted workspace");
            for (int i = 0; i < 12; i++) dialog.Invalidate(true); Pump(15);
            int stableChrome = (int)Prop(card, "ChromeBuilds");
            Check((int)Prop(backdrop, "Builds") == 1 && stableChrome == bakedChrome, mode + " repeated repaint reuses cached glass and blurred backdrop");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-ready");

            Check(Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, mode + " confirmation actions are available");
            int installs = 0, cancels = 0;
            type!.GetEvent("InstallRequested")!.AddEventHandler(dialog, new Action(() => installs++));
            type.GetEvent("CancelRequested")!.AddEventHandler(dialog, new Action(() => cancels++));
            ((Button)Find("UpdateInstallButton")).PerformClick(); Check(installs == 1, "install action is connected");
            type.GetMethod("BeginDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Check((bool)Prop(card, "ProgressVisible") && dialog.Height > 500, mode + " download reveals progress without resizing the window");
            Check(!Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, "download permits cancellation but prevents duplicate install");
            Check((int)Prop(card, "AnimationInterval") >= 24, "progress animation is throttled instead of repainting at 50 fps");
            Check((int)Prop(card, "ChromeBuilds") == stableChrome + 1, mode + " beginning progress rebuilds static chrome only once");

            ((Button)Find("UpdateCloseButton")).PerformClick(); Pump();
            Check(cancels == 1 && dialog.Visible && !dialog.IsDisposed, "custom close requests cancellation during download");
            type.GetMethod("ShowFailure")!.Invoke(dialog, new object[] { "测试：网络连接中断，请重试。" });
            Check(Find("UpdateInstallButton").Enabled && (string)Prop(card, "DetailsText") == "测试：网络连接中断，请重试。", "failure retains readable reason and enables retry");
            Check(dialog.AccessibleDescription!.Contains("当前版本仍可使用") && ((string)Prop(card, "TitleText")).Contains("再试一次"), "failure keeps status and next step distinct");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-error");

            type.GetMethod("BeginDownload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Type progress = app.GetType("ClipBridge.Windows.UpdateProgress")!;
            Type stage = app.GetType("ClipBridge.Windows.UpdateStage")!;
            object value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Downloading"), 6L * 1024 * 1024, 12L * 1024 * 1024, 2.0 * 1024 * 1024 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump(1200);
            int chromeBefore = (int)Prop(card, "ChromeBuilds");
            Type cardType = card.GetType();
            Size percentSize = (Size)cardType.GetProperty("PercentSize", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!;
            Check(percentSize.Width > 20 && percentSize.Height > 16, "percent typography is measured for its true bounds");
            Check((string)Prop(card, "PercentText") == "40%" && ((string)Prop(card, "DetailsText")).Contains("MB"), "download displays honest overall percentage and byte details");
            Check(dialog.AccessibleDescription!.Contains("40%"), "download percentage is exposed in accessibility text");
            for (int i = 0; i < 8; i++) { type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump(20); }
            Check((int)Prop(card, "ChromeBuilds") == chromeBefore, "progress updates never rebuild the glass chrome");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant());

            Type cardT = card.GetType();
            cardT.GetMethod("ConfigureAvailable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, new object[] { "0.5.7", string.Join("\n", Enumerable.Range(1, 40).Select(index => "更新内容 " + index + "：支持长说明阅读与键盘滚动。")), 12L * 1024 * 1024 });
            Pump();
            cardT.GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, new object[] { new KeyEventArgs(Keys.End) });
            Check((double)Field(card, "_noteOffset")! > 0, "long release notes scroll to their end");
            cardT.GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, new object[] { new KeyEventArgs(Keys.Home) });
            Check((double)Field(card, "_noteOffset")! == 0, "release notes can return to their beginning");
            cardT.GetMethod("ConfigureAvailable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, new object[] { "0.5.7", "更新界面更轻更流畅\n改进设备连接与传输稳定性\n修复若干已知问题", 12L * 1024 * 1024 }); Pump();

            IntPtr originalHandle = dialog.Handle;
            string otherMode = mode == "Light" ? "Dark" : "Light";
            int backdropBeforeTheme = (int)Prop(backdrop, "Builds");
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, otherMode) });
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null); Pump();
            Check(dialog.Handle == originalHandle && (string)Prop(card, "PercentText") == "", "theme change preserves the overlay and completed card state");
            Check((int)Prop(backdrop, "Builds") == backdropBeforeTheme + 1, mode + " theme change refreshes the blurred owner image");
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, mode) });
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null); Pump();
            Check((int)Prop(backdrop, "Builds") == backdropBeforeTheme + 2, mode + " restoring the theme refreshes the backdrop again");
            dialog.Scale(new SizeF(1.5f, 1.5f)); type.GetMethod("LayoutOverlay", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)!.Invoke(dialog, new object[] { true }); Pump();
            Check(Math.Abs(card.Left + card.Width / 2 - dialog.ClientSize.Width / 2) <= 3 && Math.Abs(card.Top + card.Height / 2 - dialog.ClientSize.Height / 2) <= 3, "scaled overlay keeps the card centred");
            bool contained = true;
            foreach (string name in new[] { "UpdateCard", "UpdateInstallButton", "UpdateCancelButton", "UpdateCloseButton" })
            {
                Control current = Find(name);
                contained &= dialog.ClientRectangle.Contains(current.Bounds) && current.Parent!.ClientRectangle.Contains(current.Bounds);
                Check(dialog.ClientRectangle.Contains(current.Bounds), mode + " " + name + " remains inside the software window");
            }
            if (capture && mode == "Light") Save(dialog, "update-light-scaled");

            value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Installing"), 1L, 2L, 0.0 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value });
            dialog.Close(); Pump();
            Check(!dialog.IsDisposed && dialog.Visible && !Find("UpdateCancelButton").Enabled && !Find("UpdateCloseButton").Enabled, "file replacement cannot be interrupted by closing the window");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-installing");
            value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Completed"), 1L, 1L, 0.0 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump();
            Check((string)Prop(card, "PercentText") == "100%" && dialog.AccessibleDescription!.Contains("新版已经打开") && Find("UpdateCloseButton").Enabled && Find("UpdateCancelButton").Enabled, "confirmed completion has a finished title and dismissible actions");
            type.GetMethod("ShowFailure")!.Invoke(dialog, new object[] { "测试：已恢复原版本。" });
            ((Button)Find("UpdateCloseButton")).PerformClick(); Pump();
            Check(dialog.IsDisposed, "custom close dismisses an idle or failed update");

            using (Form helper = (Form)Activator.CreateInstance(type!, new object[] { "", "更新文件已校验。\n正在安装，完成后将自动打开。", 0L, true })!)
            {
                Control H(string name) => helper.Controls.Find(name, true)[0];
                helper.Show(); Pump(160);
                Check((string)Prop(H("UpdateCard"), "PercentText") == "90%" && !H("UpdateCloseButton").Enabled && !H("UpdateCancelButton").Enabled, "independent installer starts in a protected waiting state");
                Check(H("UpdateCard").Left + H("UpdateCard").Width / 2 == helper.ClientSize.Width / 2 && H("UpdateBackdrop").Visible, "standalone installer is centred over its own backdrop");
                object installing = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Installing"), 1L, 2L, 0.0 })!;
                type.GetMethod("Report")!.Invoke(helper, new[] { installing }); Pump();
                if (capture) Save(helper, "update-" + mode.ToLowerInvariant() + "-installer");
                type.GetMethod("ShowFailure")!.Invoke(helper, new object[] { "更新未完成，请重新打开应用。" });
                Check(!H("UpdateInstallButton").Visible == false && H("UpdateCloseButton").Enabled && ((string)Prop(H("UpdateCard"), "TitleText")).Contains("重新打开"), "installer failure leads to reopening rather than an unavailable retry");
                Check(helper.AccessibleDescription!.Contains("安装未完成") && helper.AccessibleDescription.Contains("原版本已尝试恢复"), "installer status and recovery state are distinct");
                ((Button)H("UpdateCloseButton")).PerformClick(); Pump();
            }

            MethodInfo? noticeFactory = type.GetMethod("CreateNotice", BindingFlags.Static | BindingFlags.NonPublic);
            Check(noticeFactory != null, "short update notices have a dedicated compact layout");
            foreach (bool failed in new[] { false, true })
            {
                using Form notice = (Form)noticeFactory!.Invoke(null, new object[] { failed ? "更新检查未完成" : "已是最新版本", failed ? "网络连接暂时不可用，请稍后重试。" : "当前已是最新版本，无需安装更新。", failed, "软件更新" })!;
                notice.Show(); Pump(160);
                Control N(string name) => notice.Controls.Find(name, true)[0];
                Check(notice.Height < 500 && !(bool)Prop(N("UpdateCard"), "ProgressVisible") && !N("UpdateCancelButton").Visible, "notice avoids an empty progress area and extra action");
                Check(N("UpdateInstallButton").Text == "知道了" && notice.AccessibleDescription!.Contains(failed ? "更新检查未完成" : "已是最新版本"), "compact notice preserves its status conclusion and dismiss action");
                Check(failed ? ((string)Prop(N("UpdateCard"), "DetailsText")).Contains("网络连接") : !(bool)Prop(N("UpdateCard"), "ProgressVisible"), failed ? "failure notice retains unique diagnostic detail" : "latest notice omits duplicate result copy");
                notice.Scale(new SizeF(1.5f, 1.5f)); notice.GetType().GetMethod("LayoutOverlay", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)!.Invoke(notice, new object[] { true }); Pump();
                Check(notice.ClientRectangle.Contains(N("UpdateCard").Bounds) && N("UpdateInstallButton").Bounds.Width > 0, "scaled notice keeps its card and action inside the window");
                if (capture) Save(notice, "update-" + mode.ToLowerInvariant() + (failed ? "-check-error" : "-latest") + "-scaled");
                ((Button)N("UpdateInstallButton")).PerformClick(); Pump(); Check(notice.IsDisposed, "notice can be dismissed from the primary action");
            }
            Type programType = app.GetType("ClipBridge.Windows.Program")!;
            MethodInfo? startupFactory = programType.GetMethod("CreateStartupFailureNotice", BindingFlags.Static | BindingFlags.NonPublic);
            Check(startupFactory != null, "startup failures have a testable themed notice factory");
            using Form startupNotice = (Form)startupFactory!.Invoke(null, new object[] { new IOException("do not expose this detail") })!;
            Check(startupNotice.AccessibleDescription!.Contains("应用启动") && startupNotice.AccessibleDescription.Contains("启动失败"), "startup failure uses the compact error surface");
            string startupDetails = (string)Prop(startupNotice.Controls.Find("UpdateCard", true).Single(), "DetailsText");
            Check(startupDetails.Contains("IOException") && !startupDetails.Contains("do not expose"), "startup notice shows exception type without leaking exception text");
        }
    }

    private static void Save(Form dialog, string name)
    {
        string directory = Path.Combine(Path.GetTempPath(), "clipbridge-ui-smoke"); Directory.CreateDirectory(directory);
        using var image = new Bitmap(dialog.Width, dialog.Height);
        try
        {
            dialog.Activate(); Pump(60);
            using var graphics = Graphics.FromImage(image);
            graphics.CopyFromScreen(dialog.PointToScreen(Point.Empty), Point.Empty, dialog.Size, CopyPixelOperation.SourceCopy);
        }
        catch (Exception)
        {
            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        }
        string target = Path.Combine(directory, name + ".png"); image.Save(target, ImageFormat.Png); Console.WriteLine("SCREENSHOT " + target);
    }
}
