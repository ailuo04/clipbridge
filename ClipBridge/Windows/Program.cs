using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		if (SelfUpdate.IsApplyMode(args))
		{
			int num = SelfUpdate.Apply(args);
			if (num != 0)
			{
				string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "update.log");
				MessageBox.Show("自动更新未完成，原程序已尽量保留。\n请重新打开 ClipBridge，或查看日志：\n" + text, "ClipBridge 自动更新", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			Environment.ExitCode = num;
			return;
		}
		using EventWaitHandle eventWaitHandle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\ClipBridge.Activate");
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\ClipBridge.Windows", out createdNew))
		{
			if (!createdNew)
			{
				eventWaitHandle.Set();
				return;
			}
			Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("ClipBridge.Windows"));
			Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			try
			{
				Application.Run(new TrayAppContext(eventWaitHandle));
			}
			catch (Exception ex) when ((ex is SocketException || ex is CryptographicException || ex is IOException || ex is InvalidOperationException) ? true : false)
			{
				MessageBox.Show("启动失败：请检查 45821/45822 端口是否被占用，以及当前用户密钥存储是否可访问。\n错误类型：" + ex.GetType().Name, "ClipBridge", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
