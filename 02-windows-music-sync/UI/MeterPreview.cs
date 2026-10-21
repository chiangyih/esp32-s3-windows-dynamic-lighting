using System.Drawing.Drawing2D;
using MusicSync.Core;
using MusicSync.Lighting;

namespace MusicSync.UI;

internal sealed class MeterPreview : Control
{
    private LedColor[] _colors = Array.Empty<LedColor>();
    private Color[] _gradientColors = Array.Empty<Color>();
    private float[] _gradientPositions = Array.Empty<float>();
    private ColorBlend? _blend;

    public MeterPreview()
    {
        DoubleBuffered = true;
        Height = 64;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        AccessibleName = "連續光條預覽";
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
        SetStyle(ControlStyles.ResizeRedraw, true);
        ConfigureCount(LampCountLimits.Default);
    }

    public void UpdateLevel(double level, double brightness)
    {
        Span<LedColor> colors = stackalloc LedColor[LampCountLimits.Maximum];
        var frame = colors[.._colors.Length];
        FrameMapper.Fill(level, brightness, frame);
        UpdateFrame(frame);
    }

    /// <summary>Copies only the latest RGB output; no animation timer or audio data is retained.</summary>
    public void UpdateFrame(ReadOnlySpan<LedColor> frame)
    {
        LampCountLimits.Validate(frame.Length);
        var resized = frame.Length != _colors.Length;
        if (resized) ConfigureCount(frame.Length);
        if (!resized && frame.SequenceEqual(_colors)) return;
        frame.CopyTo(_colors);
        Invalidate();
    }

    private void ConfigureCount(int count)
    {
        _colors = new LedColor[count];
        _gradientColors = new Color[count];
        _gradientPositions = new float[count];
        _blend = count > 1 ? new ColorBlend(count) : null;
        for (var i = 0; i < count; i++)
            _gradientPositions[i] = count == 1 ? 0 : i / (float)(count - 1);
        if (_blend is not null) _blend.Positions = _gradientPositions;
        AccessibleDescription = $"將 {count} 顆 LED 的色彩連續漸層顯示為一整條光帶；暗色表示未點亮。";
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
        for (var i = 0; i < _colors.Length; i++)
        {
            var color = _colors[i];
            _gradientColors[i] = Color.FromArgb(color.R, color.G, color.B);
            lit |= color.R != 0 || color.G != 0 || color.B != 0;
        }
        if (lit && _colors.Length == 1)
        {
            using var solid = new SolidBrush(_gradientColors[0]);
            e.Graphics.FillPath(solid, strip);
        }
        else if (lit)
        {
            var blend = _blend!;
            blend.Colors = _gradientColors;
            using var gradient = new LinearGradientBrush(bounds, Color.Black, Color.Black, LinearGradientMode.Horizontal)
            {
                InterpolationColors = blend
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
