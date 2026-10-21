using MusicSync.Lighting;

namespace MusicSync.Core;

public enum SignalMode { Volume, Frequency, BassTreble }
public enum PaletteMode { WindowsTheme, Rainbow, Custom }
public enum AnimationMode { Breathing, Flow, Sequential }
public enum SpeedMode { Beat, Style, Custom }

/// <summary>Small immutable settings snapshot, shared between the UI and render worker.</summary>
public sealed record EffectSettings
{
    public SignalMode Signal { get; init; } = SignalMode.Volume;
    public PaletteMode Palette { get; init; } = PaletteMode.Rainbow;
    public AnimationMode Animation { get; init; } = AnimationMode.Flow;
    public SpeedMode Speed { get; init; } = SpeedMode.Beat;
    public double StyleCyclesPerSecond { get; init; } = 0.75;
    public double CustomCyclesPerSecond { get; init; } = 1;
    public LedColor ThemeColor { get; init; } = new(129, 216, 208);
    public int CustomColorCount { get; init; } = 2;
    public LedColor Color1 { get; init; } = new(129, 216, 208);
    public LedColor Color2 { get; init; } = new(133, 115, 230);
    public LedColor Color3 { get; init; } = new(248, 178, 102);
    public LedColor Color4 { get; init; } = new(240, 128, 168);
}
