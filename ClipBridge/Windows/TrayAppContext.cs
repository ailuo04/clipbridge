using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClipBridge.Windows.History;

namespace ClipBridge.Windows;

internal sealed class TrayAppContext : ApplicationContext
{
	private readonly Control _dispatcher = new Control();

	private readonly NotifyIcon _trayIcon;

	private readonly ContextMenuStrip _trayMenu;

	private readonly SyncNode _node;
    private readonly DeviceHub _devices;
    private readonly HistoryStore? _history;
    private HistoryPruneScheduler? _historyPruneScheduler;
    private ConversationView? _conversation;

	private readonly FileTransferNode _files;
    private readonly string _receiveDirectory = LocalFiles.ReceiveDirectory;
    private sealed record PendingFile(FileOffer Offer, IPAddress Address, TaskCompletionSource<bool> Reply);
    private readonly ConcurrentDictionary<string, PendingFile> _pending = new();
    private readonly ConcurrentDictionary<string, TransferProgress> _transfers = new();
    private TransfersForm? _transfersForm;

	private WorkspaceForm _panel;



	private readonly ToolStripMenuItem _statusMenu;

	private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer
	{
		Interval = 1000
	};

	private readonly System.Threading.Timer _updateTimer;

	private readonly CancellationTokenSource _updateLifetime = new CancellationTokenSource();

	private readonly RegisteredWaitHandle _activationWait;

	private readonly byte[] _pairingKey;

	private readonly UiThemePreferences _themePreferences = new UiThemePreferences();

	private readonly UiThemeController _themeController;

	private readonly SystemThemeWatcher _systemThemeWatcher;



	private bool _exiting;

	private int _updateCheckRunning;

	private int _updateInstalling;

    private UpdateDialog? _updateDialog;
    private CancellationTokenSource? _updateDownload;
    private CancellationTokenSource? _rollbackLoadCancellation;

	public TrayAppContext(EventWaitHandle activation)
	{
		_ = _dispatcher.Handle;
		_themeController = new UiThemeController(_themePreferences);
		_systemThemeWatcher = new SystemThemeWatcher();
		_systemThemeWatcher.Changed += () =>
		{
			Dispatch(() =>
			{
				_themeController.RefreshSystemTheme();
			});
		};
		_themeController.RefreshSystemTheme();
		_pairingKey = ProtectedKeyStore.LoadOrCreate();
		_devices = new DeviceHub(new WindowsTrustStore(), _pairingKey);
		try { _history = new HistoryStore(); } catch { _history = null; }
		_node = new SyncNode(_pairingKey, Dispatch, discovery: false, hub: _devices, historyStore: _history);
        _devices.LegacyDiscovered += _node.DiscoveredLegacy;

		_files = new FileTransferNode(_pairingKey, _receiveDirectory, QueueFileRequestAsync, hub: _devices, historyStore: _history);
        _panel = CreateWorkspace();
        _conversation = _panel.Conversation;
        _devices.PeerReady += peer => Dispatch(() =>
        {
            if (!_exiting) { _panel.RefreshWorkspace(); }
        });
        _devices.Status += text => Dispatch(() => ReportStatus(text));
        _files.Progress += progress => { _transfers[progress.TransferId] = progress; };
        _files.StatusChanged += status => Dispatch(() => ReportStatus(status));
		_trayMenu = new ContextMenuStrip
		{
			ShowImageMargin = false
		};
		_trayMenu.Items.Add("打开会话窗口", null, (object? _, EventArgs _) => OpenConversationOrPanel());
        _trayMenu.Items.Add("打开控制面板", null, (object? _, EventArgs _) =>
		{
			_panel.ShowControlPanel();
		});
        _trayMenu.Items.Add("发消息…", null, (_, _) => OpenConversationOrPanel());
        _trayMenu.Items.Add("发送文件", null, (_, _) => ChooseFiles());
        _trayMenu.Items.Add("文件请求与传输记录", null, (_, _) => OpenTransfers());
		_trayMenu.Items.Add("附近设备与配对", null, (_, _) => OpenDevices());
		_statusMenu = new ToolStripMenuItem("就绪 · 局域网互传")
		{
			Enabled = false
		};
		_trayMenu.Items.Add(_statusMenu);
		_trayMenu.Items.Add(new ToolStripSeparator());
		_trayMenu.Items.Add("退出应用", null, (object? _, EventArgs _) =>
		{
			ExitThread();
		});
		ApplyThemeToTrayMenu();
		_trayIcon = new NotifyIcon
		{
			Text = "ClipBridge · 互传",
			Icon = AppIcon.Load(),
			ContextMenuStrip = _trayMenu,
			Visible = true
		};
		_trayIcon.DoubleClick += (object? _, EventArgs _) =>
		{
			            OpenConversationOrPanel();
		};
		_devices.PairingRequested += () => Dispatch(() =>
        {
            if (_exiting) return;
            _trayIcon.BalloonTipTitle = "互传 · 设备配对";
            _trayIcon.BalloonTipText = "有设备等待配对，点击这里核对验证码。";
            _trayIcon.ShowBalloonTip(10000);
        });
        _trayIcon.BalloonTipClicked += (_, _) => { if (!_devices.Pending.IsEmpty) OpenDevices(); };
        _node.RemoteTextReceived += _ => { ReportStatus("收到新消息，可在会话中查看或复制"); _conversation?.RefreshHistory(); };
		_node.StatusChanged += ReportStatus;
		_themeController.ThemeChanged += OnThemeChanged;
		_refreshTimer.Tick += (object? _, EventArgs _) =>
		{
			RefreshPanel();
		};
		_refreshTimer.Start();
		_updateTimer = UpdateCheckScheduler.Start(() =>
		{
			_ = CheckForUpdatesAsync(manual: false);
		});
		_activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (object? _, bool _) =>
		{
			Dispatch(OpenConversationOrPanel);
		}, null, -1, executeOnlyOnce: false);
        if (_history != null)
        {
            _historyPruneScheduler = new HistoryPruneScheduler(
                () => _history.Prune(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                error => System.Diagnostics.Trace.TraceWarning("ClipBridge history prune failed: {0}", error));
        }
		RefreshPanel();
        _panel.ShowControlPanel();
	}

