using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Foundation.Metadata;
using LampColor = Windows.UI.Color;

namespace MusicSync.Lighting;

/// <summary>
/// Controls this project's unique device with a matching configured lamp count.
/// The state gate protects managed fields only: no Windows lighting calls or event accessors
/// may run while it is held, because those calls can synchronously dispatch native callbacks.
/// </summary>
public sealed class LampArrayController : IDisposable
{
    public const string TargetName = "ESP32-S3 USB Light Strip";
    private const int TargetVendorId = 0x303A;
    private const int TargetProductId = 0x1001;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<string, string> _devices = new(StringComparer.Ordinal);
    private FrameBuffers _buffers = new(LampCountLimits.Default);
    private int _expectedLampCount = LampCountLimits.Default;
    private int _reportedLampCount;
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
    private int _sendInProgress;
    private int _refreshQueued;
    private int _controlRevision;
    private int _deviceRevision;
    private long _lastSentTicks;
    private long _minimumIntervalTicks;
    private CancellationTokenSource? _discoveryCancellation;
    private readonly List<CancellationTokenSource> _retiredCancellations = new();
    private readonly ConcurrentQueue<string> _cleanupErrors = new();
    private int _refreshOperations;
    private int _stopOperations;
    private bool _refreshGateDisposed;

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
    [SupportedOSPlatformGuard("windows10.0.26100.0")]
    public bool SupportsControlAvailability => _supportsControlAvailability;
    public string Status => Volatile.Read(ref _status);
    public bool IsAvailable => Volatile.Read(ref _available);
    public int ConnectedCount => Volatile.Read(ref _connectedCount);
    public int ExpectedLampCount => Volatile.Read(ref _expectedLampCount);
    public int ReportedLampCount => Volatile.Read(ref _reportedLampCount);
    public string DeviceSummary => Volatile.Read(ref _deviceSummary);
    public bool IsDiscoveryRunning => Volatile.Read(ref _started) ||
        Volatile.Read(ref _refreshOperations) > 0 || Volatile.Read(ref _sendInProgress) != 0 ||
        Volatile.Read(ref _stopOperations) > 0;
    public string[] CleanupErrors => _cleanupErrors.ToArray();

