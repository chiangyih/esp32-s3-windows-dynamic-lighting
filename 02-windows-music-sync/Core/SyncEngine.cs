using System.Diagnostics;
using MusicSync.Audio;
using MusicSync.Lighting;

namespace MusicSync.Core;

/// <summary>Single render worker, reusable frame, no audio buffers or recording.</summary>
public sealed class SyncEngine : IDisposable
{
    private readonly WasapiLoopbackCapture _capture = new();
    private readonly LampArrayController _lamps = new();
    private readonly LevelMeter _meter = new();
    private readonly LedColor[] _frame = new LedColor[FrameMapper.LampCount];
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _renderTask;
    private double _rms;
    private double _peak;
    private double _level;
    private double _gainDb;
    private double _brightness = 0.65;
    private long _sampleTicks;
    private long _audioPackets;
    private long _sentFrames;
    private long _blackFramesSent;
    private double _maximumRms;
    private string _audioStatus = "尚未開始";
    private bool _disposed;
    private bool _stopping;

    public bool IsRunning { get { lock (_lifecycleGate) return _cancellation != null && !_stopping; } }
    public double Level => Volatile.Read(ref _level);
    public double Rms => Volatile.Read(ref _rms);
    public double Peak => Volatile.Read(ref _peak);
    public double MaximumRms => Volatile.Read(ref _maximumRms);
    public long AudioPackets => Interlocked.Read(ref _audioPackets);
    public long SentFrames => Interlocked.Read(ref _sentFrames);
    public long BlackFramesSent => Interlocked.Read(ref _blackFramesSent);
    public string AudioStatus => Volatile.Read(ref _audioStatus);
    public string AudioDevice => _capture.LastDeviceName;
    public string LightingStatus => _lamps.Status;
    public string DeviceSummary => _lamps.DeviceSummary;
    public bool HasLightingControl => _lamps.IsAvailable;
    public int ConnectedCount => _lamps.ConnectedCount;
    public double GainDb { get => Volatile.Read(ref _gainDb); set => Volatile.Write(ref _gainDb, Math.Clamp(value, -12, 24)); }
    public double Brightness { get => Volatile.Read(ref _brightness); set => Volatile.Write(ref _brightness, Math.Clamp(value, 0, 1)); }

    public SyncEngine()
    {
        _capture.SampleAvailable += sample =>
        {
            Volatile.Write(ref _rms, sample.Rms);
            Volatile.Write(ref _peak, sample.Peak);
            Interlocked.Exchange(ref _sampleTicks, sample.TimestampTicks);
            Interlocked.Increment(ref _audioPackets);
            if (sample.Rms > _maximumRms) Volatile.Write(ref _maximumRms, sample.Rms);
        };
        _capture.StatusChanged += status => Volatile.Write(ref _audioStatus, status);
    }

    public async Task StartAsync()
    {
        CancellationTokenSource cancellation;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancellation != null) return;
            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
        }
        _meter.Reset();
        Volatile.Write(ref _rms, 0);
        Volatile.Write(ref _peak, 0);
        Interlocked.Exchange(ref _sampleTicks, 0);
        try
        {
            await _lamps.StartAsync();
            lock (_lifecycleGate)
            {
                if (cancellation.IsCancellationRequested || _stopping || _disposed) return;
                _capture.Start();
                var token = cancellation.Token;
                _renderTask = Task.Run(() => RenderAsync(token));
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private async Task RenderAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33.333));
        var previous = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                var now = Stopwatch.GetTimestamp();
                var fresh = LevelMeter.IsFresh(Interlocked.Read(ref _sampleTicks), now);
                var level = _meter.Update(Rms, fresh, (now - previous) / (double)Stopwatch.Frequency, GainDb);
                previous = now;
                Volatile.Write(ref _level, level);
                FrameMapper.Fill(level, Brightness, _frame);
                if (_lamps.TrySendFrame(_frame))
                {
                    Interlocked.Increment(ref _sentFrames);
                    if (level == 0) Interlocked.Increment(ref _blackFramesSent);
                }
                // Sleeping at 8 Hz after silence saves wake-ups. Audio wakes naturally by next tick.
                var period = TimeSpan.FromMilliseconds(level == 0 ? 125 : 33.333);
                if (timer.Period != period) timer.Period = period;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_lifecycleGate)
        {
            cancellation = _cancellation;
            if (cancellation == null || _stopping) return;
            _stopping = true;
            cancellation.Cancel();
        }
        try
        {
            _capture.Stop();
            try { _renderTask?.GetAwaiter().GetResult(); }
            catch (Exception e) { Volatile.Write(ref _audioStatus, $"同步已停止（0x{e.HResult:X8}）。"); }
        }
        finally
        {
            _lamps.Stop();
            lock (_lifecycleGate)
            {
                _renderTask = null;
                _cancellation = null;
                cancellation.Dispose();
                _meter.Reset();
                Volatile.Write(ref _level, 0);
                Volatile.Write(ref _rms, 0);
                _stopping = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        _capture.Dispose();
        _lamps.Dispose();
    }
}
