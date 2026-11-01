using System.Drawing;

namespace ClipBridge.Windows;

internal static class Ui
{
	internal static Color Canvas = Color.FromArgb(238, 242, 248);

	internal static Color Surface = Color.FromArgb(255, 255, 255);

	internal static Color SurfaceIce = Color.FromArgb(255, 255, 255);

	internal static Color SurfaceMint = Color.FromArgb(255, 255, 255);

	internal static Color FieldSurface = Color.FromArgb(248, 250, 253);

	internal static Color SurfaceMutedSolid = Color.FromArgb(246, 248, 251);

	internal static Color SurfaceMuted = Color.FromArgb(255, 255, 255);

	internal static Color SurfaceHover = Color.FromArgb(238, 244, 252);

	internal static Color SurfaceIceBody = Color.FromArgb(255, 255, 255);

	internal static Color SurfaceMintBody = Color.FromArgb(255, 255, 255);

	internal static Color SurfaceMutedBody = Color.FromArgb(255, 255, 255);

	internal static Color Ink = Color.FromArgb(30, 39, 53);

	internal static Color Muted = Color.FromArgb(88, 101, 120);

	internal static Color Faint = Color.FromArgb(92, 105, 124);

	internal static Color Line = Color.FromArgb(218, 225, 234);

	internal static Color Accent = Color.FromArgb(37, 99, 235);

	internal static Color AccentDark = Color.FromArgb(30, 79, 200);

	internal static Color AccentSoft = Color.FromArgb(222, 234, 255);

	internal static Color AccentSoftSolid = Color.FromArgb(229, 238, 255);

	internal static Color PrimarySurface = Color.FromArgb(37, 99, 235);

	internal static Color PrimaryHover = Color.FromArgb(33, 86, 212);

	internal static Color PrimaryPressed = Color.FromArgb(29, 72, 162);

	internal static Color PrimaryInk = Color.White;

	internal static Color Success = Color.FromArgb(20, 145, 105);

	internal static Color SuccessText = Color.FromArgb(11, 107, 79);

	internal static Color SuccessSoft = Color.FromArgb(225, 247, 239);

	internal static Color Warning = Color.FromArgb(180, 104, 24);

	internal static Color WarningText = Color.FromArgb(138, 75, 15);

	internal static Color WarningSoft = Color.FromArgb(255, 243, 224);

	internal static Color Danger = Color.FromArgb(190, 61, 78);

	internal static Color DangerSoft = Color.FromArgb(255, 230, 235);

	internal static Color Header = Color.FromArgb(255, 255, 255);

	internal static Color HeaderRaised = Color.FromArgb(242, 246, 251);

	internal static Color HeaderText = Color.FromArgb(30, 39, 53);

	internal static Color HeaderMuted = Color.FromArgb(88, 101, 120);

	internal static Color ChromeRaised = Color.FromArgb(203, 213, 225);

	internal static Color ChromeLine = Color.FromArgb(214, 223, 235);

	internal static Color ChromeMuted = Color.FromArgb(88, 101, 120);

	internal static UiColorTheme CurrentTheme { get; private set; } = UiColorTheme.Light;

	internal static bool HeaderIsDark => CurrentTheme == UiColorTheme.Dark;

