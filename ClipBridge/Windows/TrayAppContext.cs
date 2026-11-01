using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class TrayAppContext : ApplicationContext
{
	private readonly Control _dispatcher = new Control();

	private readonly NotifyIcon _trayIcon;

	private readonly ContextMenuStrip _trayMenu;

	private readonly SyncNode _node;

	private readonly WindowsClipboardListener _clipboardListener;

	private ControlPanelForm _panel;

	private readonly ToolStripMenuItem _syncMenu;

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

	private bool _enabled;

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
		_node = new SyncNode(_pairingKey, Dispatch);
		_panel = CreatePanel(Convert.ToHexString(_pairingKey));
		_clipboardListener = new WindowsClipboardListener(_node.OnLocalClipboardTextChanged, () => _enabled);
		_trayMenu = new ContextMenuStrip
		{
			ShowImageMargin = false
		};
		_trayMenu.Items.Add("打开控制面板", null, (object? _, EventArgs _) =>
		{
			_panel.ShowControlPanel();
		});
		_syncMenu = new ToolStripMenuItem("开启剪贴板同步", null, (object? _, EventArgs _) =>
		{
			SetSyncEnabled(!_enabled);
		});
		_trayMenu.Items.Add(_syncMenu);
		_trayMenu.Items.Add("配对密钥", null, (object? _, EventArgs _) =>
		{
			_panel.FocusPairing();
		});
		_trayMenu.Items.Add("添加设备 IP", null, (object? _, EventArgs _) =>
		{
			_panel.FocusManualPeer();
		});
		_statusMenu = new ToolStripMenuItem("同步已暂停")
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
			Text = "ClipBridge · 已暂停",
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
		_clipboardListener.ReadFailed += () =>
		{
			ReportStatus("剪贴板持续被占用，请重新复制");
		};
		_themeController.ThemeChanged += OnThemeChanged;
		_refreshTimer.Tick += (object? _, EventArgs _) =>
		{
			RefreshPanel();
		};
		_refreshTimer.Start();
		_updateTimer = UpdateCheckScheduler.Start(() =>
		{
			CheckForUpdatesAsync(manual: false);
		});
		_activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (object? _, bool _) =>
		{
			Dispatch(_panel.ShowControlPanel);
		}, null, -1, executeOnlyOnce: false);
		RefreshPanel();
		_panel.ShowControlPanel();
	}

	private ControlPanelForm CreatePanel(string pairingKey, bool emitStartupEvent = true)
	{
		ControlPanelForm controlPanelForm = new ControlPanelForm(pairingKey, _themeController.Mode, emitStartupEvent);
		controlPanelForm.SyncStateRequested += SetSyncEnabled;
		controlPanelForm.CopyKeyRequested += CopyPairingKey;
		controlPanelForm.AddPeerRequested += AddManualPeer;
		controlPanelForm.RemovePeerRequested += RemoveManualPeer;
		controlPanelForm.RefreshRequested += RefreshPanel;
		controlPanelForm.CheckUpdateRequested += () =>
		{
			CheckForUpdatesAsync(manual: true);
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
						PromptForUpdate(update);
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
			DownloadAndInstallUpdateAsync(update);
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

	private void SetSyncEnabled(bool enabled)
	{
		if (_enabled == enabled)
		{
			return;
		}
		if (enabled)
		{
			_panel.ShowControlPanel();
			if (MessageBox.Show(_panel, "开启后将自动读取新复制的纯文本，并与持有相同密钥的设备双向同步。文本可能包含密码或验证码，不能保证自动识别敏感内容。配对密钥不会被自动同步。可随时在面板或托盘中暂停。是否开启？", "开启剪贴板同步", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) != DialogResult.Yes)
			{
				_panel.SetSyncState(enabled: false);
				return;
			}
		}
		_enabled = enabled;
		_node.SetEnabled(enabled);
		_panel.SetSyncState(enabled);
		_syncMenu.Text = (enabled ? "暂停剪贴板同步" : "开启剪贴板同步");
		_trayIcon.Text = (enabled ? "ClipBridge · 同步中" : "ClipBridge · 已暂停");
	}

	private void CopyPairingKey()
	{
		string text = Convert.ToHexString(_pairingKey);
		_clipboardListener.SuppressNextText(text);
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

	private void SetClipboardText(string text)
	{
		if (!_enabled)
		{
			return;
		}
		try
		{
			_node.MarkRemoteTextApplied(text);
			Clipboard.SetText(text, TextDataFormat.UnicodeText);
			ReportStatus("已接收并写入剪贴板");
		}
		catch (ExternalException)
		{
			_node.OnLocalClipboardTextChanged(string.Empty);
			ReportStatus("剪贴板正忙，本次接收未写入");
		}
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
			_clipboardListener.Dispose();
			_node.Dispose();
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