    /// <summary>Changes exact-size buffers only after all work from the previous run has stopped.</summary>
    public void ConfigureLampCount(int count)
    {
        LampCountLimits.Validate(count);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started || _refreshOperations != 0 || _sendInProgress != 0 || _stopOperations != 0)
                throw new InvalidOperationException("請先停止同步並等待清理完成，再修改燈數。");
            if (_expectedLampCount == count) return;
            _buffers = new FrameBuffers(count);
            _expectedLampCount = count;
            _reportedLampCount = 0;
            _deviceSummary = "尚未搜尋燈條";
            _status = "尚未開始";
            _forceSend = true;
            _lastSentTicks = 0;
            ++_generation;
        }
    }

    public async Task StartAsync()
    {
        Task enumeration;
        int generation;
        CancellationToken cancellation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            if (_stopOperations != 0 || _sendInProgress != 0)
                throw new InvalidOperationException("前一次燈光工作尚未清理完成，請稍候再開始。");
            if (!SupportsControlAvailability)
            {
                PublishUnsupportedStatus();
                return;
            }
            _started = true;
            _enumerated = false;
            _reportedLampCount = 0;
            generation = ++_generation;
            _discoveryCancellation = new CancellationTokenSource();
            cancellation = _discoveryCancellation.Token;
            _devices.Clear();
            _enumerationFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            enumeration = _enumerationFinished.Task;
            _refreshQueued = 0;
            _refreshTimer = new System.Threading.Timer(OnRefreshTimer, generation, Timeout.Infinite, Timeout.Infinite);
            // Keep the cancellation source alive until watcher setup and the startup await finish.
            Interlocked.Increment(ref _refreshOperations);
        }

        DeviceWatcher? setupWatcher = null;
        try
        {
            setupWatcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
            setupWatcher.Added += OnAdded;
            setupWatcher.Updated += OnUpdated;
            setupWatcher.Removed += OnRemoved;
            setupWatcher.EnumerationCompleted += OnEnumerationCompleted;
            bool current;
            lock (_gate)
            {
                current = _started && generation == _generation && !_disposed;
                if (current) _watcher = setupWatcher;
            }
            if (!current)
            {
                ReleaseWatcher(setupWatcher);
                return;
            }
            setupWatcher.Start();
            if (!IsCurrentGeneration(generation))
            {
                // Stop may have raced the native Start call after the managed watcher was detached.
                ReleaseWatcher(setupWatcher);
                return;
            }
            PublishStatus($"正在搜尋 ESP32-S3 的 {ExpectedLampCount} 顆燈條…");
            await enumeration.WaitAsync(TimeSpan.FromSeconds(8), cancellation).ConfigureAwait(false);
            await RefreshDevicesAsync(generation, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
            {
                Stop();
                PublishStatus($"燈條搜尋失敗（0x{ex.HResult:X8}），可停止後重試。");
            }
            // Also covers a partial native event-subscription setup that failed before the
            // watcher reference was committed to managed state. Removing twice is harmless.
            if (setupWatcher is not null) ReleaseWatcher(setupWatcher);
        }
        finally
        {
            Interlocked.Decrement(ref _refreshOperations);
            TryDisposeCoordination();
        }
    }

    // Called only for the runtime feature check; no callbacks or native objects are involved.
    private void PublishUnsupportedStatus() =>
        Volatile.Write(ref _status, "此 Windows 版本缺少 LampArray 控制權 API；請更新 Windows 11 後重試。為避免誤控，已停止送色。");

    /// <summary>Submits a frame without holding the managed state gate across native I/O.</summary>
    public bool TrySendFrame(ReadOnlySpan<LedColor> colors)
    {
        if (colors.Length != ExpectedLampCount)
            throw new ArgumentException($"每個畫面必須包含設定的 {ExpectedLampCount} 顆燈。", nameof(colors));
        // One render worker normally calls this method. A concurrent call skips rather than
        // waiting; the reusable native colour buffer remains owned until its call returns.
        if (Interlocked.CompareExchange(ref _sendInProgress, 1, 0) != 0) return false;
        LampArray? lamp = null;
        FrameBuffers? buffers = null;
        int generation = 0;
        string? changedStatus = null;
        bool refresh = false;
        bool submitted = false;
        try
        {
            long now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                if (!_started || _lamp is null) return false;
                lamp = _lamp;
                buffers = _buffers;
                // A stopped configuration/start racing a caller's earlier size check must not
                // submit an old-length frame against the new native buffer.
                if (colors.Length != buffers.Count) return false;
                generation = _generation;
                bool due = _lastSentTicks == 0 || now - _lastSentTicks >= _minimumIntervalTicks;
                if (!due || (!_forceSend && colors.SequenceEqual(buffers.LastColors))) return false;
            }

            int controlRevision = Volatile.Read(ref _controlRevision);
            string status = ReadControlStatus(lamp, buffers.GrantedControlStatus, out bool canSend);
            lock (_gate)
            {
                if (!IsSelectedLocked(lamp, generation)) return false;
                _available = canSend;
                if (_status != status) _status = changedStatus = status;
                if (!canSend) _forceSend = true;
            }
            if (!canSend) return false;

            for (int i = 0; i < buffers.Count; i++)
            {
                LedColor color = colors[i];
                buffers.Colors[i] = LampColor.FromArgb(255, color.R, color.G, color.B);
            }
            // A control callback can arrive inline here. It must not wait on _gate or perform
            // another native lighting operation; OnAvailabilityChanged only queues a refresh.
            lamp.SetColorsForIndices(buffers.Colors, buffers.Indices);
            submitted = true;
            lock (_gate)
            {
                if (IsSelectedLocked(lamp, generation))
                {
                    colors.CopyTo(buffers.LastColors);
                    _lastSentTicks = now;
                    _forceSend = Volatile.Read(ref _controlRevision) != controlRevision;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (lamp is not null && IsSelectedLocked(lamp, generation))
                {
                    _available = false;
                    _forceSend = true;
                    string status = $"燈條暫時無法更新（0x{ex.HResult:X8}），正在重新搜尋。";
                    if (_status != status) _status = changedStatus = status;
                    refresh = true;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _sendInProgress, 0);
            if (changedStatus is not null) StatusChanged?.Invoke(changedStatus);
            if (refresh) QueueRefresh();
        }
        return submitted;
    }

    public void Stop()
    {
        DeviceWatcher? watcher;
        System.Threading.Timer? timer;
        CancellationTokenSource? cancellation;
        SelectedLamp released;
        FrameBuffers buffers;
        int stoppedGeneration;
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            ++_stopOperations;
            stoppedGeneration = ++_generation;
            _enumerationFinished?.TrySetCanceled();
            _enumerationFinished = null;
            watcher = _watcher;
            _watcher = null;
            timer = _refreshTimer;
            _refreshTimer = null;
            _refreshQueued = 0;
            cancellation = _discoveryCancellation;
            _discoveryCancellation = null;
            released = DetachSelectedLampLocked();
            buffers = _buffers;
            _devices.Clear();
            _connectedCount = 0;
            _reportedLampCount = 0;
            _deviceSummary = "已停止控制燈條";
            _enumerated = false;
        }
        try
        {
            if (cancellation is not null)
            {
                Cleanup(cancellation.Cancel, "Discovery.Cancel");
                // Retire only after Cancel returns; a finishing refresh must not dispose the
                // source while this thread is about to request cancellation.
                lock (_gate) _retiredCancellations.Add(cancellation);
            }
            if (timer is not null) Cleanup(timer.Dispose, "RefreshTimer.Dispose");
            // Native calls are deliberately outside _gate. A black frame uses its own buffer,
            // and skips if a render call still owns the old projection; it never waits for it.
            if (released.Lamp is not null && Interlocked.CompareExchange(ref _sendInProgress, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        ReadControlStatus(released.Lamp, buffers.GrantedControlStatus, out bool canSend);
                        if (canSend)
                        {
                            var black = new LampColor[buffers.Count];
                            for (int i = 0; i < black.Length; i++) black[i] = LampColor.FromArgb(255, 0, 0, 0);
                            released.Lamp.SetColorsForIndices(black, buffers.Indices);
                        }
                    }
                    catch (Exception) { /* Device removal or lost arbitration also permits shutdown. */ }
                }
                finally { Volatile.Write(ref _sendInProgress, 0); }
            }
            ReleaseLampSubscription(released);
            if (watcher is not null) ReleaseWatcher(watcher);
            bool stillStopped;
            lock (_gate) stillStopped = !_started && _generation == stoppedGeneration;
            if (stillStopped) PublishStatus("已停止；Windows 可能恢復原先設定的燈效。");
        }
        finally
        {
            Interlocked.Decrement(ref _stopOperations);
            TryDisposeCoordination();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { Stop(); }
        finally
        {
            StatusChanged = null;
            TryDisposeCoordination();
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        // DeviceInformation itself is a WinRT projection: copy its fields before entering _gate.
        string id = device.Id;
        string name = device.Name;
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _watcher)) return;
            _devices[id] = name;
            ++_deviceRevision;
        }
        QueueRefresh();
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        // The HID product name/identity is stable during this enumeration. Re-open and inspect
        // LampArray after property changes rather than mutating a WinRT object under the gate.
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _watcher)) return;
            ++_deviceRevision;
        }
        QueueRefresh();
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        string id = update.Id;
        SelectedLamp released = default;
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _watcher)) return;
            _devices.Remove(id);
            ++_deviceRevision;
            if (_selectedId == id)
            {
                released = DetachSelectedLampLocked();
                if (released.Lamp is not null) Interlocked.Increment(ref _refreshOperations);
            }
        }
        // Watcher callbacks also return without performing native unsubscribe work.
        if (released.Lamp is not null)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ReleaseLampSubscription(released); }
                finally
                {
                    Interlocked.Decrement(ref _refreshOperations);
                    TryDisposeCoordination();
                }
            });
        QueueRefresh();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _watcher)) return;
            _enumerated = true;
            _enumerationFinished?.TrySetResult(true);
        }
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!Volatile.Read(ref _started) || !Volatile.Read(ref _enumerated)) return;
        if (Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
        var timer = Volatile.Read(ref _refreshTimer);
        try
        {
            if (timer is null || !timer.Change(150, Timeout.Infinite))
                Volatile.Write(ref _refreshQueued, 0);
        }
        catch (ObjectDisposedException) { Volatile.Write(ref _refreshQueued, 0); }
    }

    private async void OnRefreshTimer(object? state)
    {
        int generation = (int)state!;
        CancellationToken cancellation;
        lock (_gate)
        {
            if (!_started || _disposed || generation != _generation || _discoveryCancellation is null) return;
            Volatile.Write(ref _refreshQueued, 0);
            cancellation = _discoveryCancellation.Token;
        }
        try { await RefreshDevicesAsync(generation, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
                PublishStatus($"重新搜尋燈條失敗（0x{ex.HResult:X8}）。");
        }
    }

    private async Task RefreshDevicesAsync(int generation, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (!_started || _disposed || generation != _generation) return;
            Interlocked.Increment(ref _refreshOperations);
        }
        bool acquired = false;
        bool refreshAgain = false;
        SelectedLamp retired = default;
        SelectedLamp uncommitted = default;
        try
        {
            await _refreshGate.WaitAsync(cancellation).ConfigureAwait(false);
            acquired = true;
            KeyValuePair<string, string>[] candidates;
            LampArray? selected;
            string? selectedId;
            bool selectedSubscribed;
            int deviceRevision;
            int expectedCount;
            string grantedControlStatus;
            lock (_gate)
            {
                if (!_started || generation != _generation) return;
                candidates = _devices.Where(d => string.Equals(d.Value, TargetName, StringComparison.OrdinalIgnoreCase)).ToArray();
                selected = _lamp;
                selectedId = _selectedId;
                selectedSubscribed = _availabilitySubscribed;
                deviceRevision = _deviceRevision;
                expectedCount = _expectedLampCount;
                grantedControlStatus = _buffers.GrantedControlStatus;
            }

            LampArray? uniqueLamp = null;
            string? uniqueId = null;
            int matched = 0;
            int reportedCount = 0;
            bool couldNotInspect = false;
            foreach (var candidate in candidates)
            {
                LampArray? candidateLamp;
                try
                {
                    candidateLamp = candidate.Key == selectedId ? selected :
                        await LampArray.FromIdAsync(candidate.Key).AsTask(cancellation).ConfigureAwait(false);
                    if (candidateLamp is null)
                    {
                        couldNotInspect = true;
                        continue;
                    }
                    if (candidateLamp.HardwareVendorId != TargetVendorId ||
                        candidateLamp.HardwareProductId != TargetProductId ||
                        !candidateLamp.IsConnected)
                        continue;
                    reportedCount = candidateLamp.LampCount;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    couldNotInspect = true;
                    continue;
                }
                matched++;
                uniqueLamp = candidateLamp;
                uniqueId = candidate.Key;
            }

            string status;
            bool available = false;
            long minimumTicks = 1;
            bool newSubscription = false;
            bool selectUnique = matched == 1 && !couldNotInspect && reportedCount == expectedCount;
            if (selectUnique)
            {
                // All native property and event operations finish before committing managed state.
                minimumTicks = Math.Max(1, (long)Math.Ceiling(uniqueLamp!.MinUpdateInterval.TotalSeconds * Stopwatch.Frequency));
                status = ReadControlStatus(uniqueLamp, grantedControlStatus, out available);
                if (!ReferenceEquals(uniqueLamp, selected) || !selectedSubscribed)
                {
                    if (SupportsControlAvailability)
                    {
                        uniqueLamp.AvailabilityChanged += OnAvailabilityChanged;
                        newSubscription = true;
                        uncommitted = new SelectedLamp(uniqueLamp, true);
                    }
                }
            }
            else
            {
                status = matched > 1
                    ? "請只連接一個 ESP32-S3 燈光裝置，避免控制錯誤燈條。"
                    : couldNotInspect
                        ? "無法確認全部同名裝置；請檢查連線與 Windows 動態光效設定。"
                        : matched == 1
                            ? $"設定 {expectedCount} 顆、裝置回報 {reportedCount} 顆，請確認燈數與韌體；目前不送色。"
                            : "找不到 ESP32-S3 燈光裝置，請檢查 USB 資料線。";
            }

            lock (_gate)
            {
                if (!_started || _disposed || generation != _generation) return;
                // A remove/re-add of the same HID ID must not reinstall the retired
                // projection or reuse a subscription that removal has already detached.
                if (deviceRevision != _deviceRevision)
                {
                    refreshAgain = true;
                    return;
                }
                // An unplug that occurred during native inspection invalidates the candidate.
                if (selectUnique && (uniqueId is null || !_devices.ContainsKey(uniqueId))) return;
                _connectedCount = matched;
                _reportedLampCount = matched == 1 && !couldNotInspect ? reportedCount : 0;
                if (!selectUnique)
                {
                    retired = DetachSelectedLampLocked();
                    _deviceSummary = matched > 1 ? $"偵測到 {matched} 個符合的 ESP32-S3 裝置" :
                        matched == 1 && !couldNotInspect
                            ? $"{TargetName} · 設定 {expectedCount} 顆／裝置 {reportedCount} 顆"
                            : "未連接唯一可確認的 ESP32-S3 燈光裝置";
                }
                else
                {
                    if (!ReferenceEquals(_lamp, uniqueLamp))
                    {
                        retired = DetachSelectedLampLocked();
                        _lamp = uniqueLamp;
                        _selectedId = uniqueId;
                        _availabilitySubscribed = newSubscription;
                        _lastSentTicks = 0;
                        _forceSend = true;
                    }
                    else if (newSubscription) _availabilitySubscribed = true;
                    _minimumIntervalTicks = minimumTicks;
                    _available = available;
                    _deviceSummary = $"{TargetName} · {reportedCount} 顆 · VID 303A / PID 1001";
                    uncommitted = default;
                }
                _status = status;
            }
            StatusChanged?.Invoke(status);
        }
        finally
        {
            ReleaseLampSubscription(retired);
            ReleaseLampSubscription(uncommitted);
            if (acquired) _refreshGate.Release();
            Interlocked.Decrement(ref _refreshOperations);
            TryDisposeCoordination();
            if (refreshAgain) QueueRefresh();
        }
    }

    private void OnAvailabilityChanged(LampArray sender, object args)
    {
        // This callback may run synchronously inside SetColors/native property access. It must
        // neither acquire a managed gate nor call LampArray, subscribe, or notify UI listeners.
        if (!Volatile.Read(ref _started) || !ReferenceEquals(sender, Volatile.Read(ref _lamp))) return;
        Interlocked.Increment(ref _controlRevision);
        Volatile.Write(ref _forceSend, true);
        Interlocked.Exchange(ref _lastSentTicks, 0);
        Volatile.Write(ref _available, false);
        QueueRefresh();
    }

    private string ReadControlStatus(LampArray lamp, string grantedControlStatus, out bool canSend)
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
            ? grantedControlStatus
            : "已取得前景控制權；背景同步請使用 MSIX 安裝版。";
    }

    // Only managed state mutation; callers release the returned native subscription outside _gate.
    private SelectedLamp DetachSelectedLampLocked()
    {
        var released = new SelectedLamp(_lamp, _availabilitySubscribed);
        _lamp = null;
        _availabilitySubscribed = false;
        _selectedId = null;
        _available = false;
        _forceSend = true;
        _lastSentTicks = 0;
        return released;
    }

    private void ReleaseLampSubscription(SelectedLamp released)
    {
        if (released.Lamp is not null && released.Subscribed && SupportsControlAvailability)
            Cleanup(() => released.Lamp.AvailabilityChanged -= OnAvailabilityChanged, "Lamp.AvailabilityChanged");
        // LampArray has no public Close/Dispose API. Once callbacks and workers release their
        // projection references the runtime/OS owns the remaining lighting arbitration.
    }

    private void ReleaseWatcher(DeviceWatcher watcher)
    {
        Cleanup(() => watcher.Added -= OnAdded, "Watcher.Added");
        Cleanup(() => watcher.Updated -= OnUpdated, "Watcher.Updated");
        Cleanup(() => watcher.Removed -= OnRemoved, "Watcher.Removed");
        Cleanup(() => watcher.EnumerationCompleted -= OnEnumerationCompleted, "Watcher.EnumerationCompleted");
        Cleanup(() =>
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }, "Watcher.Stop");
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
        lock (_gate) return _started && !_disposed && generation == _generation;
    }

    private bool IsSelectedLocked(LampArray lamp, int generation) =>
        _started && generation == _generation && ReferenceEquals(lamp, _lamp);

    private void TryDisposeCoordination()
    {
        CancellationTokenSource[] retired;
        bool disposeSemaphore;
        lock (_gate)
        {
            if (Volatile.Read(ref _refreshOperations) != 0) return;
            retired = _retiredCancellations.ToArray();
            _retiredCancellations.Clear();
            disposeSemaphore = _disposed && !_refreshGateDisposed;
            if (disposeSemaphore) _refreshGateDisposed = true;
        }
        foreach (var cancellation in retired) Cleanup(cancellation.Dispose, "DiscoveryCancellation.Dispose");
        if (disposeSemaphore) Cleanup(_refreshGate.Dispose, "RefreshGate.Dispose");
    }

    private void Cleanup(Action action, string stage)
    {
        try { action(); }
        catch (Exception e) { _cleanupErrors.Enqueue($"{stage}: {e.GetType().Name} 0x{e.HResult:X8}"); }
    }

    private readonly record struct SelectedLamp(LampArray? Lamp, bool Subscribed);

    /// <summary>Lifetime is independent of the selected WinRT projection; never resized during I/O.</summary>
    private sealed class FrameBuffers
    {
        public readonly LampColor[] Colors;
        public readonly LedColor[] LastColors;
        public readonly int[] Indices;
        public readonly string GrantedControlStatus;
        public int Count => Colors.Length;

        public FrameBuffers(int count)
        {
            Colors = new LampColor[count];
            LastColors = new LedColor[count];
            Indices = new int[count];
            GrantedControlStatus = $"已取得 {count} 顆燈的控制權。";
            for (int i = 0; i < count; i++) Indices[i] = i;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
