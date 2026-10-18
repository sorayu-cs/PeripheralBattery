using System.Drawing;

namespace PeripheralBattery.UI;

/// <summary>深色托盘菜单渲染：与主面板一致的背景、悬停色和细分割线。</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Back = Color.FromArgb(31, 32, 36);
    private static readonly Color Hover = Color.FromArgb(46, 47, 53);
    private static readonly Color Text = Color.FromArgb(230, 231, 234);
    private static readonly Color TextDisabled = Color.FromArgb(130, 132, 140);
    private static readonly Color Border = Color.FromArgb(46, 47, 53);

    public DarkMenuRenderer() : base(new DarkColorTable())
    {
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Back);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;

        using var brush = new SolidBrush(Hover);
        var bounds = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    private static readonly Color Back = Color.FromArgb(31, 32, 36);
    private static readonly Color Hover = Color.FromArgb(46, 47, 53);
    private static readonly Color Border = Color.FromArgb(46, 47, 53);

    public override Color ToolStripDropDownBackground => Back;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuBorder => Border;
    public override Color ImageMarginGradientBegin => Back;
    public override Color ImageMarginGradientMiddle => Back;
    public override Color ImageMarginGradientEnd => Back;
}
