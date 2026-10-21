using System.Buffers.Binary;
using MusicSync.Audio;

internal static class FeatureChecks
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Audio feature check: " + message);
            checks++;
        }

        var mono = new WaveFormatInfo(true, 1, 48000, 4, 32, 32);
        var stereo = new WaveFormatInfo(true, 2, 48000, 8, 32, 32);
        var lowTone = Tone(mono, 80, 0.4, 4800);
        var highTone = Tone(mono, 6000, 0.4, 4800);
        var bassAnalyzer = new AudioFeatureAnalyzer(mono);
        bassAnalyzer.AddAudio(lowTone, 4800);
        var low = bassAnalyzer.TakeSnapshot(1);
        var trebleAnalyzer = new AudioFeatureAnalyzer(mono);
        trebleAnalyzer.AddAudio(highTone, 4800);
        var high = trebleAnalyzer.TakeSnapshot(2);
        Check(low.BassRms > low.TrebleRms * 8, "80Hz favors bass band");
        Check(high.TrebleRms > high.BassRms * 8, "6kHz favors treble band");
        Check(high.SpectralPosition > low.SpectralPosition + 0.5, "broad timbre position follows frequency");
        Check(Math.Abs(low.Rms - 0.4 / Math.Sqrt(2)) < 0.002, "RMS retained by single-pass analyzer");
        Check(low.TempoBpm == 0, "one onset does not produce a tempo estimate");

        var opposite = Tone(stereo, 80, 0.4, 4800, antiphase: true);
        var oppositeAnalyzer = new AudioFeatureAnalyzer(stereo);
        oppositeAnalyzer.AddAudio(opposite, 4800);
        var oppositeSnapshot = oppositeAnalyzer.TakeSnapshot(3);
        Check(Math.Abs(oppositeSnapshot.Rms - low.Rms) < 1e-8, "opposite stereo channels preserve RMS");
        Check(Math.Abs(oppositeSnapshot.BassRms - low.BassRms) < 1e-8, "opposite stereo channels preserve band energy");

        // Deterministic synthetic 100Hz bursts: 60ms wide, one every500ms.
        var pulses = Tone(mono, 100, 0.5, mono.SampleRate * 7, pulsePeriod: 0.5);
        var rhythm = new AudioFeatureAnalyzer(mono);
        double measuredTempo = 0, maximumPulse = 0;
        for (var frame = 0; frame < mono.SampleRate * 7; frame += 480)
        {
            rhythm.AddAudio(pulses.AsSpan(frame * 4, 480 * 4), 480);
            var snapshot = rhythm.TakeSnapshot(frame + 4);
            if (snapshot.TempoBpm > 0) measuredTempo = snapshot.TempoBpm;
            maximumPulse = Math.Max(maximumPulse, snapshot.BeatPulse);
        }
        Check(Math.Abs(measuredTempo - 120) < 4, "repeated 120BPM onsets estimate tempo");
        Check(maximumPulse > 0.9, "onsets produce beat pulse");
        foreach (var expectedTempo in new[] { 60, 180 })
        {
            var otherPulses = Tone(mono, 100, 0.5, mono.SampleRate * 7, pulsePeriod: 60.0 / expectedTempo);
            var otherRhythm = new AudioFeatureAnalyzer(mono);
            double otherTempo = 0;
            for (var frame = 0; frame < mono.SampleRate * 7; frame += 480)
            {
                otherRhythm.AddAudio(otherPulses.AsSpan(frame * 4, 480 * 4), 480);
                var snapshot = otherRhythm.TakeSnapshot(frame + 4);
                if (snapshot.TempoBpm > 0) otherTempo = snapshot.TempoBpm;
            }
            Check(Math.Abs(otherTempo - expectedTempo) < 5, $"repeated {expectedTempo}BPM onsets estimate tempo");
        }
        rhythm.AddSilence(4800);
        var quiet = rhythm.TakeSnapshot(10);
        Check(quiet.Rms == 0 && quiet.TempoBpm == 0 && quiet.BeatPulse == 0, "digital silence emits no beat or tempo");
        rhythm.Reset();
        var reset = rhythm.TakeSnapshot(11);
        Check(reset.Rms == 0 && reset.BassRms == 0 && reset.MidRms == 0 && reset.TrebleRms == 0 && reset.TempoBpm == 0,
            "discontinuity reset clears feature history");

        var constant = new AudioFeatureAnalyzer(mono);
        for (var i = 0; i < 60; i++) constant.AddAudio(lowTone, 4800);
        Check(constant.TakeSnapshot(12).TempoBpm == 0, "steady tone is not repeated beat evidence");

        var bad = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bad, float.NaN);
        BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(4), float.PositiveInfinity);
        BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(8), float.NegativeInfinity);
        var finite = new AudioFeatureAnalyzer(mono);
        finite.AddAudio(bad, 3);
        var finiteSnapshot = finite.TakeSnapshot(13);
        Check(finiteSnapshot.Rms == 0 && finiteSnapshot.BassRms == 0 && finiteSnapshot.MidRms == 0 && finiteSnapshot.TrebleRms == 0,
            "nonfinite samples cannot contaminate filters");

        var alternateRate = new WaveFormatInfo(true, 1, 8000, 4, 32, 32);
        var alternate = new AudioFeatureAnalyzer(alternateRate);
        alternate.AddAudio(Tone(alternateRate, 80, 0.4, 800), 800);
        var alternateSnapshot = alternate.TakeSnapshot(14);
        Check(alternateSnapshot.BassRms > alternateSnapshot.TrebleRms * 8, "filter coefficients follow endpoint sample rate");

        var allocation = new AudioFeatureAnalyzer(mono);
        for (var i = 0; i < 10; i++) { allocation.AddAudio(lowTone, 4800); allocation.TakeSnapshot(i); }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) { allocation.AddAudio(lowTone, 4800); allocation.TakeSnapshot(i); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(bytes == 0, $"warmed feature processing allocates zero bytes (actual {bytes})");
        return checks;
    }

    private static byte[] Tone(WaveFormatInfo format, double frequency, double amplitude, int frames,
        bool antiphase = false, double pulsePeriod = 0)
    {
        var bytes = new byte[frames * format.BlockAlign];
        for (var frame = 0; frame < frames; frame++)
        {
            var time = frame / (double)format.SampleRate;
            var active = pulsePeriod <= 0 || time % pulsePeriod < 0.06;
            var value = active ? amplitude * Math.Sin(2 * Math.PI * frequency * time) : 0;
            for (var channel = 0; channel < format.Channels; channel++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(frame * format.BlockAlign + channel * 4),
                    (float)(antiphase && channel % 2 == 1 ? -value : value));
        }
        return bytes;
    }
}
