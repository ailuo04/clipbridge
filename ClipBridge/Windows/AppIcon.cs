using System;
using System.Drawing;
using System.IO;

namespace ClipBridge.Windows;

internal static class AppIcon
{
	internal static Icon Load()
	{
		string directoryName = Path.GetDirectoryName(typeof(AppIcon).Assembly.Location);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			string text = Path.Combine(directoryName, "ClipBridge.exe");
			if (File.Exists(text))
			{
				return Load(text);
			}
		}
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath))
		{
			try
			{
				return Load(processPath);
			}
			catch (ArgumentException)
			{
			}
			catch (IOException)
			{
			}
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	internal static Icon Load(string executablePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executablePath, "executablePath");
		return Icon.ExtractAssociatedIcon(executablePath) ?? ((Icon)SystemIcons.Application.Clone());
	}
}
