using System;

namespace ClipBridge.Windows;

internal sealed class UiThemeController
{
	private readonly UiThemePreferences _preferences;

	private readonly Func<bool> _systemUsesLightTheme;

	public UiThemeMode Mode { get; private set; }

	public UiColorTheme EffectiveTheme { get; private set; }

	public bool LastSaveSucceeded { get; private set; } = true;

	public event Action? ThemeChanged;

	public UiThemeController(UiThemePreferences preferences, Func<bool>? systemUsesLightTheme = null)
	{
		_preferences = preferences;
		_systemUsesLightTheme = systemUsesLightTheme ?? new Func<bool>(UiThemeResolver.WindowsUsesLightTheme);
		Mode = _preferences.Load();
		EffectiveTheme = UiThemeResolver.Resolve(Mode, _systemUsesLightTheme());
		Ui.ApplyTheme(EffectiveTheme);
	}

	public bool SelectMode(UiThemeMode mode)
	{
		Mode = mode;
		LastSaveSucceeded = _preferences.TrySave(mode);
		UiColorTheme uiColorTheme = UiThemeResolver.Resolve(mode, _systemUsesLightTheme());
		if (uiColorTheme == EffectiveTheme)
		{
			return false;
		}
		EffectiveTheme = uiColorTheme;
		Ui.ApplyTheme(uiColorTheme);
		ThemeChanged?.Invoke();
		return true;
	}

	public bool RefreshSystemTheme()
	{
		if (Mode != UiThemeMode.System)
		{
			return false;
		}
		UiColorTheme uiColorTheme = UiThemeResolver.Resolve(Mode, _systemUsesLightTheme());
		if (uiColorTheme == EffectiveTheme)
		{
			return false;
		}
		EffectiveTheme = uiColorTheme;
		Ui.ApplyTheme(uiColorTheme);
		ThemeChanged?.Invoke();
		return true;
	}
}
