using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
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
    private DevicesForm? _devicesForm;

	private readonly FileTransferNode _files;
    private readonly string _receiveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ClipBridge");
    private sealed record PendingFile(FileOffer Offer, IPAddress Address, TaskCompletionSource<bool> Reply);
    private readonly ConcurrentDictionary<string, PendingFile> _pending = new();
    private readonly ConcurrentDictionary<string, TransferProgress> _transfers = new();
    private TransfersForm? _transfersForm;

	private ControlPanelForm _panel;



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
		_panel = CreatePanel(Convert.ToHexString(_pairingKey));
		_files = new FileTransferNode(_pairingKey, _receiveDirectory, QueueFileRequestAsync, hub: _devices);
        _devices.Status += text => Dispatch(() => ReportStatus(text));
        _files.Progress += progress => { _transfers[progress.TransferId] = progress; };
        _files.StatusChanged += status => Dispatch(() => ReportStatus(status));
		_trayMenu = new ContextMenuStrip
		{
			ShowImageMargin = false
		};
		_trayMenu.Items.Add("打开控制面板", null, (object? _, EventArgs _) =>
		{
			_panel.ShowControlPanel();
		});
        _trayMenu.Items.Add("发送当前剪贴板", null, async (_, _) => await PreviewClipboardAsync());
        _trayMenu.Items.Add("发送文件", null, (_, _) => ChooseFiles());
        _trayMenu.Items.Add("文件请求与传输记录", null, (_, _) => OpenTransfers());
		_trayMenu.Items.Add("附近设备与配对", null, (_, _) => OpenDevices());
        _trayMenu.Items.Add("兼容旧版：配对密钥", null, (object? _, EventArgs _) =>
		{
			_panel.FocusPairing();
		});
		_trayMenu.Items.Add("添加设备 IP", null, (object? _, EventArgs _) =>
		{
			_panel.FocusManualPeer();
		});
		_statusMenu = new ToolStripMenuItem("就绪 · 主动发送模式")
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
			Text = "ClipBridge · 主动发送",
			Icon = AppIcon.Load(),
			ContextMenuStrip = _trayMenu,
			Visible = true
		};
		_trayIcon.DoubleClick += (object? _, EventArgs _) =>
		{
			_panel.ShowControlPanel();
		};
		_node.RemoteTextReceived += SetClipboardText;
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
			Dispatch(_panel.ShowControlPanel);
		}, null, -1, executeOnlyOnce: false);
		RefreshPanel();

	}

	private ControlPanelForm CreatePanel(string pairingKey, bool emitStartupEvent = true)
	{
		ControlPanelForm controlPanelForm = new ControlPanelForm(pairingKey, _themeController.Mode, emitStartupEvent);
		controlPanelForm.ManageDevicesRequested += OpenDevices;
        controlPanelForm.SendClipboardRequested += async () => await PreviewClipboardAsync();
        controlPanelForm.SendFilesRequested += ShowFileSend;
        controlPanelForm.ReviewIncomingRequested += OpenTransfers;
		controlPanelForm.CopyKeyRequested += CopyPairingKey;
		controlPanelForm.AddPeerRequested += AddManualPeer;
		controlPanelForm.RemovePeerRequested += RemoveManualPeer;
		controlPanelForm.RefreshRequested += RefreshPanel;
		controlPanelForm.CheckUpdateRequested += () =>
		{
			_ = CheckForUpdatesAsync(manual: true);
		};
		controlPanelForm.ThemeChangeRequested += ChangeTheme;
		controlPanelForm.FormClosed += OnPanelFormClosed;
		return controlPanelForm;
	}

	private void OnPanelFormClosed(object? sender, FormClosedEventArgs e)
	{
		if (!_exiting && sender == _panel)
		{
			ExitThread();
		}
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
			_panel.UpdatePeers(_node.GetPeerSnapshots());
            _panel.SetPendingFiles(_pending.Count);
            _panel.SetPairingCount(_devices.Pending.Count);
            _trayIcon.Text = _pending.IsEmpty ? "ClipBridge · 主动发送" : $"ClipBridge · {_pending.Count} 个待接收请求";
            UpdateTransfers();
		}
	}

	private async Task CheckForUpdatesAsync(bool manual)
	{
		if (_exiting || Interlocked.Exchange(ref _updateCheckRunning, 1) != 0)
		{
			return;
		}
		Dispatch(() =>
		{
			_panel.SetUpdateCheckState(enabled: false, "检查中...");
		});
		try
		{
			AvailableUpdate update = await UpdateProtocol.CheckAsync(_updateLifetime.Token);
			Dispatch(() =>
			{
				if (!_exiting)
				{
					if ((object)update == null)
					{
						if (manual)
						{
							MessageBox.Show(_panel, "当前已经是最新版本。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
						}
					}
					else
					{
						if (manual) PromptForUpdate(update);
                        else ReportStatus("发现新版本 " + update.Version + "，可在控制面板主动检查更新");
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
					if (manual)
					{
						MessageBox.Show(_panel, "暂时无法检查更新。\n" + ex4.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					}
				}
			});
		}
		finally
		{
			Interlocked.Exchange(ref _updateCheckRunning, 0);
			Dispatch(() =>
			{
				_panel.SetUpdateCheckState(enabled: true);
			});
		}
	}

	private void PromptForUpdate(AvailableUpdate update)
	{
		string text = (string.IsNullOrWhiteSpace(update.Notes) ? "无更新说明。" : update.Notes.Trim());
		_panel.ShowControlPanel();
		if (MessageBox.Show(_panel, "发现新版本 " + update.Version + "。\n\n" + text + "\n\n是否下载并自动安装？应用会重启一次。", "发现更新", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
		{
			_ = DownloadAndInstallUpdateAsync(update);
		}
	}

	private async Task DownloadAndInstallUpdateAsync(AvailableUpdate update)
	{
		if (_exiting || Interlocked.Exchange(ref _updateInstalling, 1) != 0)
		{
			return;
		}
		ReportStatus("正在下载更新 " + update.Version + "...");
		try
		{
			string stagedDirectory = await SelfUpdate.StagePackageAsync(update, _updateLifetime.Token);
			Dispatch(() =>
			{
				if (!_exiting)
				{
					try
					{
						SelfUpdate.LaunchUpdater(stagedDirectory);
					}
					catch (Exception ex)
					{
						Interlocked.Exchange(ref _updateInstalling, 0);
						ReportStatus("无法启动更新程序");
						MessageBox.Show(_panel, "更新未安装。\n" + ex.Message, "自动更新", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return;
					}
					ReportStatus("更新已校验，正在重启安装");
					ExitThread();
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
			Interlocked.Exchange(ref _updateInstalling, 0);
			Dispatch(() =>
			{
				if (!_exiting)
				{
					ReportStatus("更新下载或校验失败");
					MessageBox.Show(_panel, "更新未安装。\n" + ex4.Message, "自动更新", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			});
		}
	}

    private async Task PreviewClipboardAsync()
    {
        string text = "";
        for (int i = 0; i < 6; i++)
        {
            try { text = Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : ""; break; }
            catch (ExternalException) { if (i == 5) { ReportStatus("剪贴板正忙，请稍后重试"); return; } await Task.Delay(50); }
        }
        if (string.IsNullOrEmpty(text)) { ReportStatus("剪贴板没有纯文本"); return; }
        if (string.Equals(text.Trim(), Convert.ToHexString(_pairingKey), StringComparison.OrdinalIgnoreCase)) { ReportStatus("不能发送配对密钥"); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(text) > SharedProtocol.MaxTextBytes) { ReportStatus("文本超过 256 KiB"); return; }
        string captured = text;
        var window = new ManualSendForm(_node.GetPeerSnapshots(), "发送剪贴板 · " + System.Text.Encoding.UTF8.GetByteCount(text) + " 字节", text, async (address, token) =>
        { if (!await _node.SendTextAsync(address, captured, token)) throw new IOException("发送失败，请检查目标设备和网络"); });
        window.Show(_panel);
    }

    private void ChooseFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
        if (dialog.ShowDialog(_panel) == DialogResult.OK) ShowFileSend(dialog.FileNames);
    }

    private void ShowFileSend(string[] paths)
    {
        if (paths.Length == 0 || paths.Length > 256 || paths.Any(path => !File.Exists(path))) { ReportStatus("请选择 1 至 256 个普通文件，暂不支持文件夹"); return; }
        var window = new ManualSendForm(_node.GetPeerSnapshots(), "发送文件", string.Join(Environment.NewLine, paths.Select(Path.GetFileName)), (address, token) => _files.SendAsync(address, paths, token));
        window.Show(_panel);
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

	private void OpenDevices()
    {
        if (_devicesForm == null || _devicesForm.IsDisposed) _devicesForm = new DevicesForm(_devices);
        _devicesForm.Show(_panel); _devicesForm.Activate();
    }

	private void CopyPairingKey()
	{
		string text = Convert.ToHexString(_pairingKey);
		try
		{
			Clipboard.SetText(text);
			ReportStatus("配对密钥已复制");
		}
		catch (ExternalException)
		{
			ReportStatus("剪贴板正忙，请稍后重试复制密钥");
		}
	}

	private void AddManualPeer(string input)
	{
		if (!IPAddress.TryParse(input.Trim(), out IPAddress address) || !SharedProtocol.IsLocalAddress(address))
		{
			ReportStatus("请输入有效的局域网 IPv4 地址");
			return;
		}
		try
		{
			_node.AddManualPeer(address);
			_panel.ClearManualInput();
			RefreshPanel();
		}
		catch (InvalidOperationException)
		{
			ReportStatus("设备地址数量已达上限");
		}
	}

	private void RemoveManualPeer(string input)
	{
		if (IPAddress.TryParse(input, out IPAddress address))
		{
			_node.RemoveManualPeer(address);
			RefreshPanel();
		}
	}

	private void ReportStatus(string status)
	{
		if (!_exiting)
		{
			_statusMenu.Text = status;
			_panel.AddEvent(status);
		}
	}

    private async void SetClipboardText(string text)
    {
        for (int i = 0; i < 6 && !_exiting; i++)
        {
            try { Clipboard.SetText(text, TextDataFormat.UnicodeText); ReportStatus("已接收并写入剪贴板"); return; }
            catch (ExternalException) { if (i < 5) await Task.Delay(50); }
        }
        if (!_exiting) ReportStatus("剪贴板正忙，本次接收未写入");
    }

	protected override void ExitThreadCore()
	{
		if (!_exiting)
		{
			_exiting = true;
			_updateLifetime.Cancel();
			_activationWait.Unregister(null);
			_themeController.ThemeChanged -= OnThemeChanged;
			_systemThemeWatcher.Dispose();
			_refreshTimer.Dispose();
			_updateTimer.Dispose();
			_files.Dispose();
            _transfersForm?.Dispose();
			_node.Dispose();
            _devices.Dispose(); _devicesForm?.Dispose();
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