    private PeerSnapshot[] WorkspacePeers()
    {
        var live=_node.GetPeerSnapshots();
        return _devices.Trusted().Where(x=>x.Active).Select(trust=>(live.FirstOrDefault(p=>p.PeerId==trust.Id)??new PeerSnapshot(trust.Name,trust.Address,SharedProtocol.TcpPort,false,false,null,trust.Id)) with { Platform = trust.Platform }).OrderBy(x=>x.Name,StringComparer.Ordinal).ToArray();
    }
    private WorkspaceForm CreateWorkspace()
    {
        var page=new DevicesView(_devices);
        var workspace = new WorkspaceForm(_history,_devices.DeviceId,WorkspacePeers,
            (peer,text,token)=>_node.SendTextAsync(IPAddress.Parse(peer.Address),text,token,expectedPeerId:peer.PeerId),
            (peer,paths,token)=>_files.SendAsync(IPAddress.Parse(peer.Address),paths,token,expectedPeerId:peer.PeerId),
            OpenReceiveDirectory,id=>_devices.Revoke(id),()=>_ = CheckForUpdatesAsync(true),ChangeTheme,page,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClipBridge","workspace.json"));
        workspace.RollbackRequested += () => _ = LoadRollbackVersionsAsync(workspace);
        workspace.RollbackTargetRequested += PromptForRollback;
        return workspace;
    }    private void OpenConversation(string? peerId=null){_panel.OpenConversation(peerId);}
    private void OpenConversationOrPanel(){_panel.ShowControlPanel();}

private void OpenReceiveDirectory()
{
    try
    {
        Directory.CreateDirectory(_receiveDirectory);
        Process.Start(new ProcessStartInfo { FileName = _receiveDirectory, UseShellExecute = true });
    }
    catch (Exception error) { ReportStatus("打开保存位置失败：" + error.Message); }
}


	private void ChangeTheme(UiThemeMode mode)
	{
		if (!_exiting)
		{
			_themeController.SelectMode(mode);
			if (!_themeController.LastSaveSucceeded)
			{
				_panel.AddEvent("主题已应用，但偏好保存失败");
			}
		}
	}

	private void OnThemeChanged()
	{
		// Theme changes originate on the UI thread, including system theme updates.
		// Recolor the existing window rather than hiding and recreating it.
		if (!_exiting)
		{
			// Capture the target colors for the tray before the panel starts
			// preparing the panel's buffered transition.
			ApplyThemeToTrayMenu();
			_panel.ApplyTheme(_themeController.Mode);

            if (_updateDialog is { IsDisposed: false }) _updateDialog.ApplyTheme();
            if (_conversation is { IsDisposed: false }) _conversation.ApplyTheme();
		}
	}

	private void ApplyThemeToTrayMenu()
	{
		_trayMenu.BackColor = Ui.Surface;
		_trayMenu.ForeColor = Ui.Ink;
		_trayMenu.Renderer = new ToolStripProfessionalRenderer(new ThemedMenuColorTable());
		foreach (ToolStripItem item in _trayMenu.Items)
		{
			if (item is ToolStripMenuItem toolStripMenuItem)
			{
				toolStripMenuItem.ForeColor = Ui.Ink;
			}
		}
	}

	private void Dispatch(Action action)
	{
		if (_exiting)
		{
			return;
		}
		try
		{
			_dispatcher.BeginInvoke(() =>
			{
				if (!_exiting)
				{
					action();
				}
			});
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void RefreshPanel()
	{
		if (!_exiting)
		{
			PeerSnapshot[] peers = _node.GetPeerSnapshots();
            _panel.RefreshWorkspace();

            _conversation?.SetTransfers(_transfers.Values.ToArray());

            _trayIcon.Text = _pending.IsEmpty ? "ClipBridge · 主动发送" : $"ClipBridge · {_pending.Count} 个待接收请求";
            UpdateTransfers();
		}
	}

	private async Task CheckForUpdatesAsync(bool manual)
	{
		if (_exiting || Volatile.Read(ref _updateInstalling) != 0 || Interlocked.CompareExchange(ref _updateCheckRunning, 1, 0) != 0)
		{
			return;
		}
		Dispatch(() =>
		{
			_panel.SetUpdateCheckState(enabled: false, "检查中...");
		});
		try
		{
			AvailableUpdate? update = await UpdateProtocol.CheckAsync(_updateLifetime.Token);
			Dispatch(() =>
			{
				if (!_exiting)
				{
					if (update == null)
					{
						_panel.SetUpdateAvailable(null, null);
						if (manual)
						{
							UpdateDialog.ShowNotice(_panel, "已是最新版本", "当前已经是最新版本，无需安装更新。");
						}
					}
					else
					{
						_panel.SetUpdateAvailable(update.Version, () => PromptForUpdate(update));
						if (manual) PromptForUpdate(update);
					}
				}
			});
		}
		catch (OperationCanceledException) when (_exiting)
		{
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			Dispatch(() =>
			{
				if (!_exiting)
				{
					ReportStatus("更新检查失败");
                    _panel.SetUpdateStatus(UpdateSidebarState.Failed, ex4.Message);
					if (manual)
					{
						UpdateDialog.ShowNotice(_panel, "更新检查未完成", ex4.Message, error: true);
					}
				}
			});
		}
		finally
		{
			Interlocked.Exchange(ref _updateCheckRunning, 0);
			Dispatch(() =>
			{
				_panel.SetUpdateCheckState(enabled: Volatile.Read(ref _updateInstalling) == 0);
			});
		}
	}

    private async Task LoadRollbackVersionsAsync(WorkspaceForm workspace)
    {
        if (_exiting || Volatile.Read(ref _updateInstalling) != 0) return;
        if (Interlocked.CompareExchange(ref _updateCheckRunning, 1, 0) != 0)
        {
            workspace.SetRollbackLoading(false, "当前有其他更新操作正在进行，请稍后重试。");
            return;
        }
        _rollbackLoadCancellation?.Cancel();
        _rollbackLoadCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        _rollbackLoadCancellation = cancellation;
        workspace.SetRollbackLoading(true);
        try
        {
            using var catalog = new GitHubReleaseCatalog();
            var candidates = await catalog.LoadAsync(UpdateProtocol.CurrentVersion, cancellation.Token).ConfigureAwait(false);
            Dispatch(() => { if (!_exiting && !workspace.IsDisposed) workspace.SetRollbackCandidates(candidates); });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            Dispatch(() => { if (!_exiting && !workspace.IsDisposed) workspace.SetRollbackLoading(false, "读取 GitHub 发布版本失败：" + error.Message); });
        }
        finally
        {
            if (ReferenceEquals(_rollbackLoadCancellation, cancellation)) _rollbackLoadCancellation = null;
            cancellation.Dispose();
            Interlocked.Exchange(ref _updateCheckRunning, 0);
        }
    }

    private void PromptForRollback(AvailableUpdate update)
    {
        if (!update.IsRollback || !Version.TryParse(update.Version, out Version? version) || version >= UpdateProtocol.CurrentVersion)
        {
            UpdateDialog.ShowNotice(_panel, "无法回退", "所选版本未通过回退验证，请重新加载 GitHub 版本列表。", error: true);
            return;
        }
        PromptForUpdate(update);
    }
    private void PromptForUpdate(AvailableUpdate update)
    {
        _panel.ShowControlPanel();
        if (_updateDialog is { IsDisposed: false }) { _updateDialog.Show(); _updateDialog.Activate(); return; }
        var dialog = new UpdateDialog(update.Version, update.Notes, update.Size, isRollback: update.IsRollback);
        _updateDialog = dialog;
        dialog.InstallRequested += () => _ = DownloadAndInstallUpdateAsync(update, dialog);
        dialog.CancelRequested += () => _updateDownload?.Cancel();
        dialog.FormClosed += (_, _) => { if (ReferenceEquals(_updateDialog, dialog)) _updateDialog = null; };
        dialog.Show(_panel);
    }

    private async Task DownloadAndInstallUpdateAsync(AvailableUpdate update, UpdateDialog dialog)
    {
        if (_conversation?.HasActiveSends==true || _files.IsBusy) { dialog.ShowFailure("请等待文件传输完成后再安装更新。"); return; }
        if (_exiting || Interlocked.Exchange(ref _updateInstalling, 1) != 0) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        _updateDownload = cancellation;
        dialog.BeginDownload();
        _panel.SetUpdateStatus(UpdateSidebarState.Installing, update.IsRollback ? "正在回退到 v" + update.Version : "正在安装 v" + update.Version);
        var progress = new Progress<UpdateProgress>(value =>
        {
            if (!_exiting && !dialog.IsDisposed && !cancellation.IsCancellationRequested) dialog.Report(value);
        });
        ReportStatus((update.IsRollback ? "正在下载历史版本 " : "正在下载更新 ") + update.Version + "…");
        string? staged = null; bool handedOff = false;
        try
        {
            staged = await SelfUpdate.StagePackageAsync(update, cancellation.Token, progress);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_exiting) return;
            dialog.Report(new UpdateProgress(UpdateStage.Waiting));
            if(_files.IsBusy || _conversation?.HasActiveSends==true)throw new IOException("有新的传输正在进行，请传输完成后再安装。");
            SelfUpdate.LaunchUpdater(staged, isRollback: update.IsRollback, rollbackVersion: update.IsRollback ? update.Version : null);
            handedOff = true;
            ReportStatus(update.IsRollback ? "历史版本已校验，独立安装窗口正在接管回退" : "更新已校验，独立安装窗口正在接管");
            dialog.Finish();
            ExitThread();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_exiting) { ReportStatus(update.IsRollback ? "已取消回退，当前版本可继续使用" : "已取消更新，当前版本可继续使用"); if (!dialog.IsDisposed) dialog.Finish(); }
        }
        catch (Exception ex)
        {
            if (!_exiting)
            {
                ReportStatus((update.IsRollback ? "回退未完成：" : "更新未完成：") + ex.Message);
                if (!dialog.IsDisposed) dialog.ShowFailure(ex.Message);
            }
        }
        finally
        {
            if (staged != null && !handedOff) SelfUpdate.DiscardStagedPackage(staged);
            if (ReferenceEquals(_updateDownload, cancellation)) _updateDownload = null;
            Interlocked.Exchange(ref _updateInstalling, 0);
            if (!_exiting) _panel.SetUpdateCheckState(enabled: true);
        }
    }

    internal void ShowAfterUpdate(bool rolledBack, Action ready, string? installedVersion = null)
    {
        Dispatch(() =>
        {
            _panel.ShowControlPanel();
            string message = !string.IsNullOrWhiteSpace(installedVersion)
                ? "已回退到 v" + installedVersion + "，主界面已自动打开"
                : rolledBack ? "更新未完成，已尝试恢复原版本" : "更新完成，已自动打开主界面";
            ReportStatus(message);
            ready();
        });
    }

    private void ChooseFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
        if (dialog.ShowDialog(_panel) == DialogResult.OK) ShowFileSend(dialog.FileNames);
    }

