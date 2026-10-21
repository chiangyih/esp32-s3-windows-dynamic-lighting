using System.Diagnostics;

namespace MusicSync.Core;

/// <summary>Time-based envelope. Keeps smoothing independent of render rate.</summary>
public sealed class LevelMeter
{
    public double Level { get; private set; }

    public double Update(double rms, bool fresh, double elapsedSeconds, double gainDb)
    {
        rms = fresh && double.IsFinite(rms) ? Math.Clamp(rms, 0, 1) : 0;
        var db = rms > 0 ? 20 * Math.Log10(rms) + gainDb : -120;
        var target = db <= -55 ? 0 : Math.Clamp((db + 48) / 38, 0, 1);
        var timeConstant = target > Level ? 0.035 : 0.16;
        var alpha = 1 - Math.Exp(-Math.Clamp(elapsedSeconds, 0, 1) / timeConstant);
        Level += (target - Level) * alpha;
        if (Level < 0.002) Level = 0;
        return Level;
    }

    public void Reset() => Level = 0;

    public static bool IsFresh(long sampleTicks, long nowTicks) =>
        sampleTicks > 0 && nowTicks >= sampleTicks &&
        (nowTicks - sampleTicks) / (double)Stopwatch.Frequency <= 0.2;
}
