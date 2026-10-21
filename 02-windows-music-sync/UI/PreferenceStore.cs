using System.Text.Json;
using System.Text.Json.Serialization;
using MusicSync.Core;
using MusicSync.Lighting;

namespace MusicSync.UI;

internal sealed record UserPreferences
{
    public int GainDb { get; init; }
    public int BrightnessPercent { get; init; } = 65;
    public int LampCount { get; init; } = LampCountLimits.Default;
    public EffectSettings Effect { get; init; } = new();
}

internal static class PreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Esp32MusicSync", "settings.json");

    internal static UserPreferences Load()
    {
        try
        {
            if (new FileInfo(SettingsPath) is { Exists: true, Length: < 16384 })
            {
                var value = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(SettingsPath), JsonOptions);
                if (value?.Effect is { } effect && Enum.IsDefined(effect.Signal) && Enum.IsDefined(effect.Palette) &&
                    Enum.IsDefined(effect.Animation) && Enum.IsDefined(effect.Speed))
                    return value with { LampCount = Math.Clamp(value.LampCount, LampCountLimits.Minimum, LampCountLimits.Maximum),
                        GainDb = Math.Clamp(value.GainDb, -12, 24),
                        BrightnessPercent = Math.Clamp(value.BrightnessPercent, 5, 100),
                        Effect = effect with { CustomColorCount = Math.Clamp(effect.CustomColorCount, 2, 4),
                            StyleCyclesPerSecond = FiniteSpeed(effect.StyleCyclesPerSecond, 0.75),
                            CustomCyclesPerSecond = FiniteSpeed(effect.CustomCyclesPerSecond, 1) } };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return new UserPreferences();
    }

    internal static bool Save(UserPreferences value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static double FiniteSpeed(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0.1, 4) : fallback;
}
