using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ClipBridge.Windows;

internal static class UiThemeResolver
{
	internal static UiColorTheme Resolve(UiThemeMode mode, bool systemUsesLightTheme)
	{
		return mode switch
		{
			UiThemeMode.Dark => UiColorTheme.Dark,
			UiThemeMode.System => (!systemUsesLightTheme) ? UiColorTheme.Dark : UiColorTheme.Light,
			_ => UiColorTheme.Light,
		};
	}

	internal static bool WindowsUsesLightTheme()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			return !(registryKey?.GetValue("AppsUseLightTheme") is int num) || num != 0;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException) ? true : false)
		{
			return true;
		}
	}
}
