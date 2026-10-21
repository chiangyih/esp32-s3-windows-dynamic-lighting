using System.Drawing.Drawing2D;

namespace MusicSync.UI;

/// <summary>Native Button interaction with a small, static custom paint surface.</summary>
internal sealed class ActionButton : Button
{
    private bool _hover;
    private bool _mousePressed;
    private bool _keyboardPressed;

    public ActionButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var pressed = Enabled && (_keyboardPressed || (_mousePressed && _hover));
        var fill = !Enabled ? BackColor : pressed ? FlatAppearance.MouseDownBackColor :
            _hover ? FlatAppearance.MouseOverBackColor : BackColor;
        if (fill.IsEmpty) fill = BackColor;
        var bounds = new RectangleF(scale / 2, scale / 2, Width - scale, Height - scale);
        using var shape = UiTheme.RoundedRectangle(bounds, 4 * scale);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, shape);
        if (FlatAppearance.BorderSize > 0)
        {
            var borderColor = FlatAppearance.BorderColor.IsEmpty ? UiTheme.Border : FlatAppearance.BorderColor;
            using var border = new Pen(borderColor, FlatAppearance.BorderSize * scale);
            e.Graphics.DrawPath(border, shape);
        }

        var inset = (int)Math.Ceiling(6 * scale);
        var textBounds = Rectangle.Inflate(ClientRectangle, -inset, -(int)Math.Ceiling(2 * scale));
        textBounds = Rectangle.FromLTRB(textBounds.Left + Padding.Left, textBounds.Top + Padding.Top,
            textBounds.Right - Padding.Right, textBounds.Bottom - Padding.Bottom);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : UiTheme.Muted, flags);
        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(ClientRectangle, -(int)Math.Ceiling(4 * scale), -(int)Math.Ceiling(4 * scale));
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, Enabled ? ForeColor : UiTheme.Muted, fill);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _mousePressed = true;
        base.OnMouseDown(e);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _mousePressed = false;
        base.OnMouseUp(e);
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) _mousePressed = false;
        base.OnMouseCaptureChanged(e);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) _keyboardPressed = true;
        base.OnKeyDown(e);
        Invalidate();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _keyboardPressed = false;
        base.OnKeyUp(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _keyboardPressed = false;
        base.OnLostFocus(e);
        Invalidate();
    }
}