	internal static void ApplyTheme(UiColorTheme theme)
	{
		if (theme == UiColorTheme.Dark)
		{
			Canvas = Color.FromArgb(16, 21, 29);
			Surface = Color.FromArgb(24, 32, 43);
			SurfaceIce = Surface;
			SurfaceMint = Surface;
			FieldSurface = Color.FromArgb(32, 42, 56);
			SurfaceMutedSolid = Color.FromArgb(30, 39, 51);
			SurfaceMuted = Surface;
			SurfaceHover = Color.FromArgb(39, 52, 69);
			SurfaceIceBody = Surface;
			SurfaceMintBody = Surface;
			SurfaceMutedBody = Surface;
			Ink = Color.FromArgb(238, 242, 248);
			Muted = Color.FromArgb(174, 185, 199);
			Faint = Color.FromArgb(142, 155, 172);
			Line = Color.FromArgb(53, 65, 81);
			Accent = Color.FromArgb(112, 152, 255);
			AccentDark = Color.FromArgb(138, 174, 255);
			AccentSoft = Color.FromArgb(36, 59, 99);
			AccentSoftSolid = Color.FromArgb(34, 52, 79);
			PrimarySurface = Color.FromArgb(63, 99, 184);
			PrimaryHover = Color.FromArgb(69, 111, 202);
			PrimaryPressed = Color.FromArgb(52, 85, 159);
			PrimaryInk = Color.White;
			Success = Color.FromArgb(69, 201, 155);
			SuccessText = Color.FromArgb(120, 227, 190);
			SuccessSoft = Color.FromArgb(27, 59, 48);
			Warning = Color.FromArgb(240, 178, 104);
			WarningText = Color.FromArgb(255, 208, 139);
			WarningSoft = Color.FromArgb(56, 42, 25);
			Danger = Color.FromArgb(255, 135, 151);
			DangerSoft = Color.FromArgb(68, 34, 42);
			Header = Color.FromArgb(16, 21, 29);
			HeaderRaised = Color.FromArgb(36, 46, 59);
			HeaderText = Color.FromArgb(245, 248, 252);
			HeaderMuted = Color.FromArgb(170, 181, 195);
			ChromeRaised = Color.FromArgb(58, 72, 91);
			ChromeLine = Color.FromArgb(52, 65, 82);
			ChromeMuted = HeaderMuted;
		}
		else
		{
			Canvas = Color.FromArgb(238, 242, 248);
			Surface = Color.FromArgb(255, 255, 255);
			SurfaceIce = Surface;
			SurfaceMint = Surface;
			FieldSurface = Color.FromArgb(248, 250, 253);
			SurfaceMutedSolid = Color.FromArgb(246, 248, 251);
			SurfaceMuted = Surface;
			SurfaceHover = Color.FromArgb(238, 244, 252);
			SurfaceIceBody = Surface;
			SurfaceMintBody = Surface;
			SurfaceMutedBody = Surface;
			Ink = Color.FromArgb(30, 39, 53);
			Muted = Color.FromArgb(88, 101, 120);
			Faint = Color.FromArgb(92, 105, 124);
			Line = Color.FromArgb(218, 225, 234);
			Accent = Color.FromArgb(37, 99, 235);
			AccentDark = Color.FromArgb(30, 79, 200);
			AccentSoft = Color.FromArgb(222, 234, 255);
			AccentSoftSolid = Color.FromArgb(229, 238, 255);
			PrimarySurface = Color.FromArgb(37, 99, 235);
			PrimaryHover = Color.FromArgb(33, 86, 212);
			PrimaryPressed = Color.FromArgb(29, 72, 162);
			PrimaryInk = Color.White;
			Success = Color.FromArgb(20, 145, 105);
			SuccessText = Color.FromArgb(11, 107, 79);
			SuccessSoft = Color.FromArgb(225, 247, 239);
			Warning = Color.FromArgb(180, 104, 24);
			WarningText = Color.FromArgb(138, 75, 15);
			WarningSoft = Color.FromArgb(255, 243, 224);
			Danger = Color.FromArgb(190, 61, 78);
			DangerSoft = Color.FromArgb(255, 230, 235);
			Header = Color.FromArgb(255, 255, 255);
			HeaderRaised = Color.FromArgb(242, 246, 251);
			HeaderText = Ink;
			HeaderMuted = Muted;
			ChromeRaised = Color.FromArgb(203, 213, 225);
			ChromeLine = Color.FromArgb(214, 223, 235);
			ChromeMuted = Muted;
		}
		CurrentTheme = theme;
	}

	internal static Font Font(float size, FontStyle style = FontStyle.Regular)
	{
		return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
	}
}
