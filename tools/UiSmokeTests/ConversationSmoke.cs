using System;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class ConversationSmoke
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL conversation: " + message);
        Console.WriteLine("PASS conversation: " + message);
    }

    internal static void Run(Assembly app, bool capture = false)
    {
        Type type = app.GetType("ClipBridge.Windows.HistoryPresentation")
            ?? throw new Exception("Missing ClipBridge.Windows.HistoryPresentation");
        object? Call(string method, params object[] args) => type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, binder: null, types: args.Select(value => value.GetType()).ToArray(), modifiers: null)!.Invoke(null, args);

        Check((string)Call("Direction", false)! == "收到", "received direction label");
        Check((string)Call("Direction", true)! == "已发送", "sent direction label");
        Check((string)Call("ContentKind", "text")! == "文本", "text row kind");
        Check((string)Call("ContentKind", "file")! == "文件", "file row kind");
        Check((string)Call("State", "sent")! == "已完成", "sent state label");
        Check((string)Call("State", "待接受")! == "待接受", "pending state label");
        Check((string)Call("State", "传输中")! == "传输中", "active state label");
        Check((string)Call("State", "failed: connection lost")! == "失败", "failed state label");
        Check((string)Call("State", "rejected")! == "已拒绝", "rejected state label");

        string[] textActions = ((System.Collections.Generic.IReadOnlyList<string>)Call("Actions", "text", false)!).ToArray();
        string[] fileActions = ((System.Collections.Generic.IReadOnlyList<string>)Call("Actions", "file", true)!).ToArray();
        string[] fileWithoutPathActions = ((System.Collections.Generic.IReadOnlyList<string>)Call("Actions", "file", false)!).ToArray();
        Check(textActions.SequenceEqual(new[] { "复制", "删除记录" }), "text actions copy and delete only the record");
        Check(fileActions.SequenceEqual(new[] { "打开本地文件", "删除记录" }), "file actions open local file and delete only the record");
        Check(fileWithoutPathActions.SequenceEqual(new[] { "删除记录" }), "file without a local path cannot offer open");
        Check(!textActions.Concat(fileActions).Any(action => action.Contains("删除文件", StringComparison.Ordinal)), "history row actions never delete local files");

        long now = 1_800_000_000_000;
        long day = 24L * 60 * 60 * 1000;
        Check((string)Call("ExpiryLabel", now, now)! == "7 天后过期", "new record has seven-day expiry label");
        Check((string)Call("ExpiryLabel", now - 6 * day, now)! == "1 天后过期", "near-expiry record has remaining-day label");
        Check((string)Call("ExpiryLabel", now - 7 * day, now)! == "已过期", "seven-day boundary is expired");
        RunFormSmoke(app, capture);
        Console.WriteLine("CONVERSATION TESTS PASSED");
    }

    internal static void RunPalette(Assembly app)
    {
        Type ui = app.GetType("ClipBridge.Windows.Ui") ?? throw new Exception("Missing Ui");
        Type theme = app.GetType("ClipBridge.Windows.UiColorTheme") ?? throw new Exception("Missing UiColorTheme");
        string[] roles = { "Canvas", "Surface", "FieldSurface", "Ink", "Muted", "Line", "Accent", "Success", "Warning", "Danger", "PrimarySurface", "PrimaryInk" };
        foreach (string themeName in new[] { "Light", "Dark" })
        {
            ui.GetMethod("ApplyTheme", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { Enum.Parse(theme, themeName) });
            foreach (string role in roles)
            {
                object value = ui.GetField(role, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Check(((System.Drawing.Color)value).A == 255, themeName + " " + role + " is opaque");
            }
            var canvas = (System.Drawing.Color)ui.GetField("Canvas", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var ink = (System.Drawing.Color)ui.GetField("Ink", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Check(canvas.ToArgb() != ink.ToArgb(), themeName + " canvas and ink remain distinguishable");
        }
        Console.WriteLine("PALETTE TESTS PASSED");
    }
    private static void RunFormSmoke(Assembly app, bool capture)
    {
        Type formType = app.GetType("ClipBridge.Windows.ConversationForm") ?? throw new Exception("Missing ConversationForm");
        Type peerType = app.GetType("ClipBridge.Windows.PeerSnapshot") ?? throw new Exception("Missing PeerSnapshot");
        Type peerArrayType = peerType.MakeArrayType();
        Type historyStoreType = app.GetType("ClipBridge.Windows.History.HistoryStore")!;
        Type eventType = app.GetType("ClipBridge.Windows.History.HistorySyncEvent")!;
        Type fileType = app.GetType("ClipBridge.Windows.History.HistoryFileMetadata")!;
        Type recordType = app.GetType("ClipBridge.Windows.History.HistoryLocalRecord")!;
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-conversation-smoke-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string localPath = System.IO.Path.Combine(directory, "received.txt");
        System.IO.File.WriteAllText(localPath, "keep me");
        object store = Activator.CreateInstance(historyStoreType, new object?[] { System.IO.Path.Combine(directory, "history.jsonl"), null, null })!;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        object eventA = Activator.CreateInstance(eventType, 1, "event-a", "peer-a", new string('a', 32), now, "text", "hello from A", Array.CreateInstance(fileType, 0), "sent")!;
        Array fileMetadata = Array.CreateInstance(fileType, 1);
        fileMetadata.SetValue(Activator.CreateInstance(fileType, "received.txt", 8L, new string('b', 64)), 0);
        object eventB = Activator.CreateInstance(eventType, 1, "event-b", "peer-b", new string('b', 32), now, "file", null, fileMetadata, "sent")!;
        var references = new System.Collections.Generic.Dictionary<string, string> { ["0"] = localPath };
        MethodInfo upsert = historyStoreType.GetMethod("Upsert")!;
        upsert.Invoke(store, new[] { Activator.CreateInstance(recordType, eventA, new System.Collections.Generic.Dictionary<string, string>()) });
        upsert.Invoke(store, new[] { Activator.CreateInstance(recordType, eventB, references) });

        object peerA = Activator.CreateInstance(peerType, "Laptop A", "192.168.1.20", 45822, false, true, DateTimeOffset.Now, "peer-a")!;
        object peerB = Activator.CreateInstance(peerType, "Phone B", "192.168.1.21", 45822, false, true, DateTimeOffset.Now, "peer-b")!;
        Array peers = Array.CreateInstance(peerType, 2);
        peers.SetValue(peerA, 0); peers.SetValue(peerB, 1);
        Type peersProviderType = typeof(Func<>).MakeGenericType(peerArrayType);
        Delegate peersProvider = Expression.Lambda(peersProviderType, Expression.Constant(peers, peerArrayType)).Compile();
        Type sendTextType = typeof(Func<,,,>).MakeGenericType(peerType, typeof(string), typeof(CancellationToken), typeof(Task<bool>));
        MethodInfo resultMethod = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(bool));
        Delegate sendText = Expression.Lambda(sendTextType, Expression.Call(resultMethod, Expression.Constant(true)), Expression.Parameter(peerType), Expression.Parameter(typeof(string)), Expression.Parameter(typeof(CancellationToken))).Compile();
        Type previewType = typeof(Action<>).MakeGenericType(typeof(string[]));
        Delegate preview = Expression.Lambda(previewType, Expression.Empty(), Expression.Parameter(typeof(string[]))).Compile();
        Delegate action = Expression.Lambda(typeof(Action), Expression.Empty()).Compile();
        using Form form = (Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object?[] { store, new string('a', 32), peersProvider, sendText, preview, action, action }, null)!;
        Check(form.Controls.Find("ConversationPeerSelector", true).Length == 1, "conversation peer selector exists");
        Check(form.Controls.Find("ConversationTimeline", true).Length == 1, "conversation timeline exists");
        Check(form.Controls.Find("ConversationComposer", true).Length == 1, "conversation composer exists");
        Check(form.MinimumSize.Width >= 520 && form.MinimumSize.Height >= 460, "conversation window has a compact minimum size");
        MethodInfo refreshPeers = formType.GetMethod("RefreshPeers")!;
        refreshPeers.Invoke(form, new object?[] { peers, "peer-a" });
        object visible = formType.GetProperty("VisibleEntries")!.GetValue(form)!;
        Check(((System.Collections.IEnumerable)visible).Cast<object>().Select(item => (string)eventType.GetProperty("PeerId")!.GetValue(recordType.GetProperty("Event")!.GetValue(item))!).SequenceEqual(new[] { "peer-a" }), "conversation history is isolated to the selected peer");
        var selector = (ComboBox)form.Controls.Find("ConversationPeerSelector", true).Single();
        selector.SelectedIndex = 1;
        visible = formType.GetProperty("VisibleEntries")!.GetValue(form)!;
        Check(((System.Collections.IEnumerable)visible).Cast<object>().Select(item => (string)eventType.GetProperty("PeerId")!.GetValue(recordType.GetProperty("Event")!.GetValue(item))!).SequenceEqual(new[] { "peer-b" }), "switching devices switches the history partition");
        if (capture)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke", "conversation-history-populated.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("SCREENSHOT " + path);
        }
        object fileRecord = historyStoreType.GetMethod("GetRecent")!.Invoke(store, new object[] { "peer-b", now }) is System.Collections.IEnumerable recent ? recent.Cast<object>().Single() : throw new Exception("Missing seeded history");
        formType.GetMethod("DeleteRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { fileRecord });
        Check(((System.Collections.IEnumerable)historyStoreType.GetMethod("GetRecent")!.Invoke(store, new object[] { "peer-b", now })!).Cast<object>().Count() == 0, "history delete removes only the selected record");
        Check(System.IO.File.Exists(localPath) && System.IO.File.ReadAllText(localPath) == "keep me", "deleting a file history row preserves its local file");
        form.Show();
        Application.DoEvents();
        if (capture)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke", "conversation-history-populated.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("SCREENSHOT " + path);
        }
        form.Close();
        Check(!form.Visible && !form.IsDisposed, "closing the conversation window hides it for the tray");
        formType.GetProperty("AllowCloseForExit")!.SetValue(form, true);
        form.Close();
        Check(form.IsDisposed, "explicit exit can dispose the conversation window");
        System.IO.Directory.Delete(directory, true);
    }}

