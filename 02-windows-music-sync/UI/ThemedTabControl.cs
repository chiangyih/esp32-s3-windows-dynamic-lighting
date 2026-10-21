namespace MusicSync.UI;

/// <summary>Native tab interaction and accessibility with a static Tiffany/dark paint surface.</summary>
internal sealed class ThemedTabControl : TabControl
{
    private int _hoverTab = -1;

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SizeMode = TabSizeMode.Fixed;
        Alignment = TabAlignment.Top;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        AccessibleName = "設定與程式資訊分頁";
        TabStop = true;
        ApplyMetrics();
    }

    private void ApplyMetrics()
    {
        var scale = DeviceDpi / 96f;
        ItemSize = new Size((int)Math.Round(150 * scale), (int)Math.Round(32 * scale));
        Padding = new Point((int)Math.Round(12 * scale), (int)Math.Round(5 * scale));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(UiTheme.Background);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(UiTheme.Background);
        var scale = DeviceDpi / 96f;
        var page = DisplayRectangle;
        if (page.Width > 0 && page.Height > 0)
        {
            using var border = new Pen(UiTheme.Border, Math.Max(1, scale));
            e.Graphics.DrawRectangle(border, page.Left - 1, page.Top - 1,
                page.Width + 1, page.Height + 1);
        }
        for (var index = 0; index < TabCount; index++)
            if (index != SelectedIndex) PaintTab(e.Graphics, index, false, scale);
        if (SelectedIndex >= 0 && SelectedIndex < TabCount)
            PaintTab(e.Graphics, SelectedIndex, true, scale);
        base.OnPaint(e);
    }

    private void PaintTab(Graphics graphics, int index, bool selected, float scale)
    {
        var bounds = GetTabRect(index);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        var background = selected ? UiTheme.Surface : UiTheme.Background;
        using var fill = new SolidBrush(background);
        graphics.FillRectangle(fill, bounds);
        var foreground = selected ? UiTheme.Accent : index == _hoverTab ? UiTheme.Text : UiTheme.Muted;
        var textBounds = Rectangle.Inflate(bounds, -(int)Math.Ceiling(8 * scale), -(int)Math.Ceiling(2 * scale));
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(graphics, TabPages[index].Text, Font, textBounds, foreground, flags);
        if (selected)
        {
            using var accent = new SolidBrush(UiTheme.Accent);
            var thickness = Math.Max(2, (int)Math.Round(2 * scale));
            graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - thickness, bounds.Width, thickness);
        }
        if (selected && Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -(int)Math.Ceiling(5 * scale), -(int)Math.Ceiling(4 * scale));
            ControlPaint.DrawFocusRectangle(graphics, focusBounds, foreground, background);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hover = -1;
        for (var index = 0; index < TabCount; index++)
            if (GetTabRect(index).Contains(e.Location)) { hover = index; break; }
        if (hover != _hoverTab) { _hoverTab = hover; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverTab = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
