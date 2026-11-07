using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class UpdateDialogSmoke
{
    private static void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL " + description); Console.WriteLine("PASS " + description); }
    private static void Pump(int milliseconds = 100) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void CheckGlassStatusCapsule(Assembly app)
    {
        Type? discoveredCapsuleType = app.GetType("ClipBridge.Windows.GlassStatusCapsule");
        Check(discoveredCapsuleType != null, "glass status capsule exists");
        if (discoveredCapsuleType == null) return;
        Type capsuleType = discoveredCapsuleType;
        Type kindType = app.GetType("ClipBridge.Windows.GlassStatusKind")!;
        MethodInfo setContent = capsuleType.GetMethod("SetContent")!;
        Type ui = app.GetType("ClipBridge.Windows.Ui")!;
        Type theme = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        string[][] samples =
        {
            new[] { "Info", "软件更新", "发现可用更新", "v0.5.5" },
            new[] { "Success", "检查完成", "已是最新版本", "v0.5.4" },
            new[] { "Warning", "待确认", "确认设备身份", "Pixel 9" },
            new[] { "Error", "检查失败", "暂时无法连接", "" },
            new[] { "Progress", "正在更新", "正在下载更新", "40%" }
        };
        foreach (string mode in new[] { "Light", "Dark" })
        {
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, mode) });
            foreach (string[] sample in samples)
            {
                using Control capsule = (Control)Activator.CreateInstance(capsuleType)!;
                capsule.Size = new Size(480, 76);
                setContent.Invoke(capsule, new object[] { Enum.Parse(kindType, sample[0]), sample[1], sample[2], sample[3] });
                Check(capsule.AccessibleRole == AccessibleRole.StatusBar, mode + " " + sample[0] + " capsule has a status role");
                Check(capsule.AccessibleName == "状态提示" && capsule.AccessibleDescription!.Contains(sample[2]), mode + " " + sample[0] + " capsule exposes its status and conclusion");
                using var image = new Bitmap(capsule.Width, capsule.Height);
                capsule.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            }
        }
    }
    public static void Run(Assembly app, bool capture)
    {
        CheckGlassStatusCapsule(app);
        Type? type = app.GetType("ClipBridge.Windows.UpdateDialog");
        Check(type != null, "dedicated glass update dialog exists");
        Type ui = app.GetType("ClipBridge.Windows.Ui")!;
        Type theme = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        foreach (string mode in new[] { "Light", "Dark" })
        {
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, mode) });
            using Form dialog = (Form)Activator.CreateInstance(type!, new object[] { "0.5.2", "改进更新体验\n下载进度、签名校验、自动启动。", 12L * 1024 * 1024, false })!;
            Control Find(string name) => dialog.Controls.Find(name, true)[0];
            dialog.Show(); Pump();
            Control? updateCapsule = dialog.Controls.Find("UpdateStatusCapsule", true).SingleOrDefault();
            Check(updateCapsule != null, mode + " update window has one status capsule");
            if (updateCapsule == null) return;
            Check(updateCapsule.AccessibleDescription!.Contains("0.5.2") && updateCapsule.AccessibleDescription.Contains("12.0 MB"), mode + " capsule carries version and package size");
            Check(!dialog.Controls.Find("UpdateProgressSurface", true).Single().Visible, mode + " ready dialog hides inactive progress" );
            Check(dialog.FormBorderStyle == FormBorderStyle.None, mode + " updater uses a custom window frame");
            Check(Find("UpdateCloseButton").AccessibleName == "关闭更新窗口", "custom caption remains accessible");
            Check(Find("UpdateNotes") is not TextBoxBase, "release notes have no native textbox or scrollbar chrome");
            Check(dialog.Region != null && !dialog.Region.IsVisible(0, 0) && dialog.Region.IsVisible(dialog.Width / 2, 5), "window silhouette has rounded corners");
            Check(Find("UpdateNotes").Text.Contains("改进更新体验"), mode + " release notes are visible");
            Control note = Find("UpdateNotes"); string originalNotes = note.Text;
            note.Text = string.Join("\n", System.Linq.Enumerable.Range(1, 40).Select(index => "更新内容 " + index + "：支持长说明阅读与键盘滚动。")); Pump();
            note.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(note, new object[] { new KeyEventArgs(Keys.End) });
            Check((int)note.GetType().GetField("_offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(note)! > 0, "long release notes support keyboard scrolling");
            note.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(note, new object[] { new KeyEventArgs(Keys.Home) });
            Check((int)note.GetType().GetField("_offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(note)! == 0, "release notes can return to their beginning");
            note.Text = originalNotes; Pump();
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-ready");
            Control metadata = Find("UpdateStatusCapsule");
            bool insideAncestors = true;
            for (Control current = metadata; current.Parent != null; current = current.Parent) insideAncestors &= current.Parent.ClientRectangle.Contains(current.Bounds);
            Check(insideAncestors && metadata.Visible && metadata.Width >= 400 && metadata.AccessibleDescription!.Contains("0.5.2") && metadata.AccessibleDescription.Contains("12.0 MB"), mode + " capsule keeps version and package details accessible without clipping the container");
            Check(Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, mode + " confirmation actions are available");
            int installs = 0, cancels = 0;
            type!.GetEvent("InstallRequested")!.AddEventHandler(dialog, new Action(() => installs++));
            type.GetEvent("CancelRequested")!.AddEventHandler(dialog, new Action(() => cancels++));
            ((Button)Find("UpdateInstallButton")).PerformClick(); Check(installs == 1, "install action is connected");
            type.GetMethod("BeginDownload")!.Invoke(dialog, null);
            Check(Find("UpdateProgressSurface").Visible, mode + " download reveals its progress surface");
            Check(!Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, "download permits cancellation but prevents duplicate install");
            ((Button)Find("UpdateCloseButton")).PerformClick(); Pump();
            Check(cancels == 1 && dialog.Visible && !dialog.IsDisposed, "custom close requests cancellation during download");
            type.GetMethod("ShowFailure")!.Invoke(dialog, new object[] { "测试：网络连接中断，请重试。" });
            Check(Find("UpdateInstallButton").Enabled && Find("UpdateDetails").Text.Contains("网络连接中断"), "failure retains readable reason and enables retry");
            Check(Find("UpdateStatusCapsule").AccessibleDescription!.Contains("当前版本仍可使用") && !Find("UpdateStatusCapsule").AccessibleDescription!.Contains("可以再试一次"), "failure capsule does not repeat the retry phase");
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null);
            Color danger = (Color)ui.GetField("Danger", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(Find("UpdatePhase").ForeColor == danger, "theme refresh retains the failure state color");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-error");
            type.GetMethod("BeginDownload")!.Invoke(dialog, null);
            Type progress = app.GetType("ClipBridge.Windows.UpdateProgress")!;
            Type stage = app.GetType("ClipBridge.Windows.UpdateStage")!;
            object value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Downloading"), 6L * 1024 * 1024, 12L * 1024 * 1024, 2.0 * 1024 * 1024 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump(400);
            foreach (string textName in new[] { "UpdatePercent" })
            {
                Control text = Find(textName);
                Size measured = TextRenderer.MeasureText(textName == "UpdatePercent" ? "100%" : text.Text, text.Font);
                Check(text.Height >= measured.Height && text.Width >= measured.Width, textName + " typography fits without clipping");
            }
            Check(Find("UpdatePercent").Text == "40%" && Find("UpdateDetails").Text.Contains("MB"), "download displays honest overall percentage and byte details");
            Check(Find("UpdateStatusCapsule").AccessibleDescription!.Contains("40%"), "download percentage is also visible in the status capsule");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant());
            IntPtr originalHandle = dialog.Handle;
            string otherMode = mode == "Light" ? "Dark" : "Light";
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, otherMode) });
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null); Pump();
            Check(dialog.Handle == originalHandle && Find("UpdatePercent").Text == "40%", "theme change preserves the update window and progress");
            ui.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(theme, mode) });
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null);
            dialog.Scale(new SizeF(1.5f, 1.5f)); Pump();
            bool contained = true;
            foreach (string name in new[] { "UpdateStatusCapsule", "UpdatePercent", "UpdateInstallButton", "UpdateCancelButton", "UpdateProgressBar", "UpdateSteps" })
            {
                for (Control current = Find(name); current.Parent != null; current = current.Parent) { bool fits = current.Parent.ClientRectangle.Contains(current.Bounds); if (!fits) Console.WriteLine($"OUTSIDE {name}: {current.GetType().Name}/{current.Name} {current.Bounds}, parent {current.Parent.GetType().Name} {current.Parent.ClientSize}"); contained &= fits; }
            }
            if (capture && mode == "Light") Save(dialog, "update-light-scaled");
            Check(contained, "scaled layout keeps update information and actions inside the window");
            value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Installing"), 1L, 2L, 0.0 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value });
            dialog.Close(); Pump();
            Check(!dialog.IsDisposed && dialog.Visible && !Find("UpdateCancelButton").Enabled && !Find("UpdateCloseButton").Enabled, "file replacement cannot be interrupted by closing the window");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-installing");
            value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Completed"), 1L, 1L, 0.0 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump();
            Check(Find("UpdatePercent").Text == "100%" && Find("UpdateStatusCapsule").AccessibleDescription!.Contains("新版已经打开") && Find("UpdateCloseButton").Enabled && Find("UpdateCancelButton").Enabled, "confirmed completion has a finished title and dismissible actions");
            type.GetMethod("ShowFailure")!.Invoke(dialog, new object[] { "测试：已恢复原版本。" });
            ((Button)Find("UpdateCloseButton")).PerformClick(); Pump();
            Check(dialog.IsDisposed, "custom close dismisses an idle or failed update");
            using (Form helper = (Form)Activator.CreateInstance(type!, new object[] { "", "更新文件已校验。\n正在安装，完成后将自动打开。", 0L, true })!)
            {
                Control H(string name) => helper.Controls.Find(name, true)[0];
                helper.Show(); Pump();
                Check(H("UpdatePercent").Text == "90%" && !H("UpdateCloseButton").Enabled && !H("UpdateCancelButton").Enabled, "independent installer starts in a protected waiting state");
                object installing = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Installing"), 1L, 2L, 0.0 })!;
                type.GetMethod("Report")!.Invoke(helper, new[] { installing }); Pump();
                if (capture) Save(helper, "update-" + mode.ToLowerInvariant() + "-installer");
                type.GetMethod("ShowFailure")!.Invoke(helper, new object[] { "更新未完成，请重新打开应用。" });
                Check(!H("UpdateInstallButton").Visible && H("UpdateCloseButton").Enabled && H("UpdatePhase").Text.Contains("重新打开"), "installer failure does not advertise an unavailable retry");
                Check(H("UpdateStatusCapsule").AccessibleDescription!.Contains("安装未完成") && !H("UpdateStatusCapsule").AccessibleDescription!.Contains("请重新打开应用"), "installer capsule and next-step phase are distinct");
                ((Button)H("UpdateCloseButton")).PerformClick(); Pump();
            }
            MethodInfo? noticeFactory = type.GetMethod("CreateNotice", BindingFlags.Static | BindingFlags.NonPublic);
            Check(noticeFactory != null, "short update notices have a dedicated compact layout");
            foreach (bool failed in new[] { false, true })
            {
                using Form notice = (Form)noticeFactory!.Invoke(null, new object[] { failed ? "更新检查未完成" : "已是最新版本", failed ? "网络连接暂时不可用，请稍后重试。" : "当前已是最新版本，无需安装更新。", failed, "软件更新" })!;
                notice.Show(); Pump();
                Control N(string name) => notice.Controls.Find(name, true)[0];
                Check(notice.Height < 480 && !N("UpdateProgressSurface").Visible && !N("UpdateCancelButton").Visible, "notice avoids an empty progress card and extra action");
                Check(N("UpdateInstallButton").Text == "知道了" && N("UpdateStatusCapsule").AccessibleDescription!.Contains(failed ? "更新检查未完成" : "已是最新版本"), "compact notice preserves its status conclusion and dismiss action");
                Check(failed ? N("UpdateNotesSurface").Visible && N("UpdateNotes").Text.Contains("网络连接") : !N("UpdateNotesSurface").Visible, failed ? "failure notice retains unique diagnostic detail" : "latest notice omits duplicate result copy");
                notice.Scale(new SizeF(1.5f, 1.5f)); Pump();
                bool noticeFits = true;
                foreach (string name in new[] { "UpdateStatusCapsule", "UpdateInstallButton", "UpdateCloseButton" })
                    for (Control current = N(name); current.Parent != null; current = current.Parent)
                        noticeFits &= current.Parent.ClientRectangle.Contains(current.Bounds);
                Check(noticeFits && N("UpdateStatusCapsule").AccessibleDescription!.Contains(failed ? "更新检查未完成" : "已是最新版本"), "scaled notice keeps its capsule and action inside the window");
                if (capture) Save(notice, "update-" + mode.ToLowerInvariant() + (failed ? "-check-error" : "-latest") + "-scaled");
                ((Button)N("UpdateInstallButton")).PerformClick(); Pump(); Check(notice.IsDisposed, "notice can be dismissed from the primary action");
            }
            Type programType = app.GetType("ClipBridge.Windows.Program")!;
            MethodInfo? startupFactory = programType.GetMethod("CreateStartupFailureNotice", BindingFlags.Static | BindingFlags.NonPublic);
            Check(startupFactory != null, "startup failures have a testable themed notice factory");
            using Form startupNotice = (Form)startupFactory!.Invoke(null, new object[] { new IOException("do not expose this detail") })!;
            Control startupStatus = startupNotice.Controls.Find("UpdateStatusCapsule", true).Single();
            Check(startupStatus.AccessibleDescription!.Contains("应用启动") && startupStatus.AccessibleDescription.Contains("启动失败"), "startup failure uses the themed error capsule");
            Check(startupNotice.Controls.Find("UpdateNotes", true).Single().Text.Contains("IOException") && !startupNotice.Controls.Find("UpdateNotes", true).Single().Text.Contains("do not expose"), "startup notice shows exception type without leaking exception text");
        }
    }
    private static void Save(Form dialog, string name)
    {
        string directory = Path.Combine(Path.GetTempPath(), "clipbridge-ui-smoke"); Directory.CreateDirectory(directory);
        using var image = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        if (dialog.Region != null)
        {
            using var outside = new Region(new Rectangle(Point.Empty, image.Size)); outside.Exclude(dialog.Region);
            using var graphics = Graphics.FromImage(image); graphics.SetClip(outside, System.Drawing.Drawing2D.CombineMode.Replace);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            using var transparent = new SolidBrush(Color.Transparent); graphics.FillRectangle(transparent, 0, 0, image.Width, image.Height);
        }
        string target = Path.Combine(directory, name + ".png"); image.Save(target, ImageFormat.Png); Console.WriteLine("SCREENSHOT " + target);
    }
}
