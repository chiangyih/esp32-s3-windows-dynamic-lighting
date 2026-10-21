using MusicSync.Core;
using MusicSync.Lighting;

internal static class LampCountChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            checks++;
        }
        static bool Lit(LedColor color) => color.R != 0 || color.G != 0 || color.B != 0;

        foreach (var count in new[] { 1, 2, 8, 16, 64 })
        {
            var renderer = new MusicEffectRenderer();
            var guard = new LedColor(17, 31, 43);
            var storage = Enumerable.Repeat(guard, count + 2).ToArray();
            var frame = storage.AsSpan(1, count);
            var allModesValid = true;
            var allModesSilent = true;
            foreach (var animation in Enum.GetValues<AnimationMode>())
            foreach (var palette in Enum.GetValues<PaletteMode>())
            foreach (var signal in Enum.GetValues<SignalMode>())
            foreach (var speed in Enum.GetValues<SpeedMode>())
            {
                var settings = new EffectSettings { Animation = animation, Palette = palette,
                    Signal = signal, Speed = speed };
                renderer.Reset();
                renderer.Fill(0.8, 0.6, 0.3, 1, 120, 0.1, 0.7, settings, frame);
                var litCount = 0;
                foreach (var color in frame) if (Lit(color)) litCount++;
                allModesValid &= litCount == (animation == AnimationMode.Sequential ? 1 : count) &&
                    renderer.Phase >= 0 && renderer.Phase < 1;
                renderer.Fill(0, 0.6, 0.3, 1, 120, 0.1, 1, settings, frame);
                foreach (var color in frame) allModesSilent &= !Lit(color);
            }
            Check(allModesValid, $"All effect modes address the requested {count}-lamp span");
            Check(allModesSilent, $"Every {count}-lamp effect becomes black on silence");
            Check(storage[0] == guard && storage[^1] == guard, $"Effects never write outside a {count}-lamp slice");

            FrameMapper.Fill(1, 1, frame);
            var allLit = true;
            foreach (var color in frame) allLit &= Lit(color);
            Check(allLit, $"Legacy meter mapping fills all {count} lamps at full level");
            FrameMapper.Fill(0, 1, frame);
            var allDark = true;
            foreach (var color in frame) allDark &= !Lit(color);
            Check(allDark && storage[0] == guard && storage[^1] == guard,
                $"Legacy meter mapping clears only the requested {count}-lamp span");

            var sequential = new EffectSettings { Animation = AnimationMode.Sequential,
                Speed = SpeedMode.Custom, CustomCyclesPerSecond = 4 };
            var visited = new bool[count];
            renderer.Reset();
            renderer.Fill(1, 0.5, 0.5, 0, 240, 0, 1, sequential, frame);
            var previous = 0;
            visited[0] = true;
            var ordered = true;
            for (var step = 0; step < count * 3 + 3; step++)
            {
                // Include slow frames to exercise refresh-limited progression as well as 30 Hz.
                renderer.Fill(1, 0.5, 0.5, 0, 240, step % 7 == 0 ? 0.8 : 1.0 / 30,
                    1, sequential, frame);
                var current = -1;
                var litCount = 0;
                for (var index = 0; index < count; index++)
                    if (Lit(frame[index])) { current = index; litCount++; }
                ordered &= litCount == 1 && (current == previous || current == (previous + 1) % count);
                if (current >= 0) visited[current] = true;
                previous = current;
            }
            Check(ordered && visited.All(value => value),
                $"Sequential visits every one of {count} lamps in order without skipping");
        }

        foreach (var invalid in new[] { 0, 65 })
        {
            var rendererRejected = false;
            var mapperRejected = false;
            try { new MusicEffectRenderer().Fill(1, 0, 0, 0, 0, 0, 1, new(), new LedColor[invalid]); }
            catch (ArgumentOutOfRangeException) { rendererRejected = true; }
            try { FrameMapper.Fill(1, 1, new LedColor[invalid]); }
            catch (ArgumentOutOfRangeException) { mapperRejected = true; }
            Check(rendererRejected && mapperRejected, $"Out-of-range lamp count {invalid} is rejected");
        }

        var buffers = new[] { new LedColor[1], new LedColor[2], new LedColor[8], new LedColor[16], new LedColor[64] };
        var effect = new MusicEffectRenderer();
        var presets = Enum.GetValues<AnimationMode>().Select(mode => new EffectSettings { Animation = mode }).ToArray();
        for (var pass = 0; pass < 1000; pass++)
            foreach (var buffer in buffers)
                foreach (var preset in presets) effect.Fill(0.7, 0.2, 0.6, 0.5, 120, 0.033, 0.65, preset, buffer);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var pass = 0; pass < 1000; pass++)
            foreach (var buffer in buffers)
                foreach (var preset in presets) effect.Fill(0.7, 0.2, 0.6, 0.5, 120, 0.033, 0.65, preset, buffer);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Changing render spans across 1–64 lamps allocates no managed bytes ({allocated})");
        return checks;
    }
}
