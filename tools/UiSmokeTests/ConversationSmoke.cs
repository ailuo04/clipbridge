using System;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class ConversationSmoke
{
    private static object? _fileTarget;
    private static string[]? _filePaths;
    private static bool _failFiles;
    private static TaskCompletionSource? _pendingSend;
    public static Task CaptureFiles(object peer, string[] paths, CancellationToken token) { _fileTarget=peer; _filePaths=paths; return _pendingSend?.Task ?? (_failFiles ? Task.FromException(new System.IO.IOException("simulated transfer failure")) : Task.CompletedTask); }
    private static object? Field(object value, string name) { var field=value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);return field!=null?field.GetValue(value):Field(value.GetType().GetProperty("View")!.GetValue(value)!,name); }

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
        Check(Call("Alignment", false)!.ToString() == "Left", "received message aligns left");
        Check(Call("Alignment", true)!.ToString() == "Right", "sent message aligns right");
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
        object eventA = Activator.CreateInstance(eventType, 1, "event-a", "peer-a", new string('a', 32), now, "text", "文件已经发过去了，手机直接接收就好。", Array.CreateInstance(fileType, 0), "sent")!;
        object eventIncoming = Activator.CreateInstance(eventType, 1, "event-incoming", "peer-a", "peer-a", now - 1000, "text", "这段文字是从手机同步过来的。", Array.CreateInstance(fileType, 0), "sent")!;
        Array fileMetadata = Array.CreateInstance(fileType, 1);
        fileMetadata.SetValue(Activator.CreateInstance(fileType, "received.txt", 8L, new string('b', 64)), 0);
        object eventB = Activator.CreateInstance(eventType, 1, "event-b", "peer-b", new string('b', 32), now, "file", null, fileMetadata, "sent")!;
        var references = new System.Collections.Generic.Dictionary<string, string> { ["0"] = localPath };
        MethodInfo upsert = historyStoreType.GetMethod("Upsert")!;
        upsert.Invoke(store, new[] { Activator.CreateInstance(recordType, eventA, new System.Collections.Generic.Dictionary<string, string>()) });
        upsert.Invoke(store, new[] { Activator.CreateInstance(recordType, eventIncoming, new System.Collections.Generic.Dictionary<string, string>()) });
        upsert.Invoke(store, new[] { Activator.CreateInstance(recordType, eventB, references) });

        object peerA = Activator.CreateInstance(peerType, "我的手机", "192.168.1.20", 45822, false, true, DateTimeOffset.Now, "peer-a")!;
        object peerB = Activator.CreateInstance(peerType, "Phone B", "192.168.1.21", 45822, false, true, DateTimeOffset.Now, "peer-b")!;
        Array peers = Array.CreateInstance(peerType, 2);
        peers.SetValue(peerA, 0); peers.SetValue(peerB, 1);
        Type peersProviderType = typeof(Func<>).MakeGenericType(peerArrayType);
        Delegate peersProvider = Expression.Lambda(peersProviderType, Expression.Constant(peers, peerArrayType)).Compile();
        Type sendTextType = typeof(Func<,,,>).MakeGenericType(peerType, typeof(string), typeof(CancellationToken), typeof(Task<bool>));
        MethodInfo resultMethod = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(bool));
        Delegate sendText = Expression.Lambda(sendTextType, Expression.Call(resultMethod, Expression.Constant(true)), Expression.Parameter(peerType), Expression.Parameter(typeof(string)), Expression.Parameter(typeof(CancellationToken))).Compile();
        Type previewType = typeof(Func<,,,>).MakeGenericType(peerType, typeof(string[]), typeof(CancellationToken), typeof(Task));
        var peerArg=Expression.Parameter(peerType); var pathsArg=Expression.Parameter(typeof(string[])); var tokenArg=Expression.Parameter(typeof(CancellationToken));
        Delegate preview = Expression.Lambda(previewType, Expression.Call(typeof(ConversationSmoke).GetMethod("CaptureFiles")!,Expression.Convert(peerArg,typeof(object)),pathsArg,tokenArg),peerArg,pathsArg,tokenArg).Compile();
        Delegate action = Expression.Lambda(typeof(Action), Expression.Empty()).Compile();
        using Form form = (Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object?[] { store, new string('a', 32), peersProvider, sendText, preview, action, action }, null)!;
        Check(form.Controls.Find("ConversationPeerSelector", true).Length == 1, "conversation peer selector exists");
        Check(app.GetType("ClipBridge.Windows.ConversationView") != null,"conversation content is reusable inside the workspace");
        Check(form.Controls.Find("ConversationTimeline", true).Length == 1, "conversation timeline exists");
        Check(form.Controls.Find("ConversationComposer", true).Length == 1, "conversation composer exists");
        Check(form.MinimumSize.Width >= 520 && form.MinimumSize.Height >= 460, "conversation window has a compact minimum size");
        MethodInfo refreshPeers = formType.GetMethod("RefreshPeers")!;
        refreshPeers.Invoke(form, new object?[] { peers, "peer-a" });
        object visible = formType.GetProperty("VisibleEntries")!.GetValue(form)!;
        Check(((System.Collections.IEnumerable)visible).Cast<object>().Select(item => (string)eventType.GetProperty("PeerId")!.GetValue(recordType.GetProperty("Event")!.GetValue(item))!).Count() == 2 && ((System.Collections.IEnumerable)visible).Cast<object>().All(item => (string)eventType.GetProperty("PeerId")!.GetValue(recordType.GetProperty("Event")!.GetValue(item))! == "peer-a"), "conversation history is isolated to the selected peer");
        Check(((System.Collections.IEnumerable)visible).Cast<object>().Select(item => (string)eventType.GetProperty("EventId")!.GetValue(recordType.GetProperty("Event")!.GetValue(item))!).SequenceEqual(new[] { "event-incoming", "event-a" }), "conversation is chronological with newest message at bottom");
        var selector = (ComboBox)form.Controls.Find("ConversationPeerSelector", true).Single();
        selector.SelectedIndex = 1;
        ((Task)formType.GetMethod("SendFilesAsync")!.Invoke(form,new object[] { new[] {localPath} })!).GetAwaiter().GetResult();
        Check(ReferenceEquals(_fileTarget,peerB) && _filePaths!.Single()==localPath, "file send targets the selected conversation without a confirmation dialog");
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
        formType.GetProperty("View")!.PropertyType.GetMethod("DeleteRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(formType.GetProperty("View")!.GetValue(form), new[] { fileRecord });
        Check(((System.Collections.IEnumerable)historyStoreType.GetMethod("GetRecent")!.Invoke(store, new object[] { "peer-b", now })!).Cast<object>().Count() == 0, "history delete removes only the selected record");
        Check(System.IO.File.Exists(localPath) && System.IO.File.ReadAllText(localPath) == "keep me", "deleting a file history row preserves its local file");
        form.Show();
        Application.DoEvents();
        selector.SelectedIndex = 0;
        Application.DoEvents();
        object timeline = Field(form, "_timeline")!;
        var rows = ((System.Collections.IEnumerable)Field(timeline, "_rows")!).Cast<object>().ToArray();
        Check(rows.Length == 2, "conversation shows both paired-device messages");
        foreach (object row in rows)
        {
            Control bubble = (Control)row.GetType().GetProperty("Bubble")!.GetValue(row)!;
            Check(bubble.ContextMenuStrip != null && bubble.ContextMenuStrip.Items.Count > 0, "message actions are available in a context menu");
            bool outgoing = (bool)Field(row, "_outgoing")!;
            Check(outgoing ? Math.Abs(bubble.Right - ((Control)row).ClientSize.Width) <= 2 : bubble.Left == 0, outgoing ? "sent bubble is placed on the right" : "received bubble is placed on the left");
        }
        Array many=Array.CreateInstance(recordType,24);
        for(int i=0;i<24;i++) {
            var ev=Activator.CreateInstance(eventType,1,"scroll-"+i,"peer-a","peer-a",now+i,"text","消息 "+i,Array.CreateInstance(fileType,0),"sent")!;
            many.SetValue(Activator.CreateInstance(recordType,ev,new System.Collections.Generic.Dictionary<string,string>()),i);
        }
        timeline.GetType().GetMethod("SetEntries")!.Invoke(timeline,new object[] {many,new string('a',32)}); Application.DoEvents();
        var scroll=(ScrollableControl)Field(timeline,"_list")!;
        Check(-scroll.AutoScrollPosition.Y>0,"new conversation scrolls to newest messages");
        scroll.AutoScrollPosition=System.Drawing.Point.Empty;
        Array extra=Array.CreateInstance(recordType,25); Array.Copy(many,extra,24);
        var newest=Activator.CreateInstance(eventType,1,"scroll-new","peer-a","peer-a",now+25,"text","新增消息",Array.CreateInstance(fileType,0),"sent")!;
        extra.SetValue(Activator.CreateInstance(recordType,newest,new System.Collections.Generic.Dictionary<string,string>()),24);
        timeline.GetType().GetMethod("SetEntries")!.Invoke(timeline,new object[] {extra,new string('a',32)}); Application.DoEvents();
        Check(scroll.AutoScrollPosition.Y==0,"incoming messages preserve the position while reading earlier messages");
        Check(form.Controls.Find("ConversationSyncClipboardButton",true).Length==0,"conversation uses manual messages instead of pulling clipboard");
        var composer=form.Controls.Find("ConversationComposer",true).Single();
        var paste=composer.GetType().GetMethod("PasteData") ?? throw new Exception("Missing message paste support");
        var textData=new DataObject(); textData.SetText("手动粘贴的消息");
        paste.Invoke(composer,new object[]{textData});
        Check(composer.Text=="手动粘贴的消息","pasted text stays editable before sending");
        using(var pastedImage=new System.Drawing.Bitmap(32,24)) {
            var imageData=new DataObject(); imageData.SetImage(pastedImage);
            paste.Invoke(composer,new object[]{imageData});
            var attachments=(System.Collections.IEnumerable)formType.GetProperty("PendingAttachments")!.GetValue(form)!;
            string path=attachments.Cast<string>().Single();
            Check(System.IO.File.Exists(path) && path.EndsWith(".png"),"pasted bitmap becomes a queued PNG attachment");
            using(var saved=System.Drawing.Image.FromFile(path)) Check(saved.Width==32 && saved.Height==24 && ((System.Drawing.Bitmap)saved).GetPixel(0,0).ToArgb()==pastedImage.GetPixel(0,0).ToArgb(),"pasted image retains its original pixels");
            Check(!ReferenceEquals(_fileTarget,peerA),"pasting an image does not transmit it");
            ((Button)form.Controls.Find("ConversationSendButton",true).Single()).PerformClick(); Application.DoEvents();
            Check(ReferenceEquals(_fileTarget,peerA) && _filePaths!.Single()==path,"send transmits the pasted image to the conversation peer");
            Check(!((System.Collections.IEnumerable)formType.GetProperty("PendingAttachments")!.GetValue(form)!).Cast<string>().Any(),"successful send clears queued attachments");
            System.IO.File.Delete(path);
        }
        var dropData=new DataObject(); dropData.SetData(DataFormats.FileDrop,new[]{localPath});
        var drop=new DragEventArgs(dropData,0,0,0,DragDropEffects.Copy,DragDropEffects.Copy);
        typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(composer,new object[]{drop});
        Check(((System.Collections.IEnumerable)formType.GetProperty("PendingAttachments")!.GetValue(form)!).Cast<string>().SequenceEqual(new[]{localPath}),"dropping files into the editor queues the original file");
        _failFiles=true;
        ((Button)form.Controls.Find("ConversationSendButton",true).Single()).PerformClick(); Application.DoEvents();
        Check(((System.Collections.IEnumerable)formType.GetProperty("PendingAttachments")!.GetValue(form)!).Cast<string>().Single()==localPath && System.IO.File.ReadAllText(localPath)=="keep me","failed send retains attachments and never modifies the original");
        _failFiles=false;
        ((Button)form.Controls.Find("ConversationSendButton",true).Single()).PerformClick(); Application.DoEvents();
        var mixed = Activator.CreateInstance(eventType, 1, "event-file", "peer-a", new string('a',32), now, "file", null, fileMetadata, "sent")!;
        upsert.Invoke(store,new[] { Activator.CreateInstance(recordType,mixed,references) });
        formType.GetMethod("RefreshHistory")!.Invoke(form,null);
        string photoPath=System.IO.Path.Combine(directory,"原图示例.png");
        using(var photo=new System.Drawing.Bitmap(320,180)) {
            using var graphics=System.Drawing.Graphics.FromImage(photo);
            graphics.Clear(System.Drawing.Color.FromArgb(33,55,91));
            using var sky=new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0,0,320,180),System.Drawing.Color.FromArgb(81,126,219),System.Drawing.Color.FromArgb(137,214,216),90f);
            graphics.FillRectangle(sky,0,0,320,180);
            graphics.FillEllipse(System.Drawing.Brushes.White,230,28,48,48);
            graphics.FillPolygon(System.Drawing.Brushes.DarkSlateBlue,new[]{new System.Drawing.Point(0,180),new System.Drawing.Point(110,60),new System.Drawing.Point(240,180)});
            photo.Save(photoPath,System.Drawing.Imaging.ImageFormat.Png);
        }
        var photoFiles=Array.CreateInstance(fileType,1);
        photoFiles.SetValue(Activator.CreateInstance(fileType,"原图示例.png",new System.IO.FileInfo(photoPath).Length,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(photoPath)))),0);
        var photoEvent=Activator.CreateInstance(eventType,1,"event-photo","peer-a","peer-a",now+1,"file",null,photoFiles,"sent")!;
        upsert.Invoke(store,new[]{Activator.CreateInstance(recordType,photoEvent,new System.Collections.Generic.Dictionary<string,string>{{"0",photoPath}})});
        formType.GetMethod("RefreshHistory")!.Invoke(form,null);
        form.Size=form.MinimumSize; Application.DoEvents();
        foreach(var row in ((System.Collections.IEnumerable)Field(timeline,"_rows")!).Cast<Control>()) {
            var bubble=(Control)row.GetType().GetProperty("Bubble")!.GetValue(row)!;
            Check(bubble.Left>=0 && bubble.Right<=row.Width && bubble.Height<=row.Height,"messages fit at minimum window width");
        }
        form.Size=new System.Drawing.Size(860,920); Application.DoEvents();
        if (capture)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipbridge-ui-smoke", "conversation-history-populated.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("SCREENSHOT " + path);
        }
        Type workspaceType=app.GetType("ClipBridge.Windows.WorkspaceForm") ?? throw new Exception("Missing WorkspaceForm");
        Type themeMode=app.GetType("ClipBridge.Windows.UiThemeMode")!;
        var themeAction=Expression.Lambda(typeof(Action<>).MakeGenericType(themeMode),Expression.Empty(),Expression.Parameter(themeMode)).Compile();
        Action<string> revoke=_=>{};
        using(var workspace=(Form)Activator.CreateInstance(workspaceType,new object?[]{store,new string('a',32),peersProvider,sendText,preview,action,revoke,action,themeAction,null,null})!) {
            workspace.Show();Application.DoEvents();
            workspaceType.GetMethod("SelectConversation")!.Invoke(workspace,new object[]{"peer-a"});Application.DoEvents();
            var view=workspaceType.GetProperty("Conversation")!.GetValue(workspace)!;
            var editor=(TextBox)workspace.Controls.Find("ConversationComposer",true).Single();
            Check(workspace.Controls.Find("ConversationPeerSelector",true).Length==0,"workspace switches devices only from sidebar");
            Check(workspace.Controls.Find("WorkspacePeer-peer-a",true).Length==1 && workspace.Controls.Find("WorkspacePeer-peer-b",true).Length==1,"workspace lists independent conversations");
            Check(workspace.Controls.Find("ConversationPasteButton",true).Length==0,"composer has one attachment entry");
            view.GetType().GetMethod("AddAttachments")!.Invoke(view,new object[]{new[]{directory}});
            Check(((string[])view.GetType().GetProperty("PendingAttachments")!.GetValue(view)!).Single()==directory,"directory queues without flattening");
            _pendingSend=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);_failFiles=false;
            ((Button)workspace.Controls.Find("ConversationSendButton",true).Single()).PerformClick();Application.DoEvents();
            Check((bool)view.GetType().GetProperty("HasActiveSends")!.GetValue(view)!,"background transfer started");
            workspaceType.GetMethod("SelectConversation")!.Invoke(workspace,new object[]{"peer-b"});Application.DoEvents();editor.Text="设备 B 的草稿";
            Check(!editor.ReadOnly,"other device remains editable during transfer");
            _pendingSend.SetResult();_pendingSend=null;var watch=System.Diagnostics.Stopwatch.StartNew();while((bool)view.GetType().GetProperty("HasActiveSends")!.GetValue(view)!&&watch.ElapsedMilliseconds<3000){Application.DoEvents();Thread.Sleep(10);}
            Check(editor.Text=="设备 B 的草稿","A completion preserves B draft");
            workspaceType.GetMethod("SelectConversation")!.Invoke(workspace,new object[]{"peer-a"});Application.DoEvents();
            Check(((string[])view.GetType().GetProperty("PendingAttachments")!.GetValue(view)!).Length==0,"A success clears only A attachments");
            ((Button)workspace.Controls.Find("WorkspaceSettings",true).Single()).PerformClick();Application.DoEvents();Check(workspace.Controls.Find("WorkspaceTheme",true).Single().Visible,"settings opens within same window");
            workspaceType.GetMethod("SelectConversation")!.Invoke(workspace,new object[]{"peer-b"});Application.DoEvents();Check(editor.Text=="设备 B 的草稿","settings navigation preserves conversation draft");
            workspaceType.GetMethod("SelectConversation")!.Invoke(workspace,new object[]{"peer-a"});Application.DoEvents();
            if(capture){using var image=new System.Drawing.Bitmap(workspace.Width,workspace.Height);workspace.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));string output=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"clipbridge-ui-smoke","workspace-chat.png");image.Save(output,System.Drawing.Imaging.ImageFormat.Png);Console.WriteLine("SCREENSHOT "+output);}
            workspace.Size=workspace.MinimumSize;Application.DoEvents();Check(editor.Width>150,"workspace composer fits minimum width");
            workspaceType.GetMethod("AllowExit")!.Invoke(workspace,null);workspace.Close();
        }
        form.Close();
        Check(!form.Visible && !form.IsDisposed, "closing the conversation window hides it for the tray");
        formType.GetProperty("AllowCloseForExit")!.SetValue(form, true);
        form.Close();
        Check(form.IsDisposed, "explicit exit can dispose the conversation window");
        System.IO.Directory.Delete(directory, true);
    }}

