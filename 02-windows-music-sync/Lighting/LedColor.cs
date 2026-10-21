namespace MusicSync.Lighting;

/// <summary>An RGB value sent through the Windows LampArray API.</summary>
public readonly record struct LedColor(byte R, byte G, byte B);
