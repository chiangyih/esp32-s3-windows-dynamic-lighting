namespace MusicSync.Audio;

/// <summary>
/// Reusable broad-band energy and onset-interval analyzer. First-order 250/2500 Hz
/// filters are deliberately modest: SpectralPosition is a broad timbre index, not
/// a measured fundamental frequency or a full FFT spectrum. No audio is retained.
/// Each channel has independent state so opposite stereo phases cannot cancel.
/// </summary>
internal sealed class AudioFeatureAnalyzer
{
    private readonly WaveFormatInfo _format;
    private readonly double[] _low;
    private readonly double[] _high;
    private readonly double _lowCoefficient;
    private readonly double _highCoefficient;
    private readonly int _hopFrames;
    private readonly double[] _intervals = new double[5];
    private readonly double[] _sorted = new double[5];
    private double _squareSum, _peak, _bassSquares, _midSquares, _trebleSquares;
    private long _sampleCount, _elapsedFrames;
    private double _hopSquares;
    private int _hopCount, _intervalCount, _intervalIndex;
    private double _averageEnergy, _previousEnergy, _lastOnset = -10;
    private double _tempo, _beatPulse, _silenceSeconds;

    public AudioFeatureAnalyzer(WaveFormatInfo format)
    {
        _format = format;
        _low = new double[format.Channels];
        _high = new double[format.Channels];
        _lowCoefficient = 1 - Math.Exp(-2 * Math.PI * 250 / format.SampleRate);
        _highCoefficient = 1 - Math.Exp(-2 * Math.PI * 2500 / format.SampleRate);
        _hopFrames = Math.Max(1, format.SampleRate / 100);
    }

    public void AddAudio(ReadOnlySpan<byte> audio, int frames)
    {
        var statistics = PcmSampleDecoder.Analyze(audio, frames, _format, this);
        _squareSum += statistics.SquareSum;
        _sampleCount += statistics.SampleCount;
        _peak = Math.Max(_peak, statistics.Peak);
    }

    public void AddSilence(int frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < _format.Channels; channel++) AddSample(0, channel);
            FinishFrame();
        }
        _sampleCount += (long)frames * _format.Channels;
    }

    internal void AddSample(double sample, int channel)
    {
        var low = _low[channel] + _lowCoefficient * (sample - _low[channel]);
        var high = _high[channel] + _highCoefficient * (sample - _high[channel]);
        // Prevent denormal tails while an endpoint streams digital silence.
        _low[channel] = Math.Abs(low) < 1e-15 ? 0 : low;
        _high[channel] = Math.Abs(high) < 1e-15 ? 0 : high;
        var mid = high - low;
        var treble = sample - high;
        _bassSquares += low * low;
        _midSquares += mid * mid;
        _trebleSquares += treble * treble;
        _hopSquares += sample * sample;
    }

    internal void FinishFrame()
    {
        _elapsedFrames++;
        if (++_hopCount < _hopFrames) return;
        var energy = Math.Sqrt(_hopSquares / (_hopCount * _format.Channels));
        UpdateOnsets(energy, _hopCount / (double)_format.SampleRate);
        _hopCount = 0;
        _hopSquares = 0;
    }

    private void UpdateOnsets(double energy, double seconds)
    {
        var time = _elapsedFrames / (double)_format.SampleRate;
        _beatPulse *= Math.Exp(-seconds / 0.12);
        if (energy < 0.0005)
        {
            _silenceSeconds += seconds;
            if (_silenceSeconds > 1.5) ResetTempo();
        }
        else _silenceSeconds = 0;

        // An onset must rise above both the recent level and a slower adaptive
        // baseline. A 240 ms refractory interval limits echo/double triggers.
        var onset = energy > 0.002 && energy > _averageEnergy * 1.45 &&
            energy > _previousEnergy * 1.18 && time - _lastOnset >= 0.24;
        _averageEnergy += (1 - Math.Exp(-seconds / 0.35)) * (energy - _averageEnergy);
        _previousEnergy = energy;
        if (onset)
        {
            var interval = time - _lastOnset;
            _lastOnset = time;
            _beatPulse = 1;
            if (interval >= 0.25 && interval <= 1.5)
            {
                _intervals[_intervalIndex] = interval;
                _intervalIndex = (_intervalIndex + 1) % _intervals.Length;
                _intervalCount = Math.Min(_intervalCount + 1, _intervals.Length);
                if (_intervalCount >= 3)
                {
                    Array.Copy(_intervals, _sorted, _intervalCount);
                    Array.Sort(_sorted, 0, _intervalCount);
                    var median = _sorted[_intervalCount / 2];
                    var agreeing = 0;
                    for (var i = 0; i < _intervalCount; i++)
                        if (Math.Abs(_intervals[i] - median) <= median * 0.2) agreeing++;
                    _tempo = agreeing >= Math.Max(3, _intervalCount - 1) ? 60 / median : 0;
                }
            }
            else ResetIntervals();
        }
        if (time - _lastOnset > 3) _tempo = 0;
        if (_beatPulse < 0.001) _beatPulse = 0;
    }

    public AudioSnapshot TakeSnapshot(long timestampTicks)
    {
        var rms = _sampleCount > 0 ? Math.Sqrt(_squareSum / _sampleCount) : 0;
        var bass = _sampleCount > 0 ? Math.Sqrt(_bassSquares / _sampleCount) : 0;
        var mid = _sampleCount > 0 ? Math.Sqrt(_midSquares / _sampleCount) : 0;
        var treble = _sampleCount > 0 ? Math.Sqrt(_trebleSquares / _sampleCount) : 0;
        var total = _bassSquares + _midSquares + _trebleSquares;
        var position = total > 1e-12 ? (0.5 * _midSquares + _trebleSquares) / total : 0.5;
        var audible = rms >= 0.0005;
        var result = new AudioSnapshot(rms, _peak, timestampTicks, bass, mid, treble,
            Math.Clamp(position, 0, 1), audible ? _tempo : 0, audible ? _beatPulse : 0);
        _squareSum = _peak = _bassSquares = _midSquares = _trebleSquares = 0;
        _sampleCount = 0;
        return result;
    }

    /// <summary>Driver discontinuity or missing packets invalidates old frequency/tempo history.</summary>
    public void Reset()
    {
        Array.Clear(_low);
        Array.Clear(_high);
        _squareSum = _peak = _bassSquares = _midSquares = _trebleSquares = 0;
        _sampleCount = _elapsedFrames = 0;
        _hopSquares = _averageEnergy = _previousEnergy = _silenceSeconds = 0;
        _hopCount = 0;
        ResetTempo();
    }

    private void ResetTempo()
    {
        _lastOnset = -10;
        _beatPulse = 0;
        ResetIntervals();
    }

    private void ResetIntervals()
    {
        _intervalCount = _intervalIndex = 0;
        _tempo = 0;
    }
}