    private void ShowFileSend(string[] paths)
    {
        if (paths.Length == 0 || paths.Length > 256 || paths.Any(path => !File.Exists(path) && !Directory.Exists(path))) { ReportStatus("请选择 1 至 256 个文件或文件夹"); return; }
        PeerSnapshot? peer = _conversation is { Visible: true } ? _conversation.SelectedPeer : _panel.SelectedPeer;
        peer ??= _node.GetPeerSnapshots().FirstOrDefault(p => p.RecentlySeen && !string.IsNullOrWhiteSpace(p.PeerId));
        if(peer == null) { ReportStatus("请先连接并选择设备"); return; }
        OpenConversation(peer.PeerId);
        _conversation?.AddAttachments(paths);
    }

    private async Task<bool> QueueFileRequestAsync(FileOffer offer, IPAddress address, CancellationToken token)
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[offer.TransferId] = new PendingFile(offer, address, reply);
        using var registration = token.Register(() => reply.TrySetCanceled(token));
        try { return await reply.Task; }
        finally { _pending.TryRemove(offer.TransferId, out _); }
    }

    private void OpenTransfers()
    {
        if (_transfersForm == null || _transfersForm.IsDisposed)
        {
            _transfersForm = new TransfersForm(_receiveDirectory);
            _transfersForm.Decision += (id, accept) => { if (_pending.TryGetValue(id, out var pending)) pending.Reply.TrySetResult(accept); };
            _transfersForm.CancelTransfer += id => _files.Cancel(id);
        }
        UpdateTransfers();
        _transfersForm.Show(_panel);
        _transfersForm.Activate();
    }

    private void UpdateTransfers()
    {
        if (_transfers.Count > 200)
            foreach (var item in _transfers.Where(x => x.Value.State != "传输中" && !_pending.ContainsKey(x.Key)).Take(_transfers.Count - 200)) _transfers.TryRemove(item.Key, out _);
        if (_transfersForm == null || _transfersForm.IsDisposed) return;
        var rows = _pending.Select(p => new TransferRow(p.Key, "待接受 · " + p.Value.Address + " · " + string.Join("、", p.Value.Offer.Files.Select(f => f.Name)) + " · " + p.Value.Offer.Files.Sum(f => f.Size) + " 字节", true))
            .Concat(_transfers.Where(p => !_pending.ContainsKey(p.Key)).Select(p => new TransferRow(p.Key, p.Value.Direction + " · " + p.Value.State + " · " + p.Value.Completed + " / " + p.Value.Total + " 字节", false))).ToArray();
        _transfersForm.UpdateRows(rows);
    }

    private void OpenDevices() { _panel.ShowDevices();_panel.ShowControlPanel(); }

	private void ReportStatus(string status)
	{
		if (!_exiting)
		{
			_statusMenu.Text = status;
			_panel.AddEvent(status);
		}
	}


	protected override void ExitThreadCore()
	{
		if (!_exiting)
		{
			_exiting = true;
			_updateLifetime.Cancel();
            _updateDialog?.Dispose();
			_activationWait.Unregister(null);
			_themeController.ThemeChanged -= OnThemeChanged;
			_systemThemeWatcher.Dispose();
            _historyPruneScheduler?.Dispose();
			_refreshTimer.Dispose();
			_updateTimer.Dispose();
			_files.Dispose();
            _transfersForm?.Dispose();
            _conversation?.Dispose();
			_node.Dispose();
            _devices.Dispose();
			_trayIcon.Visible = false;
			_trayIcon.ContextMenuStrip?.Dispose();
			_trayIcon.Dispose();
			_panel.Dispose();
			_dispatcher.Dispose();
			_updateLifetime.Dispose();
			base.ExitThreadCore();
		}
	}
}
