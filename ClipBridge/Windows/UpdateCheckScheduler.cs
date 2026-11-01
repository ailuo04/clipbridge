using System;
using System.Threading;

namespace ClipBridge.Windows;

internal static class UpdateCheckScheduler
{
	private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10.0);

	private static readonly TimeSpan Interval = TimeSpan.FromHours(24.0);

	internal static Timer Start(Action check)
	{
		ArgumentNullException.ThrowIfNull(check, "check");
		return new Timer((object? _) =>
		{
			check();
		}, null, InitialDelay, Interval);
	}
}
