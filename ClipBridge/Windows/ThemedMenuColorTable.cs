using System.Drawing;
using System.Windows.Forms;

namespace ClipBridge.Windows;

internal sealed class ThemedMenuColorTable : ProfessionalColorTable
{
	public override Color ToolStripDropDownBackground => Ui.Surface;

	public override Color ImageMarginGradientBegin => Ui.Surface;

	public override Color ImageMarginGradientMiddle => Ui.Surface;

	public override Color ImageMarginGradientEnd => Ui.Surface;

	public override Color MenuBorder => Ui.Line;

	public override Color MenuItemBorder => Ui.AccentSoft;

	public override Color MenuItemSelected => Ui.SurfaceHover;

	public override Color MenuItemSelectedGradientBegin => Ui.SurfaceHover;

	public override Color MenuItemSelectedGradientEnd => Ui.SurfaceHover;

	public override Color MenuItemPressedGradientBegin => Ui.SurfaceMutedSolid;

	public override Color MenuItemPressedGradientEnd => Ui.SurfaceMutedSolid;

	public override Color CheckBackground => Ui.Surface;

	public override Color CheckSelectedBackground => Ui.SurfaceHover;
}
