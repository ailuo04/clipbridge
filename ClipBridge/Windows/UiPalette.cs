using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace ClipBridge.Windows;

// Captures the existing named palette so controls can follow it during a
// transition without confusing colors which happen to match in one theme.
internal sealed class UiPalette
{
	private static readonly FieldInfo[] Fields = typeof(Ui).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
		.Where(field => field.FieldType == typeof(Color)).ToArray();
	private static readonly string[] RoleOrder =
	{
		"Canvas", "Surface", "FieldSurface", "SurfaceMutedSolid", "SurfaceHover",
		"Ink", "Muted", "Faint", "Line", "Accent", "AccentDark", "AccentSoft", "AccentSoftSolid",
		"ChromeLine", "Header", "HeaderText", "HeaderMuted", "HeaderRaised", "ChromeRaised", "ChromeMuted",
		"Success", "SuccessText", "SuccessSoft", "Warning", "WarningText", "WarningSoft",
		"Danger", "DangerSoft", "PrimarySurface", "PrimaryHover", "PrimaryPressed", "PrimaryInk"
	};
	private readonly Dictionary<string, Color> _colors;

	private UiPalette(Dictionary<string, Color> colors) => _colors = colors;
	public Color this[string role] => _colors[role];

	public static UiPalette Capture() => new UiPalette(Fields.ToDictionary(field => field.Name, field => (Color)field.GetValue(null)!));

	public string? FindRole(Color color, bool background, bool inHeader)
	{
		if (color.A != 255) return null;
		int value = color.ToArgb();
		// White/header and ink/header text coincide in light mode but diverge
		// in dark mode, so preserve their semantic role in the title area.
		if (inHeader)
		{
			if (background && this["Header"].ToArgb() == value) return "Header";
			if (!background && this["HeaderText"].ToArgb() == value) return "HeaderText";
			if (!background && this["HeaderMuted"].ToArgb() == value) return "HeaderMuted";
		}
		foreach (string role in RoleOrder)
		{
			if (this[role].ToArgb() == value) return role;
		}
		return null;
	}
}
