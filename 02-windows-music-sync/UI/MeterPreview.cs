using System.Drawing.Drawing2D;
using MusicSync.Core;
using MusicSync.Lighting;

namespace MusicSync.UI;

internal sealed class MeterPreview : Control
{
    private readonly LedColor[] _colors = new LedColor[8];
    private readonly Color[] _gradientColors = new Color[8];
    private readonly float[] _gradientPositions = new float[8];
    private readonly ColorBlend _blend = new(8);

    public MeterPreview()
    {
        DoubleBuffered = true;
        Height = 64;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        AccessibleName = "連續光條預覽";
        AccessibleDescription = "將實體八顆 LED 的色彩連續漸層顯示為一整條光帶；暗色表示未點亮。";
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
        SetStyle(ControlStyles.ResizeRedraw, true);
        for (var i = 0; i < _gradientPositions.Length; i++)
            _gradientPositions[i] = i / 7f;
    }

    public void UpdateLevel(double level, double brightness)
    {
        Span<LedColor> colors = stackalloc LedColor[8];
        FrameMapper.Fill(level, brightness, colors);
        UpdateFrame(colors);
    }

    /// <summary>Copies only the latest RGB output; no animation timer or audio data is retained.</summary>
    public void UpdateFrame(ReadOnlySpan<LedColor> frame)
    {
        if (frame.Length != _colors.Length)
            throw new ArgumentException("Expected exactly 8 LED colours.", nameof(frame));
        if (frame.SequenceEqual(_colors)) return;
        frame.CopyTo(_colors);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 32 || ClientSize.Height < 12) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var bounds = new RectangleF(scale, 3 * scale,
            Math.Max(1, ClientSize.Width - 2 * scale), Math.Max(1, ClientSize.Height - 10 * scale));
        using var strip = UiTheme.RoundedRectangle(bounds, 6 * scale);
        var lit = false;
        for (var i = 0; i < 8; i++)
        {
            var color = _colors[i];
            _gradientColors[i] = Color.FromArgb(color.R, color.G, color.B);
            lit |= color.R != 0 || color.G != 0 || color.B != 0;
        }
        if (lit)
        {
            _blend.Colors = _gradientColors;
            _blend.Positions = _gradientPositions;
            using var gradient = new LinearGradientBrush(bounds, Color.Black, Color.Black, LinearGradientMode.Horizontal)
            {
                InterpolationColors = _blend
            };
            e.Graphics.FillPath(gradient, strip);
        }
        else
        {
            using var empty = new SolidBrush(UiTheme.Background);
            e.Graphics.FillPath(empty, strip);
        }
        using var outline = new Pen(lit ? Color.FromArgb(115, UiTheme.Accent) : UiTheme.Border, Math.Max(1, scale));
        e.Graphics.DrawPath(outline, strip);
        using var baseline = new Pen(Color.FromArgb(55, UiTheme.Accent), Math.Max(1, scale));
        e.Graphics.DrawLine(baseline, 1, ClientSize.Height - scale, ClientSize.Width - 1, ClientSize.Height - scale);
    }
}
