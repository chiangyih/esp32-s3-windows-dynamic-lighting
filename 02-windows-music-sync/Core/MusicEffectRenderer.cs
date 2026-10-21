using MusicSync.Lighting;

namespace MusicSync.Core;

/// <summary>Allocation-free 8-lamp music effects. All modes fade to black with the input envelope.</summary>
public sealed class MusicEffectRenderer
{
    private double _phase;

    public double Phase => _phase;
    public double CurrentCyclesPerSecond { get; private set; } = 0.5;

    public void Reset()
    {
        _phase = 0;
        CurrentCyclesPerSecond = 0.5;
    }

    public void Fill(double level, double spectralPosition, double bassBalance, double beatPulse,
        double tempoBpm, double deltaSeconds, double brightness, EffectSettings settings, Span<LedColor> frame)
    {
        if (frame.Length != 8) throw new ArgumentException("Expected exactly 8 LEDs.", nameof(frame));
        ArgumentNullException.ThrowIfNull(settings);
        level = Unit(level);
        brightness = Unit(brightness);
        beatPulse = Unit(beatPulse);
        CurrentCyclesPerSecond = settings.Speed switch
        {
            SpeedMode.Beat => double.IsFinite(tempoBpm) && tempoBpm >= 40 && tempoBpm <= 240 ? tempoBpm / 60 : 0.5,
            SpeedMode.Style => Rate(settings.StyleCyclesPerSecond, 0.75),
            _ => Rate(settings.CustomCyclesPerSecond, 1)
        };
        var elapsed = double.IsFinite(deltaSeconds) ? Math.Clamp(deltaSeconds, 0, 1) : 0;
        var advance = elapsed * CurrentCyclesPerSecond;
        if (settings.Animation == AnimationMode.Sequential && elapsed > 0)
        {
            // At 30 Hz, 4 complete 8-lamp cycles would require 32 distinct steps per second.
            // Advance by less than one lamp per rendered frame, even after a scheduling stall.
            advance = Math.Min(advance, 1.0 / 8 - 1e-10);
            CurrentCyclesPerSecond = advance / elapsed;
        }
        _phase = Wrap(_phase + advance);

        if (level == 0 || brightness == 0)
        {
            frame.Clear();
            return;
        }

        // Avoid mapping both endpoints of the measured range to the same cyclic palette color.
        var signalPosition = 0.8 * (settings.Signal switch
        {
            SignalMode.Frequency => Unit(spectralPosition),
            SignalMode.BassTreble => Unit(bassBalance),
            _ => level
        });
        var intensity = level * brightness;
        if (settings.Speed == SpeedMode.Beat)
            intensity *= 0.85 + 0.15 * beatPulse;

        switch (settings.Animation)
        {
            case AnimationMode.Breathing:
                // Keep an audible sustained note visible at the trough, while silence still becomes black.
                var breath = 0.25 + 0.75 * (0.5 - 0.5 * Math.Cos(_phase * Math.Tau));
                frame.Fill(Scale(Palette(signalPosition, settings), intensity * breath));
                break;
            case AnimationMode.Sequential:
                frame.Clear();
                var lamp = Math.Min(7, (int)(_phase * 8));
                frame[lamp] = Scale(Palette(signalPosition, settings), intensity);
                break;
            default:
                for (var index = 0; index < frame.Length; index++)
                    frame[index] = Scale(Palette(signalPosition + _phase + index / 8.0, settings), intensity);
                break;
        }
    }

    private static LedColor Palette(double position, EffectSettings settings)
    {
        position = Wrap(position);
        if (settings.Palette == PaletteMode.Rainbow)
            return Rainbow(position);
        if (settings.Palette == PaletteMode.WindowsTheme)
        {
            // Accent-derived dark/bright variations follow Windows without introducing an unrelated hue.
            var accent = settings.ThemeColor;
            var light = Mix(accent, new LedColor(255, 255, 255), 0.48);
            var dark = Scale(accent, 0.28);
            return position < 0.5 ? Mix(dark, light, position * 2) : Mix(light, dark, (position - 0.5) * 2);
        }

        var count = Math.Clamp(settings.CustomColorCount, 2, 4);
        var scaled = position * count;
        var current = Math.Min(count - 1, (int)scaled);
        var next = (current + 1) % count;
        return Mix(CustomColor(settings, current), CustomColor(settings, next), scaled - current);
    }

    private static LedColor CustomColor(EffectSettings settings, int index) => index switch
    {
        1 => settings.Color2,
        2 => settings.Color3,
        3 => settings.Color4,
        _ => settings.Color1
    };

    private static LedColor Rainbow(double hue)
    {
        var position = hue * 6;
        var region = (int)position;
        var rising = Channel(position - region);
        var falling = (byte)(255 - rising);
        return region switch
        {
            0 => new(255, rising, 0),
            1 => new(falling, 255, 0),
            2 => new(0, 255, rising),
            3 => new(0, falling, 255),
            4 => new(rising, 0, 255),
            _ => new(255, 0, falling)
        };
    }

    private static LedColor Mix(LedColor first, LedColor second, double blend) =>
        new((byte)Math.Round(first.R + (second.R - first.R) * blend),
            (byte)Math.Round(first.G + (second.G - first.G) * blend),
            (byte)Math.Round(first.B + (second.B - first.B) * blend));

    private static LedColor Scale(LedColor value, double intensity) =>
        new((byte)Math.Round(value.R * intensity), (byte)Math.Round(value.G * intensity), (byte)Math.Round(value.B * intensity));

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Rate(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0.1, 4) : fallback;
    private static double Wrap(double value) => value - Math.Floor(value);
    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
