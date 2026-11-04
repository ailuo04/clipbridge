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
    public static void Run(Assembly app, bool capture)
    {
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
            Control metadata = Find("UpdateMetadata");
            bool insideAncestors = true;
            for (Control current = metadata; current.Parent != null; current = current.Parent) insideAncestors &= current.Parent.ClientRectangle.Contains(current.Bounds);
            Check(insideAncestors && !metadata.Bounds.IntersectsWith(Find("UpdateTitle").Bounds) && metadata.Parent!.ClientRectangle.Contains(metadata.Bounds) && metadata.Visible && metadata.Height >= TextRenderer.MeasureText(metadata.Text, metadata.Font).Height && metadata.Width >= TextRenderer.MeasureText(metadata.Text, metadata.Font).Width, mode + " current/next versions and package size fit in the header");
            Check(Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, mode + " confirmation actions are available");
            int installs = 0, cancels = 0;
            type!.GetEvent("InstallRequested")!.AddEventHandler(dialog, new Action(() => installs++));
            type.GetEvent("CancelRequested")!.AddEventHandler(dialog, new Action(() => cancels++));
            ((Button)Find("UpdateInstallButton")).PerformClick(); Check(installs == 1, "install action is connected");
            type.GetMethod("BeginDownload")!.Invoke(dialog, null);
            Check(!Find("UpdateInstallButton").Enabled && Find("UpdateCancelButton").Enabled, "download permits cancellation but prevents duplicate install");
            ((Button)Find("UpdateCloseButton")).PerformClick(); Pump();
            Check(cancels == 1 && dialog.Visible && !dialog.IsDisposed, "custom close requests cancellation during download");
            type.GetMethod("ShowFailure")!.Invoke(dialog, new object[] { "测试：网络连接中断，请重试。" });
            Check(Find("UpdateInstallButton").Enabled && Find("UpdateDetails").Text.Contains("网络连接中断"), "failure retains readable reason and enables retry");
            type.GetMethod("ApplyTheme")!.Invoke(dialog, null);
            Color danger = (Color)ui.GetField("Danger", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(Find("UpdatePhase").ForeColor == danger, "theme refresh retains the failure state color");
            if (capture) Save(dialog, "update-" + mode.ToLowerInvariant() + "-error");
            type.GetMethod("BeginDownload")!.Invoke(dialog, null);
            Type progress = app.GetType("ClipBridge.Windows.UpdateProgress")!;
            Type stage = app.GetType("ClipBridge.Windows.UpdateStage")!;
            object value = Activator.CreateInstance(progress, new object[] { Enum.Parse(stage, "Downloading"), 6L * 1024 * 1024, 12L * 1024 * 1024, 2.0 * 1024 * 1024 })!;
            type.GetMethod("Report")!.Invoke(dialog, new[] { value }); Pump(400);
            foreach (string textName in new[] { "UpdateTitle", "UpdatePercent" })
            {
                Control text = Find(textName);
                Size measured = TextRenderer.MeasureText(textName == "UpdatePercent" ? "100%" : text.Text, text.Font);
                Check(text.Height >= measured.Height && text.Width >= measured.Width, textName + " typography fits without clipping");
            }
            Check(Find("UpdatePercent").Text == "40%" && Find("UpdateDetails").Text.Contains("MB"), "download displays honest overall percentage and byte details");
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
            foreach (string name in new[] { "UpdateTitle", "UpdateMetadata", "UpdatePercent", "UpdateInstallButton", "UpdateCancelButton", "UpdateProgressBar", "UpdateSteps" })
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
            Check(Find("UpdatePercent").Text == "100%" && Find("UpdateTitle").Text.Contains("更新完成") && Find("UpdateCloseButton").Enabled && Find("UpdateCancelButton").Enabled, "confirmed completion has a finished title and dismissible actions");
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
                ((Button)H("UpdateCloseButton")).PerformClick(); Pump();
            }
            MethodInfo? noticeFactory = type.GetMethod("CreateNotice", BindingFlags.Static | BindingFlags.NonPublic);
            Check(noticeFactory != null, "short update notices have a dedicated compact layout");
            foreach (bool failed in new[] { false, true })
            {
                using Form notice = (Form)noticeFactory!.Invoke(null, new object[] { failed ? "更新检查未完成" : "已是最新版本", failed ? "网络连接暂时不可用，请稍后重试。" : "当前已是最新版本，无需安装更新。", failed })!;
                notice.Show(); Pump();
                Control N(string name) => notice.Controls.Find(name, true)[0];
                Check(notice.Height < 480 && !N("UpdateProgressSurface").Visible && !N("UpdateCancelButton").Visible, "notice avoids an empty progress card and extra action");
                Check(N("UpdateInstallButton").Text == "知道了" && N("UpdateNotes").Text.Contains(failed ? "网络连接" : "最新版本"), "compact notice preserves its result and dismiss action");
                if (capture) Save(notice, "update-" + mode.ToLowerInvariant() + (failed ? "-check-error" : "-latest"));
                ((Button)N("UpdateInstallButton")).PerformClick(); Pump(); Check(notice.IsDisposed, "notice can be dismissed from the primary action");
            }
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
