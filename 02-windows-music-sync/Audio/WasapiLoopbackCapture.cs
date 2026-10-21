using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MusicSync.Audio;

/// <summary>
/// Captures only the default multimedia render endpoint's mixed playback levels.
/// Raw audio is measured in the WASAPI buffer and is never copied, saved, or uploaded.
/// </summary>
public sealed class WasapiLoopbackCapture : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Thread? _worker;
    private string _lastDeviceName = "Windows 預設播放裝置";
    private string? _lastStatus;
    private bool _disposed;

    public event Action<AudioSnapshot>? SampleAvailable;
    public event Action<string>? StatusChanged;

    public string LastDeviceName => Volatile.Read(ref _lastDeviceName);

    public bool IsRunning
    {
        get { lock (_gate) return _worker?.IsAlive == true; }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker?.IsAlive == true)
                return;

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var cancellation = _cancellation.Token;
            _lastStatus = null;
            _worker = new Thread(() => CaptureWorker(cancellation))
            {
                IsBackground = true,
                Name = "MusicSync WASAPI loopback"
            };
            _worker.SetApartmentState(ApartmentState.MTA);
            _worker.Start();
        }
    }

    public void Stop()
    {
        Thread? worker;
        lock (_gate)
        {
            _cancellation?.Cancel();
            worker = _worker;
        }

        // Native audio drivers can occasionally stall in a COM call. Never hold the UI indefinitely.
        if (worker is not null && worker != Thread.CurrentThread && worker.Join(3000))
        {
            lock (_gate)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    _cancellation?.Dispose();
                    _cancellation = null;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Stop();
        GC.SuppressFinalize(this);
    }

    private void CaptureWorker(CancellationToken cancellation)
    {
        var initialized = WasapiNative.CoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0)
        {
            PublishStatus($"無法初始化音訊執行緒（0x{initialized:X8}）。");
            return;
        }

        try
        {
            using var changed = new AutoResetEvent(false);
            var notification = new EndpointNotification(changed);
            IMMDeviceEnumerator? enumerator = null;
            var registered = false;
            try
            {
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(WasapiNative.EnumeratorClass, throwOnError: true)!)!;
                WasapiNative.Check(enumerator.RegisterEndpointNotificationCallback(notification));
                registered = true;

                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        CaptureEndpoint(enumerator, changed, cancellation);
                    }
                    catch (Exception exception) when (exception is COMException or NotSupportedException or InvalidCastException)
                    {
                        PublishSample(0, 0);
                        PublishStatus($"等候可用的播放裝置（0x{exception.HResult:X8}）。");
                        WaitHandle.WaitAny(new[] { cancellation.WaitHandle, changed }, 1500);
                    }
                }
            }
            catch (Exception exception)
            {
                PublishStatus($"音訊擷取已停止（0x{exception.HResult:X8}）。");
            }
            finally
            {
                if (registered && enumerator is not null)
                    enumerator.UnregisterEndpointNotificationCallback(notification);
                WasapiNative.Release(enumerator);
                GC.KeepAlive(notification);
                PublishSample(0, 0);
            }
        }
        finally
        {
            WasapiNative.CoUninitialize();
        }
    }

    private unsafe void CaptureEndpoint(IMMDeviceEnumerator enumerator, AutoResetEvent changed,
        CancellationToken cancellation)
    {
        IMMDevice? endpoint = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr formatPointer = IntPtr.Zero;
        using var ready = new AutoResetEvent(false);
        var started = false;
        try
        {
            // eRender=0, eMultimedia=1: follows the same normal playback output used by music/video apps.
            WasapiNative.Check(enumerator.GetDefaultAudioEndpoint(0, 1, out endpoint));
            Volatile.Write(ref _lastDeviceName, ReadFriendlyName(endpoint));
            var audioClientId = WasapiNative.AudioClientId;
            WasapiNative.Check(endpoint.Activate(ref audioClientId, 23, IntPtr.Zero, out var activated));
            audioClient = (IAudioClient)activated;
            WasapiNative.Check(audioClient.GetMixFormat(out formatPointer));
            var formatBytes = checked(18 + (ushort)Marshal.ReadInt16(formatPointer, 16));
            var format = WaveFormatInfo.Parse(new ReadOnlySpan<byte>((void*)formatPointer, formatBytes));
            WasapiNative.Check(audioClient.Initialize(0, WasapiNative.Loopback | WasapiNative.EventCallback,
                0, 0, formatPointer, IntPtr.Zero));
            WasapiNative.Check(audioClient.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            var captureClientId = WasapiNative.CaptureClientId;
            WasapiNative.Check(audioClient.GetService(ref captureClientId, out var service));
            captureClient = (IAudioCaptureClient)service;
            WasapiNative.Check(audioClient.Start());
            started = true;
            PublishStatus($"正在擷取電腦播放音訊：{LastDeviceName}");

            var waits = new[] { cancellation.WaitHandle, changed, ready };
            var lastPublish = Stopwatch.GetTimestamp();
            var lastPacket = lastPublish;
            var publishInterval = Stopwatch.Frequency / 30;
            var silenceInterval = Stopwatch.Frequency / 8;
            double squareSum = 0;
            double peak = 0;
            long sampleCount = 0;

            while (!cancellation.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(waits, 250);
                if (signaled == 0 || signaled == 1)
                    break;

                WasapiNative.Check(captureClient.GetNextPacketSize(out var packetFrames));
                while (packetFrames != 0 && !cancellation.IsCancellationRequested)
                {
                    WasapiNative.Check(captureClient.GetBuffer(out var data, out var frames, out var flags,
                        out _, out _));
                    if (frames == 0)
                        break;
                    try
                    {
                        lastPacket = Stopwatch.GetTimestamp();
                        if ((flags & WasapiNative.Silent) != 0 || data == IntPtr.Zero)
                        {
                            sampleCount += (long)frames * format.Channels;
                        }
                        else
                        {
                            var bytes = checked((int)frames * format.BlockAlign);
                            var statistics = PcmSampleDecoder.Analyze(
                                new ReadOnlySpan<byte>((void*)data, bytes), checked((int)frames), format);
                            squareSum += statistics.SquareSum;
                            sampleCount += statistics.SampleCount;
                            peak = Math.Max(peak, statistics.Peak);
                        }
                    }
                    finally
                    {
                        WasapiNative.Check(captureClient.ReleaseBuffer(frames));
                    }
                    WasapiNative.Check(captureClient.GetNextPacketSize(out packetFrames));
                }

                var now = Stopwatch.GetTimestamp();
                if (sampleCount > 0 && now - lastPublish >= publishInterval)
                {
                    PublishSample(Math.Sqrt(squareSum / sampleCount), peak);
                    squareSum = 0;
                    peak = 0;
                    sampleCount = 0;
                    lastPublish = now;
                }
                else if (now - lastPacket >= silenceInterval && now - lastPublish >= silenceInterval)
                {
                    // Some render endpoints stop raising events altogether when playback is paused.
                    PublishSample(0, 0);
                    squareSum = 0;
                    peak = 0;
                    sampleCount = 0;
                    lastPublish = now;
                }
            }
        }
        finally
        {
            if (started && audioClient is not null)
                audioClient.Stop();
            WasapiNative.Release(captureClient);
            WasapiNative.Release(audioClient);
            WasapiNative.Release(endpoint);
            if (formatPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(formatPointer);
        }
    }

    private static string ReadFriendlyName(IMMDevice endpoint)
    {
        IPropertyStore? store = null;
        var value = default(PropVariant);
        try
        {
            WasapiNative.Check(endpoint.OpenPropertyStore(0, out store));
            var key = WasapiNative.FriendlyNameKey;
            WasapiNative.Check(store.GetValue(ref key, out value));
            return value.Type == 31 && value.StringPointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.StringPointer) ?? "Windows 預設播放裝置"
                : "Windows 預設播放裝置";
        }
        catch (COMException)
        {
            return "Windows 預設播放裝置";
        }
        finally
        {
            WasapiNative.PropVariantClear(ref value);
            WasapiNative.Release(store);
        }
    }

    private void PublishSample(double rms, double peak) =>
        SampleAvailable?.Invoke(new AudioSnapshot(rms, peak, Stopwatch.GetTimestamp()));

    private void PublishStatus(string status)
    {
        if (_lastStatus == status)
            return;
        _lastStatus = status;
        StatusChanged?.Invoke(status);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class EndpointNotification : IMMNotificationClient
    {
        private readonly AutoResetEvent _changed;

        internal EndpointNotification(AutoResetEvent changed) => _changed = changed;

        private int Notify()
        {
            try { _changed.Set(); }
            catch (ObjectDisposedException) { }
            return 0;
        }

        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => flow == 0 && role == 1 ? Notify() : 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
}
