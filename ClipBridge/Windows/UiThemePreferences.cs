using System;
using System.IO;
using System.Security;

namespace ClipBridge.Windows;

internal sealed class UiThemePreferences
{
	private readonly string _path;

	public UiThemePreferences(string? path = null)
	{
		_path = (string.IsNullOrWhiteSpace(path) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBridge", "ui-theme.txt") : Path.GetFullPath(path));
	}

	public UiThemeMode Load()
	{
		try
		{
			if (!File.Exists(_path))
			{
				return UiThemeMode.Light;
			}
			UiThemeMode result;
			return (Enum.TryParse<UiThemeMode>(File.ReadAllText(_path).Trim(), ignoreCase: true, out result) && Enum.IsDefined(typeof(UiThemeMode), result)) ? result : UiThemeMode.Light;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException) ? true : false)
		{
			return UiThemeMode.Light;
		}
	}

	public bool TrySave(UiThemeMode mode)
	{
		string text = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			string directoryName = Path.GetDirectoryName(_path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(text, mode.ToString());
			File.Move(text, _path, overwrite: true);
			return true;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException) ? true : false)
		{
			return false;
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is SecurityException) ? true : false)
			{
			}
		}
	}
}
