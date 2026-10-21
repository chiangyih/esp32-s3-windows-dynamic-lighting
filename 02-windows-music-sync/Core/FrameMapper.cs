using MusicSync.Lighting;

namespace MusicSync.Core;

public static class FrameMapper
{
    public const int LampCount = LampCountLimits.Default;

    /// <summary>Green to amber to red, with a partially lit end segment.</summary>
    public static void Fill(double level, double brightness, Span<LedColor> frame)
    {
        LampCountLimits.Validate(frame.Length);
        level = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        brightness = double.IsFinite(brightness) ? Math.Clamp(brightness, 0, 1) : 0;
        for (var i = 0; i < frame.Length; i++)
        {
            var amount = Math.Clamp(level * frame.Length - i, 0, 1) * brightness;
            var position = frame.Length == 1 ? 0 : i * 7.0 / (frame.Length - 1);
            var red = position < 4 ? 30 + position * 30 : 255;
            var green = position < 4 ? 220 : 220 - (position - 3) * 45;
            frame[i] = new LedColor((byte)Math.Round(red * amount),
                (byte)Math.Round(green * amount), (byte)Math.Round(12 * amount));
        }
    }
}
