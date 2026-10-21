using MusicSync.Lighting;

namespace MusicSync.Core;

public static class FrameMapper
{
    public const int LampCount = 8;

    /// <summary>Green to amber to red, with a partially lit end segment.</summary>
    public static void Fill(double level, double brightness, Span<LedColor> frame)
    {
        if (frame.Length != LampCount) throw new ArgumentException("Expected exactly 8 LEDs.");
        level = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        brightness = double.IsFinite(brightness) ? Math.Clamp(brightness, 0, 1) : 0;
        for (var i = 0; i < LampCount; i++)
        {
            var amount = Math.Clamp(level * LampCount - i, 0, 1) * brightness;
            var red = i < 4 ? 30 + i * 30 : 255;
            var green = i < 4 ? 220 : 220 - (i - 3) * 45;
            frame[i] = new LedColor((byte)Math.Round(red * amount),
                (byte)Math.Round(green * amount), (byte)Math.Round(12 * amount));
        }
    }
}
