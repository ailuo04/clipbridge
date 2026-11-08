using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ControlPanelForm : Form
{
	private sealed class EventEntry
	{
		internal string Time { get; }

		internal string Message { get; }

		internal double Progress { get; set; }

		internal EventEntry(string time, string message)
		{
			Time = time;
			Message = message;
		}

		public override string ToString()
		{
			return Time + "  " + Message;
		}
	}

	private const int WmNcCalcSize = 0x0083;

	private const int WmNcHitTest = 132;

	private const int WmSysCommand = 0x0112;

	private const int ScMinimize = 0xF020;

	private const int ScMaximize = 0xF030;

	private const int ScRestore = 0xF120;

	private const int SwRestore = 9;

	private const int WsPopup = unchecked((int)0x80000000);

	private const int WsOverlappedWindow = 0x00CF0000;

	private const int WmNcLeftButtonDown = 161;

	private const int HtClient = 1;

	private const int HtCaption = 2;

	private const int HtLeft = 10;

	private const int HtRight = 11;

	private const int HtTop = 12;

	private const int HtTopLeft = 13;

	private const int HtTopRight = 14;

	private const int HtBottom = 15;

	private const int HtBottomLeft = 16;

	private const int HtBottomRight = 17;

	private const int DwmWindowCornerPreference = 33;

	private const int GwlExStyle = -20;

	private const int WsExComposited = 33554432;

	// Content rows of the network card (34 + 28 + 70 + 34 + 106) plus its
	// 18 px top and 24 px bottom padding, in 96-dpi logical pixels.
	private const int NetworkSurfaceHeight = 314;

	private readonly ModernButton _sendClipboard = new ModernButton("发送剪贴板", ButtonTone.Primary, "\ue8c8");

	private readonly ModernButton _sendFiles = new ModernButton("发送文件", ButtonTone.Secondary, "\ue8b7");

    private readonly ModernButton _pendingFiles = new ModernButton("传输记录", ButtonTone.Secondary);

	private readonly WindowCaptionButton _minimizeButton = new WindowCaptionButton
	{
		Glyph = "\ue921",
		AccessibleName = "最小化"
	};

	private readonly WindowCaptionButton _maximizeButton = new WindowCaptionButton
	{
		Glyph = "\ue922",
		AccessibleName = "最大化"
	};

	private readonly WindowCaptionButton _closeButton = new WindowCaptionButton
	{
		Glyph = "\ue8bb",
		AccessibleName = "关闭到托盘",
		CloseAction = true
	};

	private readonly AppHeaderSurface _headerSurface = new AppHeaderSurface();

	private readonly FooterBarSurface _footerSurface = new FooterBarSurface();

	private readonly ConnectionSummaryBar _connectionSummary = new ConnectionSummaryBar();

	private readonly TextBoxShell _keyShell = new TextBoxShell(new Font("Consolas", 10f), password: true);

	private readonly ModernCheckBox _revealKey = new ModernCheckBox
	{
		Text = "显示密钥"
	};

	private readonly TextBox _addresses = new TextBox();

	private readonly TextBoxShell _peerIpShell = new TextBoxShell(Ui.Font(9f));

	private readonly DeviceListView _devices = new DeviceListView();

	private readonly CountPill _deviceCount = new CountPill();

	private readonly EmptyDeviceState _empty = new EmptyDeviceState();

	private readonly ModernButton _copyKey = new ModernButton("复制密钥", ButtonTone.Primary, "\ue8c8");

	private readonly ModernButton _refresh = new ModernButton("刷新", ButtonTone.Secondary, "\ue72c");

	private readonly ModernButton _remove = new ModernButton("移除", ButtonTone.Secondary, "\ue74d");

	private readonly ModernButton _addPeer = new ModernButton("添加设备", ButtonTone.Primary, "\ue710");

	private readonly ModernButton _checkUpdates = new ModernButton("检查更新", ButtonTone.Ghost, "\ue895");

	private readonly ThemeSelector _themeSelector = new ThemeSelector();

	private readonly Label _status = TextLabel("就绪 · 主动发送模式", 9f, bold: false, Ui.Muted);

	private readonly ListBox _events = new ListBox();

	private readonly ToolTip _tips = new ToolTip();

	private readonly RoundedSurface _networkSurface = new RoundedSurface();

	private readonly RoundedSurface _devicesSurface = new RoundedSurface();

	private readonly GlassEventSurface _eventsSurface = new GlassEventSurface();

	private readonly Timer _eventMotionTimer = new Timer
	{
		Interval = 16
	};

	private readonly Timer _introTimer = new Timer
	{
		Interval = 15
	};

	private readonly ThemeTransitionOverlay _themeTransitionOverlay = new ThemeTransitionOverlay();

	private readonly List<ThemeColorBinding> _themeBindings = new List<ThemeColorBinding>();

	private UiPalette _renderedPalette = null!;

	private UiColorTheme _renderedTheme;

	private readonly Timer _networkRefreshTimer = new Timer
	{
		Interval = 350
	};

	private readonly Font _eventTimeFont = new Font("Consolas", 8.6f);



	private string _deviceSignature = "uninitialized";

	private string _lastEvent = "";

	private double _introProgress;

	private bool _introStarted;

	private bool _startupCompositionActive = true;

	private bool _networkSubscriptionActive;

	private UiThemeMode _themeMode;

	[System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
	public new bool DoubleBuffered
	{
		get
		{
			return base.DoubleBuffered;
		}
		set
		{
			base.DoubleBuffered = value;
		}
	}

	public UiThemeMode ThemeMode => _themeMode;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ClassStyle |= 0x00020000;
			// Keep the native window styles so Windows owns window transitions.
			// WM_NCCALCSIZE removes the frame while retaining those behaviors.
			createParams.Style = (createParams.Style & ~WsPopup) | WsOverlappedWindow;
			createParams.ExStyle |= 0x02000000;
			return createParams;
		}
	}

	public event Action? SendClipboardRequested;

    public event Action<string[]>? SendFilesRequested;

    public event Action? ReviewIncomingRequested;

	private readonly ModernButton _pairDevices = new ModernButton("附近设备与配对", ButtonTone.Primary);
    public event Action? ManageDevicesRequested;
    public void SetPairingCount(int count) { _pairDevices.Text = count > 0 ? $"设备配对 · {count} 项待确认" : "附近设备与配对"; }

	public event Action? CopyKeyRequested;

	public event Action<string>? AddPeerRequested;

	public event Action<string>? RemovePeerRequested;

	public event Action? RefreshRequested;

	public event Action? CheckUpdateRequested;

	public event Action<UiThemeMode>? ThemeChangeRequested;

	public ControlPanelForm(string pairingKey)
		: this(pairingKey, UiThemeMode.Light, emitStartupEvent: true)
	{
	}

	public ControlPanelForm(string pairingKey, UiThemeMode themeMode)
		: this(pairingKey, themeMode, emitStartupEvent: true)
	{
	}

	public ControlPanelForm(string pairingKey, UiThemeMode themeMode, bool emitStartupEvent)
	{
		_themeMode = themeMode;
		Text = "ClipBridge · 控制面板";
		Name = "ClipBridgeControlPanel";
		AccessibleName = "ClipBridge 控制面板";
		Icon = AppIcon.Load();
		AutoScaleMode = AutoScaleMode.Dpi;
		Font = Ui.Font(9f);
		BackColor = Ui.Canvas;
		ForeColor = Ui.Ink;
		FormBorderStyle = FormBorderStyle.None;
		ClientSize = new Size(1180, 800);
		MinimumSize = new Size(980, 700);
		StartPosition = FormStartPosition.CenterScreen;
		MaximizeBox = true;
		DoubleBuffered = true;
		Opacity = 1.0;
		_pairDevices.Click += (_, _) => ManageDevicesRequested?.Invoke();
        ConfigureControls(pairingKey);
		_themeSelector.SetMode(_themeMode);
		Controls.Add(BuildLayout());
		_renderedPalette = UiPalette.Capture();
		_renderedTheme = Ui.CurrentTheme;
		CaptureThemeBindings(this, inHeader: false);
		Controls.Add(_themeTransitionOverlay);
		WireInteractions();
		_footerSurface.SetActive(true);
		RefreshNetworkAddresses();
		if (emitStartupEvent)
		{
			AddEvent("应用已启动，只有点击发送才会传输剪贴板");
		}
		CreateNestedHandles(this);
		SubscribeNetworkChanges();
		PerformLayout();
	}

	private void ConfigureControls(string pairingKey)
	{
        _sendClipboard.Name = "SendClipboardButton";
        _sendClipboard.AccessibleName = "发送剪贴板";
        _sendFiles.Name = "SendFilesButton";
        _sendFiles.AccessibleName = "发送文件";
        _sendClipboard.Margin = new Padding(0, 0, 8, 0);
        _sendFiles.Margin = new Padding(0, 0, 8, 0);
        _pendingFiles.Name = "ReviewTransfersButton";
        _pendingFiles.Click += (_, _) => ReviewIncomingRequested?.Invoke();
		_minimizeButton.DarkBackground = Ui.HeaderIsDark;
		_maximizeButton.DarkBackground = Ui.HeaderIsDark;
		_closeButton.DarkBackground = Ui.HeaderIsDark;
		_themeSelector.Name = "ThemeSelector";
		_themeSelector.Anchor = AnchorStyles.Right;
		_themeSelector.Margin = Padding.Empty;
		_themeSelector.ModeChanged += mode =>
		{
			_themeMode = mode;
			ThemeChangeRequested?.Invoke(mode);
		};
		_checkUpdates.Name = "CheckUpdatesButton";
		_checkUpdates.Anchor = AnchorStyles.Right;
		_checkUpdates.Margin = new Padding(0, 0, 12, 0);
		_checkUpdates.Click += (object? _, EventArgs _) => CheckUpdateRequested?.Invoke();
		_keyShell.Dock = DockStyle.Fill;
		_keyShell.Name = "PairingKeyShell";
		_keyShell.Margin = new Padding(0, 0, 0, 0);
		_keyShell.Editor.Text = pairingKey;
		_keyShell.Editor.ReadOnly = true;
		_keyShell.Editor.AccessibleName = "配对密钥";
		_revealKey.AccessibleName = "显示配对密钥";
		_revealKey.Margin = Padding.Empty;
		_copyKey.Click += (object? _, EventArgs _) =>
		{
			CopyKeyRequested?.Invoke();
		};
		_addresses.ReadOnly = true;
		_addresses.Multiline = true;
		_addresses.WordWrap = false;
		_addresses.BorderStyle = BorderStyle.None;
		_addresses.Dock = DockStyle.Fill;
		_addresses.BackColor = Ui.FieldSurface;
		_addresses.ForeColor = Ui.Ink;
		_addresses.Font = new Font("Consolas", 9.3f);
		_addresses.Margin = new Padding(0, 3, 0, 3);
		_addresses.AccessibleName = "本机局域网地址";
		_peerIpShell.Dock = DockStyle.Fill;
		_peerIpShell.Margin = new Padding(0, 6, 12, 6);
		_peerIpShell.Editor.PlaceholderText = "192.168.1.20";
		_peerIpShell.Editor.AccessibleName = "手动设备 IP";
		_peerIpShell.Prefix = "手动 IP";
		_deviceCount.Anchor = AnchorStyles.Left;
		_deviceCount.Margin = new Padding(12, 0, 0, 16);
		_remove.Enabled = false;
		ConfigureDevices();
		ConfigureEvents();
		_status.AutoEllipsis = true;
		_status.Dock = DockStyle.Fill;
		_status.AutoSize = false;
		_status.TextAlign = ContentAlignment.MiddleLeft;
		_minimizeButton.Click += (object? _, EventArgs _) =>
		{
			PostMessage(Handle, WmSysCommand, ScMinimize, IntPtr.Zero);
		};
		_maximizeButton.Click += (object? _, EventArgs _) =>
		{
			ToggleMaximize();
		};
		_closeButton.Click += (object? _, EventArgs _) =>
		{
			Close();
		};
		_tips.SetToolTip(_copyKey, "复制配对密钥");
		_tips.SetToolTip(_keyShell, "仅与可信设备共享");
		_tips.SetToolTip(_sendClipboard, "预览后向指定设备发送当前文字");
		_tips.SetToolTip(_addresses, "本机可用的 IPv4 局域网地址");
		_tips.SetToolTip(_refresh, "重新读取网络地址并刷新设备");
		_tips.SetToolTip(_remove, "移除选中的手动地址");
		_tips.SetToolTip(_addPeer, "通过局域网 IP 手动添加设备");
		_tips.SetToolTip(_checkUpdates, "检查 GitHub 上的签名更新清单");
		_tips.SetToolTip(_minimizeButton, "最小化到任务栏");
		_tips.SetToolTip(_maximizeButton, "最大化或还原窗口");
		_tips.SetToolTip(_closeButton, "关闭面板并留在托盘运行");
	}

	private Control BuildLayout()
	{
		Panel panel = new Panel
		{
			Name = "WorkspaceRoot",
			Dock = DockStyle.Fill,
			BackColor = Ui.Canvas
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = Padding.Empty,
			Margin = Padding.Empty,
			BackColor = Ui.Canvas
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_connectionSummary.Name = "ConnectionSummaryBar";
		_connectionSummary.Dock = DockStyle.Fill;
		_connectionSummary.Margin = Padding.Empty;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Name = "ResponsiveContentGrid",
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Padding = new Padding(22, 18, 22, 16),
			Margin = Padding.Empty,
			BackColor = Ui.Canvas
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		TableLayoutPanel leftColumn = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = new Padding(0, 0, 12, 0),
			BackColor = Ui.Canvas
		};
		leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 68f));
		leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));
		leftColumn.Controls.Add(BuildNetwork(), 0, 0);
		leftColumn.Controls.Add(BuildEvents(), 0, 1);
		leftColumn.SizeChanged += (object? _, EventArgs _) =>
		{
			RowStyle rowStyle = leftColumn.RowStyles[0];
			rowStyle.SizeType = SizeType.Absolute;
			// The network card is sized to its content (see NetworkContentHeight) so no
			// dead space is left under the key buttons; spare height goes to the log.
			rowStyle.Height = Math.Min((float)leftColumn.ClientSize.Height * 0.68f, LogicalToDeviceUnits(NetworkSurfaceHeight)) + (float)_networkSurface.Margin.Vertical;
			leftColumn.RowStyles[1].SizeType = SizeType.Percent;
			leftColumn.RowStyles[1].Height = 100f;
		};
		Control control = BuildFooter();
		tableLayoutPanel2.Controls.Add(leftColumn, 0, 0);
		tableLayoutPanel2.Controls.Add(BuildDevices(), 1, 0);
		tableLayoutPanel2.Controls.Add(control, 0, 1);
		tableLayoutPanel2.SetColumnSpan(control, 2);
		tableLayoutPanel.Controls.Add(BuildHeader(), 0, 0);
		tableLayoutPanel.Controls.Add(_connectionSummary, 0, 1);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 2);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Control BuildHeader()
	{
		_headerSurface.Dock = DockStyle.Fill;
		_headerSurface.Name = "AppHeaderSurface";
		_headerSurface.Margin = Padding.Empty;
		// Bottom padding keeps the child grid off the hairline and accent fade
		// painted by AppHeaderSurface, which the opaque grid used to cover.
		_headerSurface.Padding = new Padding(8, 0, 0, 2);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 8,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = new Padding(14, 0, 4, 0),
			BackColor = Ui.Header
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 29f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Margin = Padding.Empty,
			BackColor = Ui.Header
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		BrandMark control = new BrandMark
		{
			Name = "BrandMark",
			Size = new Size(48, 48),
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, 12, 0)
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Name = "BrandText",
			Anchor = AnchorStyles.Left | AnchorStyles.Right,
			Size = new Size(200, 56),
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			BackColor = Ui.Header
		};
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		Label label = TextLabel("ClipBridge", 14f, bold: true, Ui.HeaderText);
		label.Name = "BrandTitle";
		label.Dock = DockStyle.Fill;
		label.AutoSize = false;
		label.AutoEllipsis = true;
		label.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel3.Controls.Add(label, 0, 0);
		Label label2 = TextLabel(Environment.MachineName + "  ·  v" + UpdateProtocol.CurrentVersion.ToString(3), 8.4f, bold: false, Ui.HeaderMuted);
		label2.Name = "BrandSubtitle";
		label2.Dock = DockStyle.Fill;
		label2.AutoSize = false;
		label2.AutoEllipsis = true;
		label2.TextAlign = ContentAlignment.MiddleLeft;
		tableLayoutPanel3.Controls.Add(label2, 0, 1);
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel3, 1, 0);
		_sendClipboard.Anchor = AnchorStyles.Right;
		_sendFiles.Anchor = AnchorStyles.Right;
		Panel control2 = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(14, 15, 14, 15),
			BackColor = Ui.ChromeLine
		};
		_minimizeButton.Dock = DockStyle.Fill;
		_maximizeButton.Dock = DockStyle.Fill;
		_closeButton.Dock = DockStyle.Fill;
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(_checkUpdates, 1, 0);
		tableLayoutPanel.Controls.Add(_sendClipboard, 2, 0);
		tableLayoutPanel.Controls.Add(_sendFiles, 3, 0);
		tableLayoutPanel.Controls.Add(control2, 4, 0);
		tableLayoutPanel.Controls.Add(_minimizeButton, 5, 0);
		tableLayoutPanel.Controls.Add(_maximizeButton, 6, 0);
		tableLayoutPanel.Controls.Add(_closeButton, 7, 0);
		_headerSurface.Controls.Add(tableLayoutPanel);
		WireWindowDrag(_headerSurface);
		WireWindowDrag(tableLayoutPanel);
		WireWindowDrag(tableLayoutPanel2);
		WireWindowDrag(tableLayoutPanel3);
		WireWindowDrag(control);
		WireWindowDrag(label);
		WireWindowDrag(label2);
		return _headerSurface;
	}

	private Control BuildNetwork()
	{
		_networkSurface.Dock = DockStyle.Fill;
		_networkSurface.Name = "ClipBridgeSurface";
		_networkSurface.CornerRadius = 14;
		_networkSurface.FillColor = Ui.SurfaceIce;
		_networkSurface.Margin = new Padding(0, 0, 0, 12);
		// The painted card ends 6 px above the control bottom (shadow room), so the
		// bottom padding is larger to keep the visible inset even with the top.
		_networkSurface.Padding = new Padding(22, 18, 22, 24);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 5,
			Margin = Padding.Empty,
			BackColor = Ui.SurfaceIceBody
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = Row(Ui.SurfaceIceBody);
		tableLayoutPanel2.ColumnCount = 3;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Label control = GlyphLabel("\ue72e", 11f, Ui.Accent);
		Label label = TextLabel("网络与安全", 14f, bold: true);
		label.Dock = DockStyle.Fill;
		label.AutoSize = false;
		label.TextAlign = ContentAlignment.MiddleLeft;
		Label label2 = TextLabel("LOCAL LAN", 8.2f, bold: true, Ui.Faint);
		label2.Anchor = AnchorStyles.Right;
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(label, 1, 0);
		tableLayoutPanel2.Controls.Add(label2, 2, 0);
		Label label3 = TextLabel("本机网络", 9.5f, bold: true, Ui.Muted);
		label3.Dock = DockStyle.Fill;
		label3.TextAlign = ContentAlignment.MiddleLeft;
		_addresses.Margin = Padding.Empty;
		InsetField addressField = new InsetField
		{
			Name = "AddressField",
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 2, 0, 6)
		};
		addressField.Controls.Add(_addresses);
		PortTags label4 = new PortTags
		{
			Name = "PortTags",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty
		};
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(label3, 0, 1);
		tableLayoutPanel.Controls.Add(addressField, 0, 2);
		tableLayoutPanel.Controls.Add(label4, 0, 3);
		tableLayoutPanel.Controls.Add(BuildPairing(), 0, 4);
		_networkSurface.Controls.Add(tableLayoutPanel);
		return _networkSurface;
	}

	private Control BuildPairing()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Name = "PairingSection";
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.BackColor = Ui.SurfaceIceBody;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowCount = 4;
		TableLayoutPanel tableLayoutPanel2 = Row(Ui.SurfaceIceBody);
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		// Same small muted heading as "本机网络" so both sub-sections read as peers.
		Label label = TextLabel("安全密钥", 9.5f, bold: true, Ui.Muted);
		label.Dock = DockStyle.Fill;
		label.AutoSize = false;
		label.TextAlign = ContentAlignment.MiddleLeft;
		Label label2 = TextLabel("AES-256-GCM", 8.1f, bold: true, Ui.Muted);
		label2.Anchor = AnchorStyles.Right;
		tableLayoutPanel2.Controls.Add(label, 0, 0);
		tableLayoutPanel2.Controls.Add(label2, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		_keyShell.Margin = new Padding(0, 2, 0, 6);
		tableLayoutPanel.Controls.Add(_keyShell, 0, 1);
		TableLayoutPanel tableLayoutPanel3 = Row(Ui.SurfaceIceBody);
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		_revealKey.Anchor = AnchorStyles.Left;
		_copyKey.Anchor = AnchorStyles.Right;
		tableLayoutPanel3.Controls.Add(_revealKey, 0, 0);
		tableLayoutPanel3.Controls.Add(_copyKey, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 2);
		return tableLayoutPanel;
	}

	private Control BuildDevices()
	{
		_devicesSurface.Dock = DockStyle.Fill;
		_devicesSurface.Name = "DeviceWorkspaceSurface";
		_devicesSurface.CornerRadius = 14;
		_devicesSurface.FillColor = Ui.SurfaceMint;
		_devicesSurface.Margin = new Padding(0, 0, 0, 12);
		_devicesSurface.Padding = new Padding(22, 16, 22, 18);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			Margin = Padding.Empty,
			BackColor = Ui.SurfaceMintBody
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		TableLayoutPanel tableLayoutPanel2 = Row(Ui.SurfaceMintBody);
		tableLayoutPanel2.ColumnCount = 6;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		IconTile tile = new IconTile
		{
			Name = "DeviceHeaderIcon",
			Glyph = "\uE772",
			Anchor = AnchorStyles.Left,
			Margin = new Padding(0, 0, 14, 0)
		};
		TableLayoutPanel titleBlock = new TableLayoutPanel
		{
			Name = "DeviceHeaderText",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			BackColor = Ui.SurfaceMintBody
		};
		Label label = TextLabel("设备", 14f, bold: true);
		label.Margin = Padding.Empty;
		Label subtitle = TextLabel("本机 " + Environment.MachineName + " · 局域网自动发现", 8.4f, bold: false, Ui.Muted);
		subtitle.Margin = new Padding(0, 1, 0, 0);
		titleBlock.Controls.Add(label, 0, 0);
		titleBlock.Controls.Add(subtitle, 0, 1);
		tableLayoutPanel2.Controls.Add(tile, 0, 0);
		tableLayoutPanel2.Controls.Add(titleBlock, 1, 0);
		tableLayoutPanel2.Controls.Add(_deviceCount, 2, 0);
		_refresh.Anchor = AnchorStyles.Right;
		_remove.Anchor = AnchorStyles.Right;
		_refresh.Margin = new Padding(0, 0, 8, 0);
		_remove.Margin = Padding.Empty;
		_pendingFiles.Anchor = AnchorStyles.Right;
        _pendingFiles.Margin = new Padding(0, 0, 8, 0);

        tableLayoutPanel2.Controls.Add(_refresh, 4, 0);
		tableLayoutPanel2.Controls.Add(_remove, 5, 0);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 6, 0, 8),
			BackColor = Ui.SurfaceMintBody
		};
		_empty.Name = "EmptyDeviceState";
		_empty.Visible = true;
		_empty.Margin = new Padding(0, 12, 0, 12);
		_empty.Dock = DockStyle.Fill;
		panel.Controls.Add(_devices);
		panel.Controls.Add(_empty);
		_empty.BringToFront();
		TableLayoutPanel tableLayoutPanel3 = Row(Ui.SurfaceMintBody);
		tableLayoutPanel3.Margin = new Padding(0, 4, 0, 0);
		tableLayoutPanel3.ColumnCount = 2;
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		_addPeer.Anchor = AnchorStyles.Right;
		_addPeer.Margin = Padding.Empty;
		tableLayoutPanel3.Controls.Add(_peerIpShell, 0, 0);
		tableLayoutPanel3.Controls.Add(_addPeer, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(panel, 0, 1);
		var deviceActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Ui.SurfaceMintBody };
        _pairDevices.AutoSize = true; _pairDevices.Height = 36;
        deviceActions.Controls.Add(_pairDevices); deviceActions.Controls.Add(_pendingFiles);
        tableLayoutPanel.Controls.Add(deviceActions, 0, 2);
        tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 3);
		// Hairline above the manual-IP row so it reads as a separate action area.
		tableLayoutPanel.Paint += (object? _, PaintEventArgs e) =>
		{
			int[] rows = tableLayoutPanel.GetRowHeights();
			int dividerY = tableLayoutPanel.ClientSize.Height - rows[3];
			using Pen divider = new Pen(Ui.Line);
			e.Graphics.DrawLine(divider, 0, dividerY, tableLayoutPanel.ClientSize.Width, dividerY);
		};
		tableLayoutPanel.Resize += (object? _, EventArgs _) => tableLayoutPanel.Invalidate();
		_devicesSurface.Controls.Add(tableLayoutPanel);
		return _devicesSurface;
	}

	private Control BuildEvents()
	{
		_eventsSurface.Dock = DockStyle.Fill;
		_eventsSurface.Name = "EventLogSurface";
		_eventsSurface.CornerRadius = 14;
		_eventsSurface.Margin = new Padding(0, 0, 0, 12);
		_eventsSurface.Padding = new Padding(20, 12, 20, 16);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			BackColor = Color.Transparent
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		TableLayoutPanel tableLayoutPanel2 = Row(Color.Transparent);
		tableLayoutPanel2.ColumnCount = 3;
		tableLayoutPanel2.Margin = new Padding(2, 0, 2, 2);
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Label control = GlyphLabel("\ue81c", 10.5f, Ui.Muted);
		Label control2 = TextLabel("运行记录", 10.8f, bold: true);
		Label label = TextLabel("最新在前", 8.4f, bold: false, Ui.Faint);
		label.Margin = new Padding(0, 0, 1, 0);
		label.Anchor = AnchorStyles.Right;
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(control2, 1, 0);
		tableLayoutPanel2.Controls.Add(label, 2, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel.Controls.Add(_events, 0, 1);
		_eventsSurface.Controls.Add(tableLayoutPanel);
		return _eventsSurface;
	}

	private Control BuildFooter()
	{
		_footerSurface.Dock = DockStyle.Fill;
		_footerSurface.Name = "FooterBarSurface";
		_footerSurface.Margin = Padding.Empty;
		// Insets keep the inner grid inside the rounded card painted by FooterBarSurface
		// (card spans y = 0 .. height - 6).
		_footerSurface.Padding = new Padding(38, 3, 18, 9);
		TableLayoutPanel tableLayoutPanel = Row(Ui.SurfaceMutedBody);
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.ColumnCount = 2;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_status, 0, 0);
		tableLayoutPanel.Controls.Add(_themeSelector, 1, 0);
		_footerSurface.Controls.Add(tableLayoutPanel);
		return _footerSurface;
	}

	private void ConfigureDevices()
	{
		_devices.Name = "DeviceList";
		_devices.Dock = DockStyle.Fill;
		_devices.Visible = false;
		_devices.SelectedPeerChanged += (object? _, EventArgs _) => UpdateRemoveState();
	}

	private void ConfigureEvents()
	{
		_events.Name = "RunEvents";
		_events.Dock = DockStyle.Fill;
		_events.BorderStyle = BorderStyle.None;
		_events.BackColor = GlassEventSurface.BodyColor;
		_events.ForeColor = Ui.Ink;
		_events.DrawMode = DrawMode.OwnerDrawFixed;
		_events.ItemHeight = 38;
		_events.IntegralHeight = false;
		_events.SelectionMode = SelectionMode.None;
		_events.TabStop = false;
		_events.AccessibleName = "运行记录";
		_events.DrawItem += DrawEventRow;
		_eventMotionTimer.Tick += (object? _, EventArgs _) =>
		{
			bool flag = false;
			foreach (EventEntry item in _events.Items.OfType<EventEntry>())
			{
				if (!(item.Progress >= 1.0))
				{
					item.Progress = VisualDrawing.MoveTowards(item.Progress, 1.0, 0.11);
					flag |= item.Progress < 1.0;
				}
			}
			_events.Invalidate();
			if (!flag)
			{
				_eventMotionTimer.Stop();
			}
		};
		_networkRefreshTimer.Tick += (object? _, EventArgs _) =>
		{
			_networkRefreshTimer.Stop();
			if (!IsDisposed && !Disposing)
			{
				RefreshNetworkAddresses();
			}
		};
		_introTimer.Tick += (object? _, EventArgs _) =>
		{
			_introProgress = VisualDrawing.MoveTowards(_introProgress, 1.0, 0.075);
			if (VisualMotion.AnimationsEnabled)
			{
				if (_introProgress >= 0.12 && _headerSurface.MotionProgress == 0.0)
				{
					_headerSurface.Flash();
				}
				if (_introProgress >= 0.2 && _networkSurface.MotionProgress == 0.0)
				{
					_networkSurface.Flash();
				}
				if (_introProgress >= 0.56 && _devicesSurface.MotionProgress == 0.0)
				{
					_devicesSurface.Flash();
				}
				if (_introProgress >= 0.74 && _eventsSurface.MotionProgress == 0.0)
				{
					_eventsSurface.Flash();
				}
				if (_introProgress >= 0.86 && _footerSurface.MotionProgress == 0.0)
				{
					_footerSurface.Flash();
				}
			}
			if (!(_introProgress < 1.0))
			{
				_introTimer.Stop();
				EndStartupComposition();
			}
		};
	}

	private void WireInteractions()
	{
        _sendClipboard.Click += (_, _) => SendClipboardRequested?.Invoke();
        _sendFiles.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要发送的文件" };
            if (dialog.ShowDialog(this) == DialogResult.OK) SendFilesRequested?.Invoke(dialog.FileNames);
        };
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) SendFilesRequested?.Invoke(paths); };
		_revealKey.CheckedChanged += (object? _, EventArgs _) =>
		{
			_keyShell.Editor.UseSystemPasswordChar = !_revealKey.Checked;
		};
		_peerIpShell.Editor.KeyDown += (object? _, KeyEventArgs e) =>
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				AddPeerRequested?.Invoke(_peerIpShell.Editor.Text);
			}
		};
		_addPeer.Click += (object? _, EventArgs _) =>
		{
			AddPeerRequested?.Invoke(_peerIpShell.Editor.Text);
		};
		_remove.Click += (object? _, EventArgs _) =>
		{
			if (_devices.SelectedPeer is { Manual: true } peerSnapshot)
			{
				RemovePeerRequested?.Invoke(peerSnapshot.Address);
			}
		};
		_refresh.Click += (object? _, EventArgs _) =>
		{
			RefreshNetworkAddresses();
			_devicesSurface.Flash();
			RefreshRequested?.Invoke();
		};
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			int value = 2;
			DwmSetWindowAttribute(Handle, 33, ref value, 4);
		}
		catch (DllNotFoundException)
		{
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		MaximizedBounds = Screen.FromControl(this).WorkingArea;
		UpdateMaximizeGlyph();
		if (!_introStarted)
		{
			_introStarted = true;
			_introTimer.Start();
		}
	}

	private void EndStartupComposition()
	{
		if (_startupCompositionActive && IsHandleCreated)
		{
			_startupCompositionActive = false;
			int windowLong = GetWindowLong(Handle, -20);
			SetWindowLong(Handle, -20, windowLong & -33554433);
		}
	}

    public void SetPendingFiles(int count) => _pendingFiles.Text = count > 0 ? $"待接收 ({count})" : "传输记录";

	public void ShowControlPanel()
	{
		_keyShell.Editor.UseSystemPasswordChar = true;
		_revealKey.Checked = false;
		if (!Visible)
		{
			Show();
		}
		if (WindowState == FormWindowState.Minimized)
		{
			ShowWindow(Handle, SwRestore);
		}
		_networkSurface.Flash();
		BringToFront();
		Activate();
	}

	public void FocusPairing()
	{
		ShowControlPanel();
		_keyShell.Editor.Focus();
	}

	public void FocusManualPeer()
	{
		ShowControlPanel();
		_peerIpShell.Editor.Focus();
	}

	public void ClearManualInput()
	{
		_peerIpShell.Editor.Clear();
	}

	private sealed record ThemeColorBinding(Control Control, bool Background, string Role);

	private void CaptureThemeBindings(Control control, bool inHeader)
	{
		inHeader |= control == _headerSurface;
		string? background = _renderedPalette.FindRole(control.BackColor, background: true, inHeader);
		string? foreground = _renderedPalette.FindRole(control.ForeColor, background: false, inHeader);
		if (background != null) _themeBindings.Add(new ThemeColorBinding(control, true, background));
		if (foreground != null) _themeBindings.Add(new ThemeColorBinding(control, false, foreground));
		foreach (Control child in control.Controls) CaptureThemeBindings(child, inHeader);
	}

	// Paint a single buffered reveal over the existing controls instead of
	// recoloring independently rendered child HWNDs on every animation frame.
	public void ApplyTheme(UiThemeMode mode)
	{
		_themeMode = mode;
		UiColorTheme targetTheme = Ui.CurrentTheme;
		bool animate = Visible && WindowState != FormWindowState.Minimized && VisualMotion.AnimationsEnabled
			&& ClientSize.Width > 0 && ClientSize.Height > 0
			&& (long)ClientSize.Width * ClientSize.Height <= 12000000;
		bool frozen = false;
		if (animate)
		{
			Bitmap? frame = null;
			try
			{
				if (_themeTransitionOverlay.HasFrame)
				{
					frame = _themeTransitionOverlay.CaptureCurrentFrame();
				}
				else
				{
					Ui.ApplyTheme(_renderedTheme);
					frame = CaptureWorkspace();
				}
				_themeTransitionOverlay.Freeze(frame);
				frame = null;
				frozen = true;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ExternalException || ex is OutOfMemoryException)
			{
				frame?.Dispose();
				_themeTransitionOverlay.EndTransition();
			}
		}

		Ui.ApplyTheme(targetTheme);
		_renderedTheme = targetTheme;
		_renderedPalette = UiPalette.Capture();
		_minimizeButton.DarkBackground = Ui.HeaderIsDark;
		_maximizeButton.DarkBackground = Ui.HeaderIsDark;
		_closeButton.DarkBackground = Ui.HeaderIsDark;
		RefreshThemeColors();
		if (frozen)
		{
			Bitmap? target = null;
			try
			{
				target = CaptureWorkspace();
				Point origin = PointToClient(_themeSelector.PointToScreen(new Point(_themeSelector.Width / 2, _themeSelector.Height / 2)));
				_themeTransitionOverlay.Reveal(target, origin);
				target = null;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ExternalException || ex is OutOfMemoryException)
			{
				target?.Dispose();
				_themeTransitionOverlay.EndTransition();
			}
		}
		else
		{
			_themeTransitionOverlay.EndTransition();
		}
	}

	private Bitmap CaptureWorkspace()
	{
		Control root = Controls.Find("WorkspaceRoot", searchAllChildren: false)[0];
		Bitmap image = new Bitmap(root.Width, root.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
		try
		{
			root.DrawToBitmap(image, new Rectangle(Point.Empty, root.Size));
			DrawMissingPlaceholder(image, root, _peerIpShell.Editor);
			return image;
		}
		catch
		{
			image.Dispose();
			throw;
		}
	}

	private static void DrawMissingPlaceholder(Bitmap image, Control root, TextBox editor)
	{
		if (!editor.Visible || editor.Focused || editor.TextLength != 0 || string.IsNullOrEmpty(editor.PlaceholderText)) return;
		Point location = root.PointToClient(editor.PointToScreen(Point.Empty));
		Size textSize = TextRenderer.MeasureText(editor.PlaceholderText, editor.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		Rectangle area = Rectangle.Intersect(new Rectangle(location, new Size(Math.Min(editor.Width, textSize.Width + 8), editor.Height)), new Rectangle(Point.Empty, image.Size));
		Color background = editor.BackColor;
		int inkPixels = 0;
		for (int y = area.Top; y < area.Bottom && inkPixels < 8; y++)
		{
			for (int x = area.Left; x < area.Right && inkPixels < 8; x++)
			{
				Color pixel = image.GetPixel(x, y);
				if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) > 35) inkPixels++;
			}
		}
		if (inkPixels >= 8) return;
		using Graphics graphics = Graphics.FromImage(image);
		graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
		using SolidBrush brush = new SolidBrush(Ui.HeaderIsDark ? VisualMotion.Mix(Ui.FieldSurface, Ui.Muted, 0.5) : Ui.Muted);
		using StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
		graphics.DrawString(editor.PlaceholderText, editor.Font, brush, new RectangleF(location.X, location.Y, editor.Width, editor.Height), format);
	}

	private void RefreshThemeColors()
	{
		foreach (ThemeColorBinding binding in _themeBindings)
		{
			if (binding.Control.IsDisposed) continue;
			Color color = _renderedPalette[binding.Role];
			if (binding.Background)
			{
				if (binding.Control.BackColor != color) binding.Control.BackColor = color;
			}
			else if (binding.Control.ForeColor != color) binding.Control.ForeColor = color;
		}
		_networkSurface.FillColor = Ui.SurfaceIce;
		_networkSurface.BorderColor = Ui.Line;
		_devicesSurface.FillColor = Ui.SurfaceMint;
		_devicesSurface.BorderColor = Ui.Line;
		_events.BackColor = GlassEventSurface.BodyColor;
		Invalidate(invalidateChildren: true);
	}

	public void SetUpdateCheckState(bool enabled, string text = "检查更新")
	{
		_checkUpdates.Enabled = enabled;
		_checkUpdates.Text = text;
		_checkUpdates.PerformLayout();
		_checkUpdates.Invalidate();
	}

	public void UpdatePeers(IReadOnlyList<PeerSnapshot> peers)
	{
		string text = string.Join("|", peers.Select((PeerSnapshot peer) => peer.ToString()));
		if (text == _deviceSignature)
		{
			return;
		}
		_deviceSignature = text;
		_devices.SetPeers(peers);
		_deviceCount.Count = peers.Count;
		_connectionSummary.SetOnlineDeviceCount(peers.Count((PeerSnapshot peer) => peer.RecentlySeen));
		_devices.Visible = peers.Count > 0;
		_empty.Visible = peers.Count == 0;
		_devicesSurface.Flash();
		UpdateRemoveState();
	}

	public void AddEvent(string message)
	{
		_status.Text = message;
		if (!(_lastEvent == message))
		{
			_lastEvent = message;
			bool animate = VisualMotion.AnimationsEnabled;
			EventEntry item = new EventEntry(DateTime.Now.ToString("HH:mm:ss"), message)
			{
				Progress = animate ? 0.0 : 1.0
			};
			_events.Items.Insert(0, item);
			while (_events.Items.Count > 50)
			{
				_events.Items.RemoveAt(_events.Items.Count - 1);
			}
			if (animate)
			{
				_eventMotionTimer.Start();
			}
			_eventsSurface.Flash();
		}
	}

	public IReadOnlyList<UiEventSnapshot> ExportEvents()
	{
		return (from entry in _events.Items.OfType<EventEntry>()
			select new UiEventSnapshot(entry.Time, entry.Message)).ToArray();
	}

	public void RestoreEvents(IReadOnlyList<UiEventSnapshot> events)
	{
		_events.BeginUpdate();
		try
		{
			_events.Items.Clear();
			foreach (UiEventSnapshot item in events.Take(50))
			{
				_events.Items.Add(new EventEntry(item.Time, item.Message)
				{
					Progress = 1.0
				});
			}
		}
		finally
		{
			_events.EndUpdate();
		}
		_lastEvent = ((events.Count > 0) ? events[0].Message : string.Empty);
		_status.Text = (string.IsNullOrEmpty(_lastEvent) ? "就绪 · 主动发送模式" : _lastEvent);
		_events.Invalidate();
	}

	public void RefreshNetworkAddresses()
	{
		try
		{
			string[] array = (from address in (from info in (from network in NetworkInterface.GetAllNetworkInterfaces()
						where network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType != NetworkInterfaceType.Loopback
						select network).SelectMany((NetworkInterface network) => network.GetIPProperties().UnicastAddresses)
					select info.Address).Where(SharedProtocol.IsLocalAddress)
				select address.ToString()).Distinct().ToArray();
			_addresses.Text = ((array.Length == 0) ? "未检测到局域网地址" : string.Join(Environment.NewLine, array));
			_connectionSummary.SetNetworkAvailable(array.Length != 0);
		}
		catch (NetworkInformationException)
		{
			_addresses.Text = "暂时无法读取网络地址";
			_connectionSummary.SetNetworkAvailable(available: false);
		}
	}

	private void UpdateRemoveState()
	{
		_remove.Enabled = _devices.SelectedPeer is { Manual: true };
	}

	private void HideToTray()
	{
		_revealKey.Checked = false;
		_keyShell.Editor.UseSystemPasswordChar = true;
		Hide();
	}

	private void SubscribeNetworkChanges()
	{
		if (!_networkSubscriptionActive)
		{
			NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
			NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
			_networkSubscriptionActive = true;
		}
	}

	private void OnNetworkAddressChanged(object? sender, EventArgs e)
	{
		QueueNetworkRefresh();
	}

	private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
	{
		QueueNetworkRefresh();
	}

	private void QueueNetworkRefresh()
	{
		if (IsDisposed || Disposing)
		{
			return;
		}
		try
		{
			if (IsHandleCreated && InvokeRequired)
			{
				BeginInvoke(StartNetworkRefreshTimer);
			}
			else
			{
				StartNetworkRefreshTimer();
			}
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void StartNetworkRefreshTimer()
	{
		if (!IsDisposed && !Disposing)
		{
			_networkRefreshTimer.Stop();
			_networkRefreshTimer.Start();
		}
	}

	private void DrawEventRow(object? sender, DrawItemEventArgs e)
	{
		if (e.Index < 0 || e.Index >= _events.Items.Count || _events.Items[e.Index] is not EventEntry entry)
		{
			return;
		}
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush background = new SolidBrush(GlassEventSurface.BodyColor))
		{
			g.FillRectangle(background, e.Bounds);
		}
		double progress = VisualMotion.EaseOutCubic(entry.Progress);
		bool latest = e.Index == 0;
		float offset = (float)((1.0 - progress) * 8.0);
		RectangleF card = new RectangleF(e.Bounds.Left + 2 + offset, e.Bounds.Top + 3,
			Math.Max(1, e.Bounds.Width - 5 - offset), Math.Max(1, e.Bounds.Height - 6));
		Color top = VisualMotion.Mix(GlassEventSurface.BodyColor, Ui.SurfaceHover, latest ? 0.57 : 0.23);
		Color bottom = VisualMotion.Mix(GlassEventSurface.BodyColor, Ui.Surface, latest ? 0.42 : 0.24);
		using (LinearGradientBrush fill = new LinearGradientBrush(card, top, bottom, 90f))
		{
			VisualDrawing.FillRoundedRectangle(g, fill, card, 9f);
		}
		using (Pen border = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 100 : 155, Ui.Line)))
		{
			VisualDrawing.DrawRoundedRectangle(g, border, card, 9f);
		}
		using (Pen highlight = new Pen(Color.FromArgb(Ui.HeaderIsDark ? 28 : 160, Color.White)))
		{
			g.DrawLine(highlight, card.Left + 9, card.Top + 1, card.Right - 9, card.Top + 1);
		}
		if (entry.Progress < 1.0 && VisualMotion.AnimationsEnabled)
		{
			GraphicsState state = g.Save();
			using GraphicsPath clip = VisualDrawing.RoundedRectangle(card, 9f);
			g.SetClip(clip);
			float shineX = card.Left - 35 + (float)progress * (card.Width + 70);
			using SolidBrush shine = new SolidBrush(Color.FromArgb((int)(45 * (1.0 - entry.Progress)), Color.White));
			g.FillEllipse(shine, shineX - 35, card.Top - 18, 70, card.Height + 36);
			g.Restore(state);
		}
		float centerY = e.Bounds.Top + e.Bounds.Height / 2f;
		using (SolidBrush halo = new SolidBrush(Color.FromArgb(latest ? 35 : 18, Ui.Accent)))
		{
			g.FillEllipse(halo, card.Left + 9, centerY - 6, 12, 12);
		}
		using (SolidBrush dot = new SolidBrush(latest ? Ui.Accent : Ui.Faint))
		{
			g.FillEllipse(dot, card.Left + 12, centerY - 3, 6, 6);
		}
		int x = (int)Math.Round(card.Left);
		TextRenderer.DrawText(g, entry.Time, _eventTimeFont, new Rectangle(x + 27, e.Bounds.Top, 64, e.Bounds.Height), Ui.Muted, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		TextRenderer.DrawText(g, entry.Message, Font, new Rectangle(x + 95, e.Bounds.Top, Math.Max(1, (int)card.Width - 101), e.Bounds.Height), Ui.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			HideToTray();
		}
		base.OnFormClosing(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		UpdateMaximizeGlyph();
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == WmNcCalcSize && message.WParam != IntPtr.Zero)
		{
			// Maximized native windows include an invisible resize frame. Keep our
			// custom client area inside the monitor work area instead of clipping it.
			if (IsZoomed(message.HWnd))
			{
				NativeRect client = Marshal.PtrToStructure<NativeRect>(message.LParam);
				Rectangle work = Screen.FromHandle(message.HWnd).WorkingArea;
				client.Left = Math.Max(client.Left, work.Left);
				client.Top = Math.Max(client.Top, work.Top);
				client.Right = Math.Min(client.Right, work.Right);
				client.Bottom = Math.Min(client.Bottom, work.Bottom);
				Marshal.StructureToPtr(client, message.LParam, false);
			}
			message.Result = IntPtr.Zero;
			return;
		}
		if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
		{
			base.WndProc(ref message);
			if ((int)message.Result == 1)
			{
				long num = ((IntPtr)message.LParam).ToInt64();
				Point p = new Point((short)(num & 0xFFFF), (short)((num >> 16) & 0xFFFF));
				Point point = PointToClient(p);
				bool flag = point.X <= 7;
				bool flag2 = point.X >= ClientSize.Width - 7;
				bool flag3 = point.Y <= 7;
				bool flag4 = point.Y >= ClientSize.Height - 7;
				message.Result = ((flag3 & flag) ? 13 : ((flag3 & flag2) ? 14 : ((flag4 & flag) ? 16 : ((flag4 & flag2) ? 17 : (flag ? 10 : (flag2 ? 11 : (flag3 ? 12 : ((!flag4) ? 1 : 15))))))));
			}
		}
		else
		{
			base.WndProc(ref message);
		}
	}

	private void WireWindowDrag(Control control)
	{
		control.MouseDown += (object? _, MouseEventArgs e) =>
		{
			if (e.Button == MouseButtons.Left)
			{
				if (e.Clicks == 2)
				{
					ToggleMaximize();
				}
				else if (WindowState != FormWindowState.Maximized)
				{
					ReleaseCapture();
					SendMessage(Handle, 161, 2, IntPtr.Zero);
				}
			}
		};
	}

	private void ToggleMaximize()
	{
		if (WindowState == FormWindowState.Maximized)
		{
			PostMessage(Handle, WmSysCommand, ScRestore, IntPtr.Zero);
		}
		else
		{
			MaximizedBounds = Screen.FromControl(this).WorkingArea;
			PostMessage(Handle, WmSysCommand, ScMaximize, IntPtr.Zero);
		}
		// The system command is posted so the caption control can release mouse capture first.
		// OnResize updates the glyph when Windows applies the new state.
	}

	private void UpdateMaximizeGlyph()
	{
		_maximizeButton.Glyph = ((WindowState == FormWindowState.Maximized) ? "\ue923" : "\ue922");
		_maximizeButton.AccessibleName = ((WindowState == FormWindowState.Maximized) ? "还原窗口" : "最大化");
		_maximizeButton.Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (_networkSubscriptionActive)
			{
				NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
				NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
				_networkSubscriptionActive = false;
			}
			_eventMotionTimer.Dispose();
			_introTimer.Dispose();
			_networkRefreshTimer.Dispose();
			_eventTimeFont.Dispose();
			_tips.Dispose();
		}
		base.Dispose(disposing);
	}

	private static TableLayoutPanel Row(Color backColor)
	{
		return new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 1,
			Margin = Padding.Empty,
			BackColor = backColor
		};
	}

	private static Label TextLabel(string text, float size, bool bold = false, Color? color = null)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = (color ?? Ui.Ink),
			Font = Ui.Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
			Margin = Padding.Empty
		};
	}

	private static Label GlyphLabel(string glyph, float size, Color color)
	{
		return new Label
		{
			Text = glyph,
			AutoSize = false,
			Width = 21,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			AccessibleRole = AccessibleRole.None,
			ForeColor = color,
			Font = new Font("Segoe Fluent Icons", size, FontStyle.Regular, GraphicsUnit.Point),
			Margin = new Padding(0, 0, 7, 0)
		};
	}

	private static void CreateNestedHandles(Control control)
	{
		_ = control.Handle;
		foreach (Control control2 in control.Controls)
		{
			CreateNestedHandles(control2);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsZoomed(nint window);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ShowWindow(nint window, int command);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PostMessage(nint window, int message, int wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint window, int message, int wParam, nint lParam);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(nint window, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(nint window, int index, int value);
}
