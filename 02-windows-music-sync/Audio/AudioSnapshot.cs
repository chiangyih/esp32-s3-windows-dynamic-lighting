namespace MusicSync.Audio;

/// <summary>Scalar audio features only. Audio bytes are never retained by this type.</summary>
public readonly record struct AudioSnapshot(double Rms, double Peak, long TimestampTicks,
    double BassRms = 0, double MidRms = 0, double TrebleRms = 0,
    double SpectralPosition = 0.5, double TempoBpm = 0, double BeatPulse = 0);
