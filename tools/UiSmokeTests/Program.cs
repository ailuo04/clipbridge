using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
    private static int _themeCount;
    private static string _lastTheme = "";
    private static void OnTheme(object value) { _themeCount++; _lastTheme = value.ToString()!; }
    private static void Pump(int ms = 150)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }
    private static void Check(bool test, string message)
    {
        if (!test) throw new Exception("FAIL " + message);
        Console.WriteLine("PASS " + message);
    }
    private static void Raise(Control control, string name, EventArgs args) => typeof(Control).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, new object[] { args });
    private static object? Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);
    private static Control Find(Form form, string name) => form.Controls.Find(name, true)[0];
    private static bool HasAncestor(Control child, string name)
    {
        for (Control? parent = child.Parent; parent != null; parent = parent.Parent) if (parent.Name == name) return true;
        return false;
    }
    private static Control? FindByType(Control root, string fullName)
    {
        if (root.GetType().FullName == fullName) return root;
        foreach (Control child in root.Controls)
        {
            Control? match = FindByType(child, fullName);
            if (match != null) return match;
        }
        return null;
    }
    private static void CheckBrandIcon(Form form, Assembly app, string theme)
    {
        Control brand = FindByType(form, "ClipBridge.Windows.BrandMark") ?? throw new Exception("Missing brand icon");
        Check(HasAncestor(brand, "AppHeaderSurface") && brand.AccessibleName == "ClipBridge", $"{theme} header has an accessible brand icon");
        using var stream = app.GetManifestResourceStream("ClipBridge.AppIcon.ico") ?? throw new Exception("Missing app icon resource");
        using Icon icon = new Icon(stream, 128, 128);
        using Bitmap image = icon.ToBitmap();
        using Bitmap expected = new Bitmap(brand.Width, brand.Height);
        using (Graphics graphics = Graphics.FromImage(expected))
        {
            graphics.Clear(brand.Parent!.BackColor);
            float side = Math.Min(brand.ClientSize.Width, brand.ClientSize.Height) - 2f;
            RectangleF tile = new RectangleF((brand.ClientSize.Width - side) / 2f, (brand.ClientSize.Height - side) / 2f, side, side);
            Type ui = app.GetType("ClipBridge.Windows.Ui")!;
            Color fill = (Color)ui.GetField("HeaderRaised", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Color border = (Color)ui.GetField("ChromeLine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo rounded = app.GetType("ClipBridge.Windows.VisualDrawing")!.GetMethod("RoundedRectangle", BindingFlags.NonPublic | BindingFlags.Static)!;
            using var path = (System.Drawing.Drawing2D.GraphicsPath)rounded.Invoke(null, new object[] { tile, side * 0.24f })!;
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            int size = Math.Max(1, (int)Math.Floor(side - 4f));
            graphics.DrawImage(image, new Rectangle((brand.ClientSize.Width - size) / 2, (brand.ClientSize.Height - size) / 2, size, size));
        }
        using Bitmap actual = new Bitmap(brand.Width, brand.Height);
        brand.DrawToBitmap(actual, new Rectangle(Point.Empty, actual.Size));
        int differences = 0;
        for (int y = 0; y < actual.Height; y++)
        for (int x = 0; x < actual.Width; x++)
            if (actual.GetPixel(x, y).ToArgb() != expected.GetPixel(x, y).ToArgb()) differences++;
        Check(differences == 0, $"{theme} header renders the app icon on its themed tile ({differences} differing pixels)");
    }
    private static void CheckBrandLayout(Form form, string context)
    {
        Control brand = Find(form, "BrandMark");
        Control text = Find(form, "BrandText");
        Control title = Find(form, "BrandTitle");
        Control subtitle = Find(form, "BrandSubtitle");
        Rectangle brandBounds = new Rectangle(brand.PointToScreen(Point.Empty), brand.Size);
        Rectangle textBounds = new Rectangle(text.PointToScreen(Point.Empty), text.Size);
        Rectangle titleBounds = new Rectangle(title.PointToScreen(Point.Empty), title.Size);
        Rectangle subtitleBounds = new Rectangle(subtitle.PointToScreen(Point.Empty), subtitle.Size);
        Rectangle updateBounds = new Rectangle(Find(form, "CheckUpdatesButton").PointToScreen(Point.Empty), Find(form, "CheckUpdatesButton").Size);
        Check(Math.Abs((brandBounds.Top + brandBounds.Bottom) - (textBounds.Top + textBounds.Bottom)) <= 2, $"{context} brand and text are vertically aligned");
        Check(brandBounds.Right < titleBounds.Left && titleBounds.Bottom <= subtitleBounds.Top && textBounds.Right <= updateBounds.Left, $"{context} brand, text and header controls do not overlap");
        Check(title.Width >= TextRenderer.MeasureText(title.Text, title.Font).Width && subtitle.Width >= TextRenderer.MeasureText(subtitle.Text, subtitle.Font).Width, $"{context} brand title and metadata fit");
        Check(title.Height >= TextRenderer.MeasureText(title.Text, title.Font).Height + 4 && subtitle.Height >= TextRenderer.MeasureText(subtitle.Text, subtitle.Font).Height, $"{context} brand text has room for ascenders and descenders");
    }
    private static void Screenshot(Form form, string name)
    {
        Control root = Find(form, "WorkspaceRoot");
        using Bitmap image = new Bitmap(root.Width, root.Height);
        root.DrawToBitmap(image, new Rectangle(Point.Empty, root.Size));
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke", name + ".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        image.Save(path, ImageFormat.Png);
        Console.WriteLine("SCREENSHOT " + path);
    }
    [STAThread] private static void Main(string[] args)
    {
        bool capture = Array.Exists(args, value => value == "--capture");
        bool benchmark = Array.Exists(args, value => value == "--benchmark");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string defaultDll = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../bin/Release/net10.0-windows/ClipBridge.dll"));
        string dllPath = Environment.GetEnvironmentVariable("CLIPBRIDGE_TEST_DLL") ?? defaultDll;
        Assembly app = Assembly.LoadFrom(dllPath);
        Type formType = app.GetType("ClipBridge.Windows.ControlPanelForm")!;
        Type modeType = app.GetType("ClipBridge.Windows.UiThemeMode")!;
        Type uiType = app.GetType("ClipBridge.Windows.Ui")!;
        Type colorType = app.GetType("ClipBridge.Windows.UiColorTheme")!;
        void Apply(string color) => uiType.GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { Enum.Parse(colorType, color) });
        Form Create(string mode) => (Form)Activator.CreateInstance(formType, new object[] { "FAKE-KEY-FOR-UI-TEST", Enum.Parse(modeType, mode), true })!;
        Apply("Light");
        using Form form = Create("Light");
        int updates = 0;
        formType.GetEvent("CheckUpdateRequested")!.AddEventHandler(form, new Action(() => updates++));
        EventInfo themeEvent = formType.GetEvent("ThemeChangeRequested")!;
        ParameterExpression argument = Expression.Parameter(modeType);
        Delegate handler = Expression.Lambda(themeEvent.EventHandlerType!, Expression.Call(typeof(Program).GetMethod("OnTheme", BindingFlags.NonPublic | BindingFlags.Static)!, Expression.Convert(argument, typeof(object))), argument).Compile();
        themeEvent.AddEventHandler(form, handler);
        form.Show(); Pump(600);
        CheckBrandIcon(form, app, "light");
        CheckBrandLayout(form, "normal width");
        Size originalSize = form.Size;
        form.Size = form.MinimumSize; Pump(150);
        CheckBrandLayout(form, "minimum width");
        form.Size = originalSize; Pump(150);
        int style = GetWindowLong(form.Handle, -16);
        Check((style & 0x00CF0000) == 0x00CF0000 && (style & unchecked((int)0x80000000)) == 0, "native overlapped window styles");
        Control selector = Find(form, "ThemeSelector");
        Control update = Find(form, "CheckUpdatesButton");
        Control events = Find(form, "RunEvents");
        Check(HasAncestor(selector, "FooterBarSurface") && HasAncestor(update, "AppHeaderSurface"), "selector in footer and update in header");
        Check(selector.AccessibilityObject.GetChildCount() == 3, "three accessible theme options");
        Check(selector.AccessibilityObject.GetChild(1)!.State.HasFlag(AccessibleStates.Checked), "light is selected initially");
        ((Button)update).PerformClick(); Pump(); Check(updates == 1, "update button invokes callback");
        formType.GetMethod("SetUpdateCheckState")!.Invoke(form, new object[] { false, "检查中..." });
        Check(!update.Enabled && update.Text == "检查中...", "update busy state retained by button");
        formType.GetMethod("SetUpdateCheckState")!.Invoke(form, new object[] { true, "检查更新" });
        selector.AccessibilityObject.GetChild(0)!.DoDefaultAction(); Pump(250);
        Check(_themeCount == 1 && _lastTheme == "Dark", "dark option callback");
        Raise(selector, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 16, 0));
        Raise(selector, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, selector.Width - 10, 16, 0));
        Check(_themeCount == 1, "drag does not commit until release");
        Raise(selector, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, selector.Width - 10, 16, 0)); Pump(250);
        Check(_themeCount == 2 && _lastTheme == "System", "drag selects system");
        Raise(selector, "OnKeyDown", new KeyEventArgs(Keys.Left)); Pump(250);
        Check(_themeCount == 3 && _lastTheme == "Light", "keyboard selects light");
        selector.AccessibilityObject.GetChild(0)!.DoDefaultAction();
        selector.AccessibilityObject.GetChild(2)!.DoDefaultAction(); Pump(250);
        // With system animations disabled each selection commits immediately.
        Check((_themeCount == 4 || _themeCount == 5) && _lastTheme == "System", "rapid changes end on the last option");
        int previousThemeCount = _themeCount;
        selector.AccessibilityObject.GetChild(1)!.DoDefaultAction(); Pump(250);
        Check(_themeCount == previousThemeCount + 1 && _lastTheme == "Light", "return to light");
        Control eventSurface = Find(form, "EventLogSurface");
        FieldInfo eventFlash = eventSurface.GetType().GetField("_flash", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool systemAnimation = (bool)app.GetType("ClipBridge.Windows.VisualMotion")!.GetProperty("AnimationsEnabled", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        formType.GetMethod("AddEvent")!.Invoke(form, new object[] { "测试：设备连接成功" });
        if (systemAnimation) Check((double)eventFlash.GetValue(eventSurface)! > 0, "new log entries retain their own glass animation");
        Stopwatch settle = Stopwatch.StartNew();
        while ((double)eventFlash.GetValue(eventSurface)! > 0 && settle.ElapsedMilliseconds < 3000) Pump(50);
        Check(((ListBox)events).Items.Count == 2, "event added to log");
        Check((double)eventFlash.GetValue(eventSurface)! == 0, "log animation settles before a theme change");
        if (capture)
        {
            Screenshot(form, "light");
            // Populated device list, for reviewing the grid next to the empty-state topology.
            Type peerType = app.GetType("ClipBridge.Windows.PeerSnapshot")!;
            object Peer(string name, string address, bool manual, bool seen, DateTimeOffset? last) => Activator.CreateInstance(peerType, name, address, 45822, manual, seen, last)!;
            Array peers = Array.CreateInstance(peerType, 3);
            peers.SetValue(Peer("Pixel-8", "10.8.165.21", false, true, DateTimeOffset.Now), 0);
            peers.SetValue(Peer("LAPTOP-OFFICE", "10.8.165.37", true, true, DateTimeOffset.Now.AddMinutes(-2)), 1);
            peers.SetValue(Peer("192.168.1.88", "192.168.1.88", true, false, null), 2);
            formType.GetMethod("UpdatePeers")!.Invoke(form, new object[] { peers });
            Pump(500);
            Screenshot(form, "light-devices");
            formType.GetMethod("UpdatePeers")!.Invoke(form, new object[] { Array.CreateInstance(peerType, 0) });
            Pump(500);
        }
        IntPtr originalHandle = form.Handle;
        IntPtr originalSelectorHandle = selector.Handle;
        Rectangle originalBounds = form.Bounds;
        object manualShell = Field(form, "_peerIpShell")!;
        TextBox manualInput = (TextBox)manualShell.GetType().GetProperty("Editor")!.GetValue(manualShell)!;
        manualInput.Text = "192.168.1.25";
        manualInput.Focus();
        int originalEvents = ((ListBox)events).Items.Count;
        formType.GetMethod("SetUpdateCheckState")!.Invoke(form, new object[] { false, "检查中..." });
        Apply("Dark");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Dark") });
        Check((double)eventFlash.GetValue(eventSurface)! == 0 && ((ListBox)events).Items.Count == originalEvents, "theme switching does not replay log animation or add a log entry");
        bool animationEnabled = (bool)app.GetType("ClipBridge.Windows.VisualMotion")!.GetProperty("AnimationsEnabled", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Control transition = (Control)Field(form, "_themeTransitionOverlay")!;
        Type transitionType = transition.GetType();
        Bitmap? targetLogFrame = null;
        Rectangle eventRegion = new Rectangle(form.PointToClient(eventSurface.PointToScreen(Point.Empty)), eventSurface.Size);
        if (animationEnabled)
        {
            Check(transition.Visible && (bool)transitionType.GetProperty("HasFrame")!.GetValue(transition)!, "one buffered surface covers the entire transition");
            MethodInfo bandMethod = transitionType.GetMethod("RevealBand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo radiusMethod = transitionType.GetMethod("RadiusForProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
            PointF origin = (PointF)transitionType.GetField("_origin", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition)!;
            for (int step = 0; step < 10; step++)
            {
                int inner = (int)radiusMethod.Invoke(transition, new object[] { step / 10.0 })!;
                int outer = (int)radiusMethod.Invoke(transition, new object[] { (step + 1) / 10.0 })!;
                using Region dirty = (Region)bandMethod.Invoke(transition, new object[] { inner, outer })!;
                for (int y = 0; y < transition.Height; y += 13)
                for (int x = 0; x < transition.Width; x += 13)
                {
                    double distance = Math.Sqrt((x - origin.X) * (x - origin.X) + (y - origin.Y) * (y - origin.Y));
                    if (distance > inner && distance < outer && !dirty.IsVisible(x, y)) throw new Exception("Reveal dirty region missed a changed pixel");
                }
            }
            Check(true, "incremental dirty regions cover the entire reveal path");
            transitionType.GetField("_progress", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transition, 0.3);
            using Bitmap frame = (Bitmap)transitionType.GetMethod("CaptureCurrentFrame")!.Invoke(transition, null)!;
            Bitmap from = (Bitmap)transitionType.GetField("_from", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition)!;
            Bitmap to = (Bitmap)transitionType.GetField("_to", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition)!;
            targetLogFrame = to.Clone(eventRegion, PixelFormat.Format32bppArgb);
            int oldPixels = 0, newPixels = 0, mixedPixels = 0;
            for (int y = 20; y < frame.Height; y += 40)
            for (int x = 20; x < frame.Width; x += 40)
            {
                int pixel = frame.GetPixel(x, y).ToArgb();
                int oldPixel = from.GetPixel(x, y).ToArgb();
                int newPixel = to.GetPixel(x, y).ToArgb();
                if (oldPixel == newPixel) continue;
                if (pixel == oldPixel) oldPixels++; else if (pixel == newPixel) newPixels++; else mixedPixels++;
            }
            Check(oldPixels > 0 && newPixels > 0 && mixedPixels < (oldPixels + newPixels) / 10, "reveal keeps almost all pixels at full theme contrast with only a narrow blended edge");
            if (benchmark)
            {
                MethodInfo draw = transitionType.GetMethod("DrawFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
                using Bitmap output = new Bitmap(transition.Width, transition.Height);
                using Graphics graphics = Graphics.FromImage(output);
                object[] drawArgs = { graphics };
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) draw.Invoke(transition, drawArgs);
                watch.Stop();
                Console.WriteLine($"BENCHMARK {transition.Width}x{transition.Height}: {watch.Elapsed.TotalMilliseconds / 30:F2} ms per buffered draw");
            }
            if (capture)
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke", "theme-reveal.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                frame.Save(path, ImageFormat.Png);
                Console.WriteLine("SCREENSHOT " + path);
            }
        }
        Pump(500);
        Check(!transition.Visible && !(bool)transitionType.GetProperty("HasFrame")!.GetValue(transition)!, "transition releases its buffered frames");
        if (targetLogFrame != null)
        {
            using (targetLogFrame)
            using (Bitmap finalWorkspace = (Bitmap)formType.GetMethod("CaptureWorkspace", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!)
            {
                int changedPixels = 0;
                for (int y = 0; y < eventRegion.Height; y++)
                for (int x = 0; x < eventRegion.Width; x++)
                {
                    Color a = targetLogFrame.GetPixel(x, y);
                    Color b = finalWorkspace.GetPixel(eventRegion.X + x, eventRegion.Y + y);
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 3) changedPixels++;
                }
                Check(changedPixels < eventRegion.Width * eventRegion.Height / 1000, "log target snapshot matches settled theme without a final flash");
            }
        }
        Check((double)eventFlash.GetValue(eventSurface)! == 0, "theme transition leaves log flash animation idle");
        Color PaletteColor(string name) => (Color)uiType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(form.Handle == originalHandle && selector.Handle == originalSelectorHandle && form.Visible && form.Bounds == originalBounds, "theme transition keeps window and controls in place");
        Check(manualInput.Text == "192.168.1.25" && manualInput.Focused && ((ListBox)events).Items.Count == originalEvents && !update.Enabled, "theme transition preserves input, focus, log and update state");
        Check(form.BackColor == PaletteColor("Canvas") && Find(form, "AppHeaderSurface").BackColor == PaletteColor("Header") && manualInput.BackColor == PaletteColor("FieldSurface"), "dark palette applied across native controls");
        Check(manualInput.ForeColor == PaletteColor("Ink") && events.ForeColor == PaletteColor("Ink"), "dark foreground colors applied");
        CheckBrandIcon(form, app, "dark");
        if (capture) Screenshot(form, "dark-transition");
        Apply("Light");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Light") });
        Pump(500);
        Check(form.Handle == originalHandle && form.BackColor == PaletteColor("Canvas") && manualInput.BackColor == PaletteColor("FieldSurface"), "light transition keeps the same window");
        Apply("Dark");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Dark") }); Pump(50);
        Apply("Light");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Light") }); Pump(500);
        Check(form.Handle == originalHandle && form.BackColor == PaletteColor("Canvas"), "interrupted theme transition ends on latest palette");
        int beforeGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        for (int i = 0; i < 20; i++)
        {
            string modeName = i % 2 == 0 ? "Dark" : "Light";
            Apply(modeName);
            formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, modeName) });
            Pump(10);
        }
        Pump(600);
        Check(!transition.Visible && !(bool)transitionType.GetProperty("HasFrame")!.GetValue(transition)! && transitionType.GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition) == null && !(bool)transitionType.GetField("_highResolutionTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition)! && transitionType.GetField("_fromNative", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition) == null && transitionType.GetField("_toNative", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition) == null, "rapid toggles release timer and native frame resources");
        int afterGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        Check(afterGdi <= beforeGdi + 6, $"no accumulating GDI handles across rapid toggles ({beforeGdi} -> {afterGdi})");
        form.Hide();
        Apply("Dark");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Dark") });
        Check(!form.Visible && form.BackColor == PaletteColor("Canvas") && form.Handle == originalHandle, "hidden window changes theme without appearing");
        form.Show(); Pump(200);
        Apply("Light");
        formType.GetMethod("ApplyTheme")!.Invoke(form, new[] { Enum.Parse(modeType, "Light") }); Pump(500);
        formType.GetMethod("SetUpdateCheckState")!.Invoke(form, new object[] { true, "检查更新" });
        for (int i = 0; i < 55; i++) formType.GetMethod("AddEvent")!.Invoke(form, new object[] { "测试记录 " + i });
        Check(((ListBox)events).Items.Count == 50, "log retains at most 50 entries");
        Control min = (Control)Field(form, "_minimizeButton")!;
        Control max = (Control)Field(form, "_maximizeButton")!;
        Raise(min, "OnClick", EventArgs.Empty); Pump(250);
        Check(form.WindowState == FormWindowState.Minimized, "minimize button");
        formType.GetMethod("ShowControlPanel")!.Invoke(form, null); Pump(250);
        Check(form.WindowState == FormWindowState.Normal, "restore normal window");
        Raise(max, "OnClick", EventArgs.Empty); Pump(250);
        Rectangle work = Screen.FromControl(form).WorkingArea;
        Rectangle clientOnScreen = new Rectangle(form.PointToScreen(Point.Empty), form.ClientSize);
        Console.WriteLine($"MAX bounds={form.Bounds} client={clientOnScreen} work={work}");
        Check(form.WindowState == FormWindowState.Maximized, "maximize button");
        Check(clientOnScreen == work, "maximized client fits monitor work area");
        Raise(min, "OnClick", EventArgs.Empty); Pump(250);
        Check(form.WindowState == FormWindowState.Minimized, "minimize maximized window");
        formType.GetMethod("ShowControlPanel")!.Invoke(form, null); Pump(250);
        Check(form.WindowState == FormWindowState.Maximized, "restore returns to maximized state");
        Raise(max, "OnClick", EventArgs.Empty); Pump(250);
        Check(form.WindowState == FormWindowState.Normal, "unmaximize button");
        form.Hide();
        Apply("Dark");
        using Form dark = Create("Dark");
        dark.Show(); Pump(600);
        formType.GetMethod("AddEvent")!.Invoke(dark, new object[] { "测试：设备连接成功" }); Pump(400);
        if (capture) Screenshot(dark, "dark");
        IntPtr darkHandle = dark.Handle;
        TextBox darkManual = (TextBox)Field(dark, "_peerIpShell")!.GetType().GetProperty("Editor")!.GetValue(Field(dark, "_peerIpShell"))!;
        Apply("Light");
        formType.GetMethod("ApplyTheme")!.Invoke(dark, new[] { Enum.Parse(modeType, "Light") }); Pump(500);
        Check(dark.Handle == darkHandle && dark.BackColor == PaletteColor("Canvas") && Find(dark, "AppHeaderSurface").BackColor == PaletteColor("Header") && darkManual.BackColor == PaletteColor("FieldSurface"), "dark-started window transitions to light in place");
        Apply("Dark");
        formType.GetMethod("ApplyTheme")!.Invoke(dark, new[] { Enum.Parse(modeType, "Dark") }); Pump(500);
        object snapshots = formType.GetMethod("ExportEvents")!.Invoke(form, null)!;
        formType.GetMethod("RestoreEvents")!.Invoke(dark, new[] { snapshots });
        Check(((ListBox)Find(dark, "RunEvents")).Items.Count == 50, "log export and restore preserve events");
        dark.Hide();

        Type preferencesType = app.GetType("ClipBridge.Windows.UiThemePreferences")!;
        Type controllerType = app.GetType("ClipBridge.Windows.UiThemeController")!;
        string preferencePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-theme-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            object preferences = Activator.CreateInstance(preferencesType, new object[] { preferencePath })!;
            object controller = Activator.CreateInstance(controllerType, new object[] { preferences, new Func<bool>(() => true) })!;
            object darkMode = Enum.Parse(modeType, "Dark");
            object systemMode = Enum.Parse(modeType, "System");
            object lightMode = Enum.Parse(modeType, "Light");
            MethodInfo select = controllerType.GetMethod("SelectMode")!;
            Check((bool)select.Invoke(controller, new[] { darkMode })! && System.IO.File.ReadAllText(preferencePath) == "Dark", "dark mode persists");
            Check((bool)select.Invoke(controller, new[] { systemMode })! && System.IO.File.ReadAllText(preferencePath) == "System", "system mode persists and follows light system theme");
            Check(!(bool)select.Invoke(controller, new[] { lightMode })! && System.IO.File.ReadAllText(preferencePath) == "Light", "same effective theme still saves selected mode");
        }
        finally { System.IO.File.Delete(preferencePath); }
        Console.WriteLine("ALL TESTS PASSED");
    }
}
