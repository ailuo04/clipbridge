using System;
using Microsoft.Win32;

namespace ClipBridge.Windows;

internal sealed class SystemThemeWatcher : IDisposable
{
	private bool _subscribed;

	public event Action? Changed;

	public SystemThemeWatcher()
	{
		SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
		_subscribed = true;
	}

	private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
	{
		if (_subscribed)
		{
			UserPreferenceCategory category = e.Category;
			if ((category == UserPreferenceCategory.Color || category == UserPreferenceCategory.General || category == UserPreferenceCategory.VisualStyle) ? true : false)
			{
				Changed?.Invoke();
			}
		}
	}

	public void Dispose()
	{
		if (_subscribed)
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			_subscribed = false;
		}
	}
}
