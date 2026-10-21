using MusicSync.Core;
using MusicSync.Lighting;

namespace MusicSync.UI;

internal sealed class MeterPreview : Control
{
    private readonly LedColor[] _colors = new LedColor[8];
    private double _level;
    private double _brightness;

    public MeterPreview()
    {
        DoubleBuffered = true;
        Height = 52;
        Dock = DockStyle.Fill;
        AccessibleName = "8 顆燈的音量預覽";
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void UpdateLevel(double level, double brightness)
    {
        if (brightness == _brightness && (level == _level ||
            (level > 0 && _level > 0 && Math.Abs(level - _level) < 0.005))) return;
        _level = level;
        _brightness = brightness;
        FrameMapper.Fill(level, brightness, _colors);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var gap = Math.Max(4, (int)(6 * DeviceDpi / 96.0));
        var width = Math.Max(1, (ClientSize.Width - gap * 7) / 8);
        for (var i = 0; i < 8; i++)
        {
            var color = _colors[i];
            using var brush = new SolidBrush(color.R == 0 && color.G == 0 && color.B == 0
                ? SystemColors.ControlDark : Color.FromArgb(color.R, color.G, color.B));
            e.Graphics.FillRectangle(brush, i * (width + gap), 4, width, Math.Max(1, Height - 8));
        }
    }
}
