using MusicSync.Core;
using MusicSync.Lighting;

internal static class EffectChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            count++;
        }
        static bool Lit(LedColor color) => color.R != 0 || color.G != 0 || color.B != 0;
        var renderer = new MusicEffectRenderer();
        var frame = new LedColor[8];
        var reference = new LedColor[8];
        var settings = new EffectSettings { Speed = SpeedMode.Custom, CustomCyclesPerSecond = 1 };
        foreach (var animation in Enum.GetValues<AnimationMode>())
        foreach (var palette in Enum.GetValues<PaletteMode>())
        foreach (var signal in Enum.GetValues<SignalMode>())
        {
            settings = settings with { Animation = animation, Palette = palette, Signal = signal };
            renderer.Fill(0, 0.7, 0.2, 1, 120, 0.01, 1, settings, frame);
            Check(frame.All(c => !Lit(c)), $"Silence is dark for {animation}/{palette}/{signal}");
        }

        settings = new EffectSettings { Speed = SpeedMode.Custom, Animation = AnimationMode.Flow };
        renderer.Reset();
        renderer.Fill(0.9, 0.5, 0.5, 0, 0, 0, 1, settings, frame);
        Check(frame.All(Lit) && frame.Distinct().Count() > 4, "Flow uses the full strip with a spatial rainbow");
        frame.CopyTo(reference, 0);
        renderer.Fill(0.9, 0.5, 0.5, 0, 0, 0.1, 1, settings, frame);
        Check(!frame.SequenceEqual(reference), "Flow advances with elapsed time");

        settings = settings with { Animation = AnimationMode.Breathing };
        renderer.Reset();
        renderer.Fill(1, 0, 0, 0, 0, 0, 1, settings, frame);
        Check(frame.All(c => c == frame[0]) && frame.All(Lit), "Breathing fills every lamp uniformly");
        var trough = frame[0];
        renderer.Fill(1, 0, 0, 0, 0, 0.5, 1, settings, frame);
        Check(frame[0].R + frame[0].G + frame[0].B > trough.R + trough.G + trough.B,
            "Breathing varies brightness across the cycle");

        settings = settings with { Animation = AnimationMode.Sequential };
        renderer.Reset();
        renderer.Fill(1, 0, 0, 0, 0, 0, 1, settings, frame);
        Check(frame.Count(Lit) == 1 && Lit(frame[0]), "Sequential starts with a single lamp");
        renderer.Fill(1, 0, 0, 0, 0, 0.1, 1, settings, frame);
        renderer.Fill(1, 0, 0, 0, 0, 0.1, 1, settings, frame);
        Check(frame.Count(Lit) == 1 && Lit(frame[1]), "Sequential advances to the next lamp");
        for (var i = 0; i < 9; i++) renderer.Fill(1, 0, 0, 0, 0, 0.1, 1, settings, frame);
        Check(frame.Count(Lit) == 1 && Lit(frame[0]), "Sequential wraps after the eighth lamp");

        static int LitIndex(LedColor[] colors) => Array.FindIndex(colors, Lit);
        settings = settings with { CustomCyclesPerSecond = 4 };
        renderer.Reset();
        renderer.Fill(1, 0, 0, 0, 240, 0, 1, settings, frame);
        var previousLamp = LitIndex(frame);
        var orderedAtThirtyHz = true;
        var visitedLamps = 1 << previousLamp;
        for (var i = 0; i < 240; i++)
        {
            renderer.Fill(1, 0, 0, 0, 240, 1.0 / 30, 1, settings, frame);
            var nextLamp = LitIndex(frame);
            orderedAtThirtyHz &= nextLamp == previousLamp || nextLamp == (previousLamp + 1) % 8;
            visitedLamps |= 1 << nextLamp;
            previousLamp = nextLamp;
        }
        Check(orderedAtThirtyHz && visitedLamps == 255, "Sequential custom4 at30Hz visits all lamps without skipped indices");
        Check(renderer.CurrentCyclesPerSecond < 3.75 && renderer.CurrentCyclesPerSecond > 3.7,
            "Sequential exposes its refresh-limited effective cycle rate");
        var orderedAfterStall = true;
        for (var i = 0; i < 20; i++)
        {
            renderer.Fill(1, 0, 0, 0, 240, 0.8, 1, settings with { Speed = SpeedMode.Beat }, frame);
            var nextLamp = LitIndex(frame);
            orderedAfterStall &= nextLamp == previousLamp || nextLamp == (previousLamp + 1) % 8;
            previousLamp = nextLamp;
        }
        Check(orderedAfterStall && renderer.CurrentCyclesPerSecond < 0.157,
            "Sequential 240BPM preserves lamp order across slow render frames");

        settings = settings with { CustomCyclesPerSecond = 1 };

        settings = settings with { Animation = AnimationMode.Breathing };
        foreach (var signal in Enum.GetValues<SignalMode>())
        {
            settings = settings with { Signal = signal };
            renderer.Reset();
            renderer.Fill(0.2, 0.2, 0.2, 0, 0, 0, 1, settings, frame);
            var first = frame[0];
            renderer.Fill(signal == SignalMode.Volume ? 0.8 : 0.2,
                signal == SignalMode.Frequency ? 0.8 : 0.2,
                signal == SignalMode.BassTreble ? 0.8 : 0.2, 0, 0, 0, 1, settings, frame);
            Check(frame[0] != first, $"Selected signal {signal} changes palette position");
        }

        settings = settings with { Palette = PaletteMode.Custom, Signal = SignalMode.Volume,
            Color1 = new(255, 0, 0), Color2 = new(0, 255, 0), Color3 = new(0, 0, 255), Color4 = new(255, 255, 255) };
        for (var colors = 2; colors <= 4; colors++)
        {
            renderer.Reset();
            renderer.Fill(1, 0, 0, 0, 0, 0, 1, settings with { CustomColorCount = colors }, frame);
            Check(Lit(frame[0]), $"Custom palette supports {colors} colors");
        }
        renderer.Reset();
        renderer.Fill(0.7, 0, 0, 0, 0, 0, 1, settings with { CustomColorCount = 2 }, frame);
        var twoColor = frame[0];
        renderer.Fill(0.7, 0, 0, 0, 0, 0, 1, settings with { CustomColorCount = 4 }, frame);
        Check(twoColor != frame[0], "Adding custom colors changes palette interpolation");

        settings = new EffectSettings { Palette = PaletteMode.WindowsTheme, Animation = AnimationMode.Flow,
            Speed = SpeedMode.Custom, ThemeColor = new(20, 80, 200) };
        renderer.Reset();
        renderer.Fill(0.8, 0, 0, 0, 0, 0, 1, settings, frame);
        Check(frame.All(c => c.B >= c.G && c.G >= c.R), "Theme palette retains the selected Windows accent hue");
        var blue = frame[0];
        renderer.Fill(0.8, 0, 0, 0, 0, 0, 1, settings with { ThemeColor = new(220, 50, 10) }, frame);
        Check(blue != frame[0] && frame[0].R >= frame[0].G, "Windows accent refresh changes colors");

        settings = new EffectSettings { Speed = SpeedMode.Beat };
        renderer.Reset();
        renderer.Fill(1, 0, 0, 0, 120, 0.1, 1, settings, frame);
        Check(Math.Abs(renderer.CurrentCyclesPerSecond - 2) < 0.0001 && Math.Abs(renderer.Phase - 0.2) < 0.0001,
            "Beat speed follows a 120 BPM estimate");
        renderer.Fill(1, 0, 0, 0, 0, 0, 1, settings, frame);
        Check(renderer.CurrentCyclesPerSecond == 0.5, "Missing tempo uses a stable slow fallback");
        renderer.Fill(1, 0, 0, 0, double.NaN, 0, 1, settings, frame);
        Check(renderer.CurrentCyclesPerSecond == 0.5, "Invalid tempo does not accelerate the effect");
        renderer.Fill(1, 0, 0, 0, 0, 0, 1,
            settings with { Speed = SpeedMode.Style, StyleCyclesPerSecond = 0.35 }, frame);
        Check(renderer.CurrentCyclesPerSecond == 0.35, "Style speed uses the selected slow pace");
        renderer.Fill(1, 0, 0, 0, 0, 0, 1,
            settings with { Speed = SpeedMode.Custom, CustomCyclesPerSecond = 3.5 }, frame);
        Check(renderer.CurrentCyclesPerSecond == 3.5, "Custom speed uses the selected cycles per second");
        renderer.Fill(1, 0, 0, 0, 0, 0, 1,
            settings with { Speed = SpeedMode.Custom, CustomCyclesPerSecond = 999 }, frame);
        Check(renderer.CurrentCyclesPerSecond == 4, "Custom speed is bounded");
        renderer.Fill(1, 0, 0, 0, 0, 0, 0, settings, frame);
        Check(frame.All(c => !Lit(c)), "Zero brightness suppresses all modes");
        renderer.Fill(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, settings, frame);
        Check(frame.All(c => !Lit(c)) && double.IsFinite(renderer.Phase), "Invalid input remains dark and phase stays finite");

        settings = new EffectSettings();
        for (var i = 0; i < 10000; i++) renderer.Fill(0.7, 0.2, 0.6, 0.5, 120, 0.033, 0.65, settings, frame);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) renderer.Fill(0.7, 0.2, 0.6, 0.5, 120, 0.033, 0.65, settings, frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(allocated == 0, $"Music renderer reuses its frame without managed allocation ({allocated} bytes)");
        return count;
    }
}
