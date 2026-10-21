using System.Diagnostics;
using System.Collections.Concurrent;
using MusicSync.Audio;
using MusicSync.Lighting;

namespace MusicSync.Core;

/// <summary>Single render worker, reusable frame, no audio buffers or recording.</summary>
public sealed class SyncEngine : IDisposable
{
    private readonly WasapiLoopbackCapture _capture = new();
    private readonly LampArrayController _lamps = new();
    private readonly LevelMeter _meter = new();
    private readonly MusicEffectRenderer _effects = new();
    private EffectSettings _settings = new();
    private readonly object _previewGate = new();
    private readonly LedColor[] _previewFrame = new LedColor[LampCountLimits.Maximum];
    private readonly LedColor[] _frame = new LedColor[LampCountLimits.Maximum];
    private int _lampCount = LampCountLimits.Default;
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _renderTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private double _rms;
    private double _peak;
    private double _bass;
    private double _mid;
    private double _treble;
    private double _spectralPosition = 0.5;
    private double _tempo;
    private long _tempoTicks;
    private double _beat;
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
    private readonly ConcurrentQueue<string> _cleanupErrors = new();

    public bool IsRunning { get { lock (_lifecycleGate) return _cancellation != null && !_stopping; } }
    public bool IsStopping => Volatile.Read(ref _stopping);
    public bool IsDisposed => Volatile.Read(ref _disposed);
    public ShutdownState ShutdownSnapshot => new(IsDisposed, _capture.IsRunning,
        _renderTask is { IsCompleted: false }, _lamps.IsDiscoveryRunning,
        _cleanupErrors.Concat(_capture.CleanupErrors).Concat(_lamps.CleanupErrors).ToArray(),
        _stopTask is { IsCompleted: false } || _disposeTask is { IsCompleted: false });
    public double Level => Volatile.Read(ref _level);
    public double Rms => Volatile.Read(ref _rms);
    public double Peak => Volatile.Read(ref _peak);
    public double BassRms => Volatile.Read(ref _bass);
    public double MidRms => Volatile.Read(ref _mid);
    public double TrebleRms => Volatile.Read(ref _treble);
    public double TempoBpm
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            var tempoTicks = Interlocked.Read(ref _tempoTicks);
            return LevelMeter.IsFresh(Interlocked.Read(ref _sampleTicks), now) && tempoTicks > 0 &&
                (now - tempoTicks) / (double)Stopwatch.Frequency < 2 ? Volatile.Read(ref _tempo) : 0;
        }
    }
    public EffectSettings Settings { get => Volatile.Read(ref _settings); set => Volatile.Write(ref _settings, value ?? new()); }
    public int LampCount => Volatile.Read(ref _lampCount);
    public int ReportedLampCount => _lamps.ReportedLampCount;
    public int CopyPreview(Span<LedColor> destination)
    {
        lock (_previewGate)
        {
            var count = LampCount;
            _previewFrame.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }

    public void ConfigureLampCount(int count)
    {
        LampCountLimits.Validate(count);
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancellation != null || _stopping || _stopTask is { IsCompleted: false } ||
                _renderTask is { IsCompleted: false })
                throw new InvalidOperationException("請先停止同步，再修改燈數。");
            _lamps.ConfigureLampCount(count);
            lock (_previewGate)
            {
                Array.Clear(_previewFrame);
                Array.Clear(_frame);
                Volatile.Write(ref _lampCount, count);
            }
            _effects.Reset();
        }
    }
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
        _capture.SampleAvailable += OnAudioSample;
        _capture.StatusChanged += OnAudioStatusChanged;
    }

    private void OnAudioSample(AudioSnapshot sample)
    {
        Volatile.Write(ref _rms, sample.Rms);
        Volatile.Write(ref _peak, sample.Peak);
        Volatile.Write(ref _bass, sample.BassRms);
        Volatile.Write(ref _mid, sample.MidRms);
        Volatile.Write(ref _treble, sample.TrebleRms);
        Volatile.Write(ref _spectralPosition, sample.SpectralPosition);
        if (sample.TempoBpm > 0)
        {
            Volatile.Write(ref _tempo, sample.TempoBpm);
            Interlocked.Exchange(ref _tempoTicks, sample.TimestampTicks);
        }
        Volatile.Write(ref _beat, sample.BeatPulse);
        Interlocked.Exchange(ref _sampleTicks, sample.TimestampTicks);
        Interlocked.Increment(ref _audioPackets);
        if (sample.Rms > _maximumRms) Volatile.Write(ref _maximumRms, sample.Rms);
    }

    private void OnAudioStatusChanged(string status) => Volatile.Write(ref _audioStatus, status);

    public async Task StartAsync()
    {
        CancellationTokenSource cancellation;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping || _stopTask is { IsCompleted: false })
                throw new InvalidOperationException("前一次停止工作尚未完成，請稍候或關閉程式。");
            if (_renderTask is { IsCompleted: false } && _cancellation == null)
                throw new InvalidOperationException("前一次燈光工作尚未結束，請關閉後重新開啟程式。");
            if (_cancellation != null) return;
            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
        }
        _meter.Reset();
        _effects.Reset();
        Volatile.Write(ref _rms, 0);
        Volatile.Write(ref _peak, 0);
        ResetAudioFeatures();
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
                var elapsed = (now - previous) / (double)Stopwatch.Frequency;
                var level = _meter.Update(Rms, fresh, elapsed, GainDb);
                previous = now;
                Volatile.Write(ref _level, level);
                var bass = BassRms;
                var treble = TrebleRms;
                var balance = bass + treble > 0.00001 ? bass / (bass + treble) : 0.5;
                _effects.Fill(level, Volatile.Read(ref _spectralPosition), balance,
                    fresh ? Volatile.Read(ref _beat) : 0, fresh ? TempoBpm : 0,
                    elapsed, Brightness, Settings, _frame.AsSpan(0, LampCount));
                lock (_previewGate) _frame.AsSpan(0, LampCount).CopyTo(_previewFrame);
                if (_lamps.TrySendFrame(_frame.AsSpan(0, LampCount)))
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

    /// <summary>Requests one shared stop worker. No native operation runs on the caller's thread.</summary>
    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            if (_stopTask is { IsCompleted: false }) return _stopTask;
            var cancellation = _cancellation;
            if (cancellation == null) return Task.CompletedTask;
            _stopping = true;
            _stopTask = Task.Run(() => StopCore(cancellation));
            return _stopTask;
        }
    }

    /// <summary>Compatibility entry point; request only. Await StopAsync to observe completion.</summary>
    public void Stop() => _ = StopAsync();

    private void StopCore(CancellationTokenSource cancellation)
    {
        try
        {
            Cleanup(cancellation.Cancel, "RenderCancellation.Cancel");
            Cleanup(_capture.Stop, "Audio.Stop");
            Cleanup(() =>
            {
                if (_renderTask is not null && !_renderTask.Wait(1500))
                    _cleanupErrors.Enqueue("RenderWorker: stop exceeded 1500 ms; process exit releases remaining resources.");
            }, "Render.Stop");
        }
        finally
        {
            Cleanup(_lamps.Stop, "Lighting.Stop");
            lock (_lifecycleGate)
            {
                if (_renderTask?.IsCompleted != false) _renderTask = null;
                _cancellation = null;
                Cleanup(cancellation.Dispose, "RenderCancellation.Dispose");
                _meter.Reset();
                Volatile.Write(ref _level, 0);
                Volatile.Write(ref _rms, 0);
                Volatile.Write(ref _peak, 0);
                ResetAudioFeatures();
                lock (_previewGate) Array.Clear(_previewFrame);
                _stopping = false;
            }
        }
    }

    public void Dispose()
    {
        // A final Form.Dispose can run after a bounded asynchronous shutdown has
        // timed out. It must never re-enter or synchronously wait for native I/O.
        _ = DisposeAsync();
    }

    /// <summary>Requests one shared disposal worker; callers choose their bounded wait.</summary>
    public Task DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            if (_disposeTask is not null) return _disposeTask;
            _disposed = true;
            var stopTask = StopAsync();
            _disposeTask = Task.Run(() => DisposeCoreAsync(stopTask));
            return _disposeTask;
        }
    }

    private async Task DisposeCoreAsync(Task stopTask)
    {
        try { await stopTask.ConfigureAwait(false); }
        catch (Exception e) { _cleanupErrors.Enqueue($"Engine.Stop: {e.GetType().Name} 0x{e.HResult:X8}"); }
        finally
        {
            _capture.SampleAvailable -= OnAudioSample;
            _capture.StatusChanged -= OnAudioStatusChanged;
            Cleanup(_capture.Dispose, "Audio.Dispose");
            Cleanup(_lamps.Dispose, "Lighting.Dispose");
        }
    }

    private void Cleanup(Action action, string stage)
    {
        try { action(); }
        catch (Exception e) { _cleanupErrors.Enqueue($"{stage}: {e.GetType().Name} 0x{e.HResult:X8}"); }
    }

    private void ResetAudioFeatures()
    {
        Volatile.Write(ref _bass, 0); Volatile.Write(ref _mid, 0); Volatile.Write(ref _treble, 0);
        Volatile.Write(ref _spectralPosition, 0.5); Volatile.Write(ref _beat, 0); Volatile.Write(ref _tempo, 0);
        Interlocked.Exchange(ref _tempoTicks, 0);
    }

    /// <summary>Used only during app shutdown, after Dispose has requested cancellation.</summary>
    public async Task<bool> WaitForShutdownAsync(TimeSpan timeout)
    {
        var limit = Stopwatch.StartNew();
        do
        {
            if (WorkersStopped())
                return true;
            await Task.Delay(25).ConfigureAwait(false);
        } while (limit.Elapsed < timeout);
        return WorkersStopped();
    }

    private bool WorkersStopped() => !_capture.IsRunning && _renderTask?.IsCompleted != false &&
        !_lamps.IsDiscoveryRunning && _stopTask?.IsCompleted != false && _disposeTask?.IsCompleted != false;
}

public sealed record ShutdownState(bool IsDisposed, bool AudioWorkerRunning,
    bool RenderWorkerRunning, bool LightingDiscoveryRunning, string[] CleanupErrors,
    bool CleanupWorkerRunning = false);
