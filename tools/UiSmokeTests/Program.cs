using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Smoke runner for the shipping UI only: the workspace shell and its
// conversation view, the device/pairing page, the inline QR panel and the
// update dialog. The retired control panel is deliberately not exercised.
internal static class Program
{
    private static void Pump(int ms = 150)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }

    private static void Check(bool test, string message)
    {
        if (!test) throw new Exception("FAIL " + message);
        Console.WriteLine("PASS " + message);
    }

    // The workspace replaced these surfaces. Keep them out so a stale file
    // cannot quietly reintroduce a second, divergent UI.
    private static readonly string[] RetiredTypes =
    {
        "ControlPanelForm", "ConversationForm", "DevicesForm", "PairingQrForm", "ManualSendForm",
        "DeviceListView", "EmptyDeviceState", "ConnectionSummaryBar", "FooterBarSurface",
        "GlassEventSurface", "StatusBadge", "PortTags", "ModernToggle", "ModernCheckBox",
        "ModalBackdropSurface", "WindowCaptionButton", "ThemeTransitionOverlay", "UiPalette",
        "UiEventSnapshot", "ClipboardFormatListener"
    };

    private static void RunRetirementGuard(Assembly app)
    {
        string[] returned = RetiredTypes
            .Where(name => app.GetType("ClipBridge.Windows." + name) != null)
            .ToArray();
        Check(returned.Length == 0, "retired legacy UI stays removed" +
            (returned.Length == 0 ? "" : " (still present: " + string.Join(", ", returned) + ")"));
    }

    private static void RunThemeController(Assembly app)
    {
        Type preferencesType = app.GetType("ClipBridge.Windows.UiThemePreferences")!;
        Type controllerType = app.GetType("ClipBridge.Windows.UiThemeController")!;
        Type modeType = app.GetType("ClipBridge.Windows.UiThemeMode")!;
        string preferencePath = Path.Combine(Path.GetTempPath(), "clipbridge-theme-smoke-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            object preferences = Activator.CreateInstance(preferencesType, new object[] { preferencePath })!;
            object controller = Activator.CreateInstance(controllerType, new object[] { preferences, new Func<bool>(() => true) })!;
            MethodInfo select = controllerType.GetMethod("SelectMode")!;
            object dark = Enum.Parse(modeType, "Dark"), system = Enum.Parse(modeType, "System"), light = Enum.Parse(modeType, "Light");
            Check((bool)select.Invoke(controller, new[] { dark })! && File.ReadAllText(preferencePath) == "Dark", "dark mode persists");
            Check((bool)select.Invoke(controller, new[] { system })! && File.ReadAllText(preferencePath) == "System", "system mode persists and follows light system theme");
            Check(!(bool)select.Invoke(controller, new[] { light })! && File.ReadAllText(preferencePath) == "Light", "same effective theme still saves selected mode");
        }
        finally { File.Delete(preferencePath); }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        bool capture = Array.Exists(args, value => value == "--capture");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string defaultDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../bin/Release/net10.0-windows/ClipBridge.dll"));
        Assembly app = Assembly.LoadFrom(Environment.GetEnvironmentVariable("CLIPBRIDGE_TEST_DLL") ?? defaultDll);
        // Keep preview thumbnails out of the real profile.
        app.GetType("ClipBridge.Windows.MediaPreview")!.GetField("ThumbnailDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Path.Combine(Path.GetTempPath(), "clipbridge-ui-smoke", "thumbnails"));

        if (Array.Exists(args, value => value == "--conversation-only")) { ConversationSmoke.Run(app, capture); return; }
        if (Array.Exists(args, value => value == "--palette-only")) { ConversationSmoke.RunPalette(app); return; }
        if (Array.Exists(args, value => value == "--updates-only")) { UpdateDialogSmoke.Run(app, capture); return; }
        if (Array.Exists(args, value => value == "--devices-only")) { DevicesFormSmoke.Run(app, capture); return; }

        RunRetirementGuard(app);
        ConversationSmoke.RunPalette(app);
        RunThemeController(app);
        Pump(60);
        ConversationSmoke.Run(app, capture);
        DevicesFormSmoke.Run(app, capture);
        UpdateDialogSmoke.Run(app, capture);
        Console.WriteLine("ALL TESTS PASSED");
    }
}