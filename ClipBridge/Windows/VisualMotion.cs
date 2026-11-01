using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal static class VisualMotion
{
	private const uint SpiGetClientAreaAnimation = 4162u;

	internal static bool AnimationsEnabled
	{
		get
		{
			if (!SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled && SystemParametersInfo(4162u, 0u, out var value, 0u))
			{
				return value != 0;
			}
			return false;
		}
	}

	internal static double EaseOutCubic(double progress)
	{
		progress = Math.Clamp(progress, 0.0, 1.0);
		double num = 1.0 - progress;
		return 1.0 - num * num * num;
	}

	internal static float Lerp(float from, float to, double progress)
	{
		return from + (to - from) * (float)Math.Clamp(progress, 0.0, 1.0);
	}

	internal static Color Mix(Color from, Color to, double progress)
	{
		progress = Math.Clamp(progress, 0.0, 1.0);
		return Color.FromArgb((int)Math.Round((double)(int)from.A + (double)(to.A - from.A) * progress), (int)Math.Round((double)(int)from.R + (double)(to.R - from.R) * progress), (int)Math.Round((double)(int)from.G + (double)(to.G - from.G) * progress), (int)Math.Round((double)(int)from.B + (double)(to.B - from.B) * progress));
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint update);
}
