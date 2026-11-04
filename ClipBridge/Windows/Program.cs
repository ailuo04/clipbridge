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
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (SelfUpdate.IsApplyMode(args))
        {
            Environment.ExitCode = UpdateDialog.RunInstaller(args);
            return;
        }
		using EventWaitHandle eventWaitHandle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\ClipBridge.Activate");
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\ClipBridge.Windows", out createdNew))
		{
			if (!createdNew)
			{
				eventWaitHandle.Set();
                if (Array.IndexOf(args, "--updated") >= 0) Environment.ExitCode = SelfUpdate.ExistingInstanceExitCode;
				return;
			}
			Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("ClipBridge.Windows"));
			try
			{
				using var context = new TrayAppContext(eventWaitHandle);
                bool updated = Array.Exists(args, argument => argument == "--updated");
                bool rolledBack = Array.Exists(args, argument => argument == "--update-rolled-back");
                if (updated || rolledBack) context.ShowAfterUpdate(rolledBack, () => SignalUpdateReady(args));
                Application.Run(context);
			}
			catch (Exception ex) when ((ex is SocketException || ex is CryptographicException || ex is IOException || ex is InvalidOperationException) ? true : false)
			{
				MessageBox.Show("启动失败：请检查 45821/45822 端口是否被占用，以及当前用户密钥存储是否可访问。\n错误类型：" + ex.GetType().Name, "ClipBridge", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
	}

    private static void SignalUpdateReady(string[] args)
    {
        int index = Array.IndexOf(args, "--update-ready");
        if (index < 0 || index + 1 >= args.Length || !args[index + 1].StartsWith("Local\\ClipBridge.UpdateReady.", StringComparison.Ordinal)) return;
        try { using var ready = EventWaitHandle.OpenExisting(args[index + 1]); ready.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
