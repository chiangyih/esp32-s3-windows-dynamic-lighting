using System.Drawing.Drawing2D;

namespace MusicSync.UI;

/// <summary>Static colours only; no shared GDI resources or animation workers.</summary>
internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(0x0B, 0x14, 0x1C);
    public static readonly Color Surface = Color.FromArgb(0x13, 0x22, 0x2C);
    public static readonly Color Border = Color.FromArgb(0x25, 0x40, 0x4A);
    public static readonly Color Text = Color.FromArgb(0xE6, 0xF4, 0xF3);
    public static readonly Color Muted = Color.FromArgb(0x93, 0xAD, 0xB5);
    public static readonly Color Accent = Color.FromArgb(0x81, 0xD8, 0xD0);
    public static readonly Color AccentHover = Color.FromArgb(0xA3, 0xE7, 0xE0);
    public static readonly Color OnAccent = Color.FromArgb(0x0B, 0x2C, 0x30);

    // The caller owns the returned path and must dispose it after painting.
    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        if (radius < 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
