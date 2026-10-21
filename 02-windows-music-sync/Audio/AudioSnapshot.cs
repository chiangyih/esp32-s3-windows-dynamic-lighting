namespace MusicSync.Audio;

/// <summary>A level measurement only. Audio bytes are never retained by this type.</summary>
public readonly record struct AudioSnapshot(double Rms, double Peak, long TimestampTicks);
