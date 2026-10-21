using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Foundation.Metadata;
using LampColor = Windows.UI.Color;

namespace MusicSync.Lighting;

/// <summary>
/// Controls only this project's unique eight-lamp device through Windows lighting arbitration.
/// Events may be raised on a worker thread. This class never changes global lighting settings,
/// IsEnabled, or BrightnessLevel, and never opens a raw HID handle.
/// </summary>
public sealed class LampArrayController : IDisposable
{
    public const string TargetName = "ESP32-S3 USB Light Strip";
    public const int TargetLampCount = 8;
    private const int TargetVendorId = 0x303A;
    private const int TargetProductId = 0x1001;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.Ordinal);
    private readonly LampColor[] _colors = new LampColor[TargetLampCount];
    private readonly LedColor[] _lastColors = new LedColor[TargetLampCount];
    private readonly int[] _indices = { 0, 1, 2, 3, 4, 5, 6, 7 };
    private readonly bool _supportsControlAvailability;
    private DeviceWatcher? _watcher;
    private System.Threading.Timer? _refreshTimer;
    private TaskCompletionSource<bool>? _enumerationFinished;
    private LampArray? _lamp;
    private string? _selectedId;
    private string _status = "尚未開始";
    private string _deviceSummary = "尚未搜尋燈條";
    private bool _started;
    private bool _disposed;
    private bool _enumerated;
    private bool _available;
    private bool _forceSend = true;
    private bool _availabilitySubscribed;
    private int _connectedCount;
    private int _generation;
    private long _lastSentTicks;
    private long _minimumIntervalTicks;

    public LampArrayController()
    {
        uint length = 0;
        HasPackageIdentity = GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700;
        _supportsControlAvailability =
            ApiInformation.IsPropertyPresent("Windows.Devices.Lights.LampArray", "IsAvailable") &&
            ApiInformation.IsEventPresent("Windows.Devices.Lights.LampArray", "AvailabilityChanged");
    }

    public event Action<string>? StatusChanged;

    public bool HasPackageIdentity { get; }
    // SDK metadata marks these APIs as 26100. Runtime feature detection also covers earlier
    // Windows builds to which Microsoft has delivered the dynamic-lighting APIs.
    [SupportedOSPlatformGuard("windows10.0.26100.0")]
    public bool SupportsControlAvailability => _supportsControlAvailability;
    public string Status { get { lock (_gate) return _status; } }
    public bool IsAvailable { get { lock (_gate) return _available; } }
    public int ConnectedCount { get { lock (_gate) return _connectedCount; } }
    public string DeviceSummary { get { lock (_gate) return _deviceSummary; } }

    public async Task StartAsync()
    {
        Task enumeration;
        int generation;
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started) return;
                if (!SupportsControlAvailability)
                    throw new PlatformNotSupportedException();
                _started = true;
                _enumerated = false;
                generation = ++_generation;
                _devices.Clear();
                _enumerationFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                enumeration = _enumerationFinished.Task;
                _refreshTimer = new System.Threading.Timer(OnRefreshTimer, generation, Timeout.Infinite, Timeout.Infinite);
                _watcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
                _watcher.Added += OnAdded;
                _watcher.Updated += OnUpdated;
                _watcher.Removed += OnRemoved;
                _watcher.EnumerationCompleted += OnEnumerationCompleted;
                _watcher.Start();
            }
        }
        catch (ObjectDisposedException) { throw; }
        catch (PlatformNotSupportedException)
        {
            Stop();
            PublishStatus("此 Windows 版本缺少 LampArray 控制權 API；請更新 Windows 11 後重試。為避免誤控，已停止送色。");
            return;
        }
        catch (Exception ex)
        {
            Stop();
            PublishStatus($"無法啟動燈條搜尋（0x{ex.HResult:X8}）。");
            return;
        }

        PublishStatus("正在搜尋 ESP32-S3 的 8 顆燈條…");
        try
        {
            await enumeration.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await RefreshDevicesAsync(generation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
                PublishStatus($"燈條搜尋失敗（0x{ex.HResult:X8}），可停止後重試。");
        }
    }

    /// <summary>
    /// Returns true only when a frame was submitted to Windows. Identical frames, unavailable
    /// devices, and calls faster than MinUpdateInterval are skipped without allocating a frame.
    /// A successful call is not proof of physical light output.
    /// </summary>
    public bool TrySendFrame(ReadOnlySpan<LedColor> colors)
    {
        if (colors.Length != TargetLampCount)
            throw new ArgumentException("每個畫面必須包含 8 顆燈。", nameof(colors));

        string? changedStatus = null;
        bool submitted = false;
        bool refresh = false;
        lock (_gate)
        {
            if (!_started || _lamp is null) return false;
            try
            {
                string status = ReadControlStatus(_lamp, out bool canSend);
                _available = canSend;
                if (!canSend)
                {
                    _forceSend = true;
                    if (_status != status) _status = changedStatus = status;
                }
                else
                {
                    if (_status != status) _status = changedStatus = status;
                    long now = Stopwatch.GetTimestamp();
                    bool due = _lastSentTicks == 0 || now - _lastSentTicks >= _minimumIntervalTicks;
                    if (due && (_forceSend || !colors.SequenceEqual(_lastColors)))
                    {
                        for (int i = 0; i < TargetLampCount; i++)
                        {
                            LedColor color = colors[i];
                            _colors[i] = LampColor.FromArgb(255, color.R, color.G, color.B);
                        }
                        _lamp.SetColorsForIndices(_colors, _indices);
                        colors.CopyTo(_lastColors);
                        _lastSentTicks = now;
                        _forceSend = false;
                        submitted = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _available = false;
                _forceSend = true;
                string status = $"燈條暫時無法更新（0x{ex.HResult:X8}），正在重新搜尋。";
                if (_status != status) _status = changedStatus = status;
                refresh = true;
            }
        }
        if (changedStatus is not null) StatusChanged?.Invoke(changedStatus);
        if (refresh) QueueRefresh();
        return submitted;
    }

    public void Stop()
    {
        DeviceWatcher? watcher;
        System.Threading.Timer? timer;
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            ++_generation;
            _enumerationFinished?.TrySetCanceled();
            _enumerationFinished = null;
            watcher = _watcher;
            _watcher = null;
            timer = _refreshTimer;
            _refreshTimer = null;
            if (_lamp is not null)
            {
                try
                {
                    if (SupportsControlAvailability && _lamp.IsConnected && _lamp.IsEnabled && _lamp.IsAvailable)
                    {
                        for (int i = 0; i < _colors.Length; i++) _colors[i] = LampColor.FromArgb(255, 0, 0, 0);
                        _lamp.SetColorsForIndices(_colors, _indices);
                    }
                }
                catch (Exception) { /* Shutdown must also work after device removal or lost access. */ }
            }
            ReleaseSelectedLampLocked();
            _devices.Clear();
            _connectedCount = 0;
            _deviceSummary = "已停止控制燈條";
            _enumerated = false;
        }
        timer?.Dispose();
        if (watcher is not null)
        {
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            watcher.Removed -= OnRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch (Exception) { }
        }
        PublishStatus("已停止；Windows 可能恢復原先設定的燈效。");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        // A pending WinRT discovery can still be completing. Leave this small managed semaphore
        // alive rather than disposing it under an in-flight callback.
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        lock (_gate)
        {
            if (!_started || sender != _watcher) return;
            _devices[device.Id] = device;
        }
        QueueRefresh();
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_started || sender != _watcher) return;
            if (_devices.TryGetValue(update.Id, out DeviceInformation? device)) device.Update(update);
        }
        QueueRefresh();
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_started || sender != _watcher) return;
            _devices.Remove(update.Id);
            if (_selectedId == update.Id) ReleaseSelectedLampLocked();
        }
        QueueRefresh();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (!_started || sender != _watcher) return;
            _enumerated = true;
            _enumerationFinished?.TrySetResult(true);
        }
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        lock (_gate)
        {
            if (_started && _enumerated) _refreshTimer?.Change(150, Timeout.Infinite);
        }
    }

    private async void OnRefreshTimer(object? state)
    {
        int generation = (int)state!;
        try { await RefreshDevicesAsync(generation).ConfigureAwait(false); }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
                PublishStatus($"重新搜尋燈條失敗（0x{ex.HResult:X8}）。");
        }
    }

    private async Task RefreshDevicesAsync(int generation)
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            DeviceInformation[] candidates;
            LampArray? selected;
            string? selectedId;
            lock (_gate)
            {
                if (!_started || generation != _generation) return;
                candidates = _devices.Values.Where(d => string.Equals(d.Name, TargetName, StringComparison.OrdinalIgnoreCase)).ToArray();
                selected = _lamp;
                selectedId = _selectedId;
            }

            LampArray? uniqueLamp = null;
            string? uniqueId = null;
            int matched = 0;
            bool couldNotInspect = false;
            foreach (DeviceInformation candidate in candidates)
            {
                LampArray? candidateLamp;
                try
                {
                    candidateLamp = candidate.Id == selectedId ? selected : await LampArray.FromIdAsync(candidate.Id);
                    if (candidateLamp is null)
                    {
                        couldNotInspect = true;
                        continue;
                    }
                    if (candidateLamp.HardwareVendorId != TargetVendorId ||
                        candidateLamp.HardwareProductId != TargetProductId ||
                        candidateLamp.LampCount != TargetLampCount || !candidateLamp.IsConnected)
                        continue;
                }
                catch (Exception)
                {
                    couldNotInspect = true;
                    continue;
                }
                matched++;
                uniqueLamp = candidateLamp;
                uniqueId = candidate.Id;
            }

            string status;
            lock (_gate)
            {
                if (!_started || generation != _generation) return;
                _connectedCount = matched;
                // Do not select a device if another same-name candidate could not be inspected:
                // its identity is unknown and it might be a second matching prototype.
                if (matched != 1 || couldNotInspect)
                {
                    ReleaseSelectedLampLocked();
                    _deviceSummary = matched > 1 ? $"偵測到 {matched} 個符合的 8 燈裝置" : "未連接唯一可確認的 8 燈裝置";
                    status = matched > 1
                        ? "請只連接一個 ESP32-S3 8 燈裝置，避免控制錯誤燈條。"
                        : couldNotInspect
                            ? "無法確認全部同名裝置；請檢查連線與 Windows 動態光效設定。"
                            : "找不到 ESP32-S3 的 8 顆燈條，請檢查 USB 資料線。";
                }
                else
                {
                    if (_lamp != uniqueLamp)
                    {
                        ReleaseSelectedLampLocked();
                        LampArray activeLamp = uniqueLamp!;
                        _lamp = activeLamp;
                        _selectedId = uniqueId;
                        if (SupportsControlAvailability)
                        {
                            activeLamp.AvailabilityChanged += OnAvailabilityChanged;
                            _availabilitySubscribed = true;
                        }
                        _minimumIntervalTicks = Math.Max(1, (long)Math.Ceiling(activeLamp.MinUpdateInterval.TotalSeconds * Stopwatch.Frequency));
                        _lastSentTicks = 0;
                        _forceSend = true;
                    }
                    _deviceSummary = $"{TargetName} · 8 顆 · VID 303A / PID 1001";
                    status = ReadControlStatus(_lamp!, out _available);
                }
                _status = status;
            }
            StatusChanged?.Invoke(status);
        }
        finally { _refreshGate.Release(); }
    }

    private void OnAvailabilityChanged(LampArray sender, object args)
    {
        string? status = null;
        lock (_gate)
        {
            if (!_started || sender != _lamp) return;
            _forceSend = true;
            _lastSentTicks = 0;
            try { _status = status = ReadControlStatus(sender, out _available); }
            catch (Exception)
            {
                _available = false;
                _status = status = "燈條控制權已改變，正在重新確認。";
            }
        }
        if (status is not null) StatusChanged?.Invoke(status);
    }

    private string ReadControlStatus(LampArray lamp, out bool canSend)
    {
        canSend = false;
        if (!SupportsControlAvailability)
            return "此 Windows 版本缺少 LampArray 控制權 API，已停止送色；請更新 Windows 11。";
        if (!lamp.IsConnected) return "燈條已斷線，等待重新連接。";
        if (!lamp.IsEnabled) return "請在 Windows「動態光效」開啟這個裝置。";
        if (!lamp.IsAvailable)
            return HasPackageIdentity
                ? "等待燈光控制權；請在「背景光源控制項」將此 App 排到前面。"
                : "開發版尚無套件身分；請保持視窗在前景，背景同步請安裝 MSIX。";
        canSend = true;
        return HasPackageIdentity
            ? "已取得 8 顆燈的控制權。"
            : "已取得前景控制權；背景同步請使用 MSIX 安裝版。";
    }

    private void ReleaseSelectedLampLocked()
    {
        if (_lamp is not null && _availabilitySubscribed && SupportsControlAvailability)
        {
            try { _lamp.AvailabilityChanged -= OnAvailabilityChanged; }
            catch (Exception) { }
        }
        // LampArray has no public Close/Dispose API. Detach events and release the projection;
        // the Windows runtime releases its reference, and the OS retains lighting arbitration.
        _lamp = null;
        _availabilitySubscribed = false;
        _selectedId = null;
        _available = false;
        _forceSend = true;
        _lastSentTicks = 0;
    }

    private void PublishStatus(string status)
    {
        lock (_gate)
        {
            if (_status == status) return;
            _status = status;
        }
        StatusChanged?.Invoke(status);
    }

    private bool IsCurrentGeneration(int generation)
    {
        lock (_gate) return _started && generation == _generation;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
