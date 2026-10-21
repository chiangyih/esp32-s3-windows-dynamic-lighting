using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using MusicSync.Core;

namespace MusicSync.UI;

internal sealed partial class MainForm : Form
{
    private readonly SyncEngine _engine = new();
    private readonly LaunchOptions _options;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _closeTestTimer = new();
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stopwatch _shutdownClock = new();
    private readonly TimeSpan _initialCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private readonly Font _bodyFont = new("Microsoft JhengHei UI", 10);
    private readonly Font _smallFont = new("Microsoft JhengHei UI", 9);
    private readonly Font _headingFont = new("Microsoft JhengHei UI", 21, FontStyle.Bold);
    private readonly Font _statusFont = new("Microsoft JhengHei UI", 12, FontStyle.Bold);
    private readonly Font _numberFont = new("Consolas", 11);
    private readonly Label _state = new();
    private readonly Label _audio = new();
    private readonly Label _lighting = new();
    private readonly Label _level = new();
    private readonly Label _gainValue = new();
    private readonly Label _brightnessValue = new();
    private readonly MeterPreview _preview = new();
    private readonly Button _start = new ActionButton { Text = "開始同步 (&S)", Width = 136 };
    private readonly Button _stop = new ActionButton { Text = "停止 (&T)", Width = 88, Enabled = false };
    private readonly Button _minimize = new ActionButton { Text = "縮至通知區", Width = 120 };
    private readonly Button _exit = new ActionButton { Name = "CloseAppButton", Text = "關閉程式 (&Q)", Width = 140, AccessibleName = "關閉程式並釋放資源" };
    private readonly AccentSlider _gain = new() { Minimum = -12, Maximum = 24, Value = 0, AccessibleName = "靈敏度，分貝" };
    private readonly AccentSlider _brightness = new() { Minimum = 5, Maximum = 100, Value = 65, AccessibleName = "燈光亮度，百分比" };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayExit = new("關閉程式");
    private readonly ToolTip _toolTip = new();
    private readonly TableLayoutPanel _content = new();
    private readonly Icon? _ownedIcon;
    private readonly Bitmap _headerImage;
    private readonly List<object> _diagnosticSamples = new();
    private readonly List<string> _uiCleanupErrors = new();
    private Dictionary<string, object?>? _report;
    private bool _busy;
    private bool _stoppingSync;
    private bool _stopFailed;
    private bool _stopRestartPassed;
    private ShutdownState? _lifecycleStopSnapshot;
    private string? _lifecycleError;
    private bool _closing;
    private bool _shutdownComplete;
    private bool _uiResourcesDisposed;
    private bool _fontsDisposed;
    private bool _disposeStarted;
    private bool _diagnosticEnded;
    private bool _everAvailable;
    private string _closeRoute = "window";
    private string? _startError;
    private long _maxWorkingSet;
    private long _maxPrivateBytes;
    private TimeSpan? _steadyCpuStart;
    private double _steadyStartSeconds;
    private int _lastDiagnosticSecond = -1;

    public MainForm(LaunchOptions options)
    {
        _options = options;
        Text = Program.DisplayName;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = _bodyFont;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        ClientSize = new Size(820, 786);
        MinimumSize = new Size(750, 810);
        StartPosition = FormStartPosition.CenterScreen;
        _ownedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Icon = _ownedIcon ?? SystemIcons.Application;
        _headerImage = Icon.ToBitmap();
        BuildLayout();
        LoadPreferences();
        WireEvents();
    }

    private void BuildLayout()
    {
        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(24);
        _content.BackColor = UiTheme.Background;
        _content.ColumnCount = 1;
        _content.RowCount = 7;
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 70, 136, 192, 104, 92 })
            _content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        _content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var image = new PictureBox { Image = _headerImage, SizeMode = PictureBoxSizeMode.CenterImage, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), AccessibleName = "音樂同步圖示" };
        header.Controls.Add(image, 0, 0);
        header.SetRowSpan(image, 2);
        header.Controls.Add(Label(Program.DisplayName, _headingFont, UiTheme.Accent, UiTheme.Background), 1, 0);
        header.Controls.Add(Label("電腦播放音訊 · 8 顆 LED", _smallFont, UiTheme.Muted, UiTheme.Background), 1, 1);
        _content.Controls.Add(header, 0, 0);

        var meter = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 0, 0, 12) };
        var meterLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ColumnCount = 2, RowCount = 3 };
        meterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        meterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        meterLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        meterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        meterLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        ConfigureLabel(_state, "已停止", _statusFont, UiTheme.Accent);
        ConfigureLabel(_level, "靜音", _numberFont, UiTheme.Muted);
        _level.TextAlign = ContentAlignment.MiddleRight;
        meterLayout.Controls.Add(_state, 0, 0);
        meterLayout.Controls.Add(_level, 1, 0);
        _preview.Margin = new Padding(0, 7, 0, 7);
        meterLayout.Controls.Add(_preview, 0, 1);
        meterLayout.SetColumnSpan(_preview, 2);
        meterLayout.Controls.Add(Label("連續色彩預覽 · 8 LED", _smallFont, UiTheme.Muted), 0, 2);
        ConfigureLabel(_tempoLabel, "節奏估測中", _smallFont, UiTheme.Muted);
        _tempoLabel.TextAlign = ContentAlignment.MiddleRight;
        meterLayout.Controls.Add(_tempoLabel, 1, 2);
        meter.Controls.Add(meterLayout);
        _content.Controls.Add(meter, 0, 1);
        _content.Controls.Add(BuildEffectsPanel(), 0, 2);

        var parameters = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 18, 10), Margin = new Padding(0, 0, 0, 12) };
        var sliderLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ColumnCount = 3, RowCount = 2 };
        sliderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        sliderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sliderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        sliderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sliderLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sliderLayout.Controls.Add(Label("靈敏度", _bodyFont, UiTheme.Text), 0, 0);
        sliderLayout.Controls.Add(_gain, 1, 0);
        ConfigureLabel(_gainValue, "+0 dB", _numberFont, UiTheme.Accent);
        _gainValue.TextAlign = ContentAlignment.MiddleRight;
        sliderLayout.Controls.Add(_gainValue, 2, 0);
        sliderLayout.Controls.Add(Label("燈光亮度", _bodyFont, UiTheme.Text), 0, 1);
        sliderLayout.Controls.Add(_brightness, 1, 1);
        ConfigureLabel(_brightnessValue, "65%", _numberFont, UiTheme.Accent);
        _brightnessValue.TextAlign = ContentAlignment.MiddleRight;
        sliderLayout.Controls.Add(_brightnessValue, 2, 1);
        parameters.Controls.Add(sliderLayout);
        _content.Controls.Add(parameters, 0, 3);

        var devices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        devices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        devices.Controls.Add(StatusCard("播放音訊", _audio, "尚未開始", new Padding(0, 0, 6, 0)), 0, 0);
        devices.Controls.Add(StatusCard("USB 燈條", _lighting, "尚未開始", new Padding(6, 0, 0, 0)), 1, 0);
        _content.Controls.Add(devices, 0, 4);

        var help = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 0, 10) };
        var settings = new LinkLabel { Text = "Windows 動態光效設定 ↗", Font = _smallFont, Dock = DockStyle.Top, AutoSize = true, LinkColor = UiTheme.Accent, ActiveLinkColor = UiTheme.AccentHover, VisitedLinkColor = UiTheme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 0, 6) };
        settings.LinkClicked += (_, _) => OpenLightingSettings();
        help.Controls.Add(settings, 0, 0);
        help.Controls.Add(Label("僅在記憶體分析播放音訊，不錄音、不上傳。\n暫停後燈光漸暗；首次使用請在 Windows 設定調整背景控制優先順序。", _smallFont, UiTheme.Muted, UiTheme.Background), 0, 1);
        _content.Controls.Add(help, 0, 5);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        ConfigureButton(_start, true);
        ConfigureButton(_stop);
        ConfigureButton(_minimize);
        ConfigureButton(_exit);
        _exit.ForeColor = Color.FromArgb(0xEE, 0xB9, 0xC1);
        _exit.FlatAppearance.BorderColor = Color.FromArgb(0x64, 0x42, 0x4D);
        _exit.Margin = new Padding(8, 0, 0, 0);
        actions.Controls.AddRange(new Control[] { _start, _stop, _minimize });
        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(_exit, 1, 0);
        _content.Controls.Add(footer, 0, 6);
        Controls.Add(_content);
        _toolTip.SetToolTip(_gain, "提高靈敏度可讓較小的音量產生更明顯的光效。可用方向鍵調整。");
        _toolTip.SetToolTip(_minimize, "隱藏至通知區，繼續在背景同步。");
        _toolTip.SetToolTip(_exit, "停止音訊與燈光工作，釋放資源並結束程式。");
        _toolTip.SetToolTip(_stop, "停止同步並保留視窗；Windows 可恢復原先燈效。");
    }

    private void WireEvents()
    {
        WireEffectEvents();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await StopSyncAsync();
        _minimize.Click += (_, _) => { if (!_closing) { _tray.Visible = true; Hide(); } };
        _exit.Click += (_, _) => RequestClose("button");
        _gain.ValueChanged += (_, _) => { _engine.GainDb = _gain.Value; _gainValue.Text = $"{_gain.Value:+0;-0;0} dB"; };
        _brightness.ValueChanged += (_, _) => { _engine.Brightness = _brightness.Value / 100.0; _brightnessValue.Text = $"{_brightness.Value}%"; };
        _tray.Icon = Icon;
        _tray.Text = Program.DisplayName;
        _trayMenu.Items.Add("開啟視窗", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add("停止同步", null, async (_, _) => await StopSyncAsync());
        _trayExit.Click += (_, _) => RequestClose("tray");
        _trayMenu.Items.Add(_trayExit);
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _uiTimer.Tick += (_, _) => UpdateStatus();
        _closeTestTimer.Tick += (_, _) => { _closeTestTimer.Stop(); InvokeDiagnosticClose(); };
        Shown += async (_, _) =>
        {
            _uiTimer.Start();
            if (_options.CloseAfterMilliseconds > 0)
            {
                _closeTestTimer.Interval = _options.CloseAfterMilliseconds;
                _closeTestTimer.Start();
            }
            if (_options.AutoStart) await StartAsync();
            if (_options.ExerciseLifecycle && _options.DiagnosticSeconds > 0 && !_closing)
                await ExerciseLifecycleAsync();
        };
    }

    private static void ConfigureLabel(Label label, string text, Font font, Color colour, Color? background = null)
    {
        label.Text = text;
        label.Font = font;
        label.ForeColor = colour;
        label.BackColor = background ?? UiTheme.Surface;
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.Margin = Padding.Empty;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private static Label Label(string text, Font font, Color colour, Color? background = null)
    {
        var label = new Label();
        ConfigureLabel(label, text, font, colour, background);
        return label;
    }

    private Control StatusCard(string title, Label value, string initial, Padding margin)
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(15, 10, 15, 10), Margin = margin };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Label(title, _smallFont, UiTheme.Accent), 0, 0);
        ConfigureLabel(value, initial, _smallFont, UiTheme.Text);
        layout.Controls.Add(value, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static void ConfigureButton(Button button, bool primary = false)
    {
        button.Height = 42;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? UiTheme.Accent : UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? UiTheme.AccentHover : UiTheme.Surface;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0x5D, 0xBD, 0xB4) : UiTheme.Border;
        button.Margin = new Padding(0, 0, 8, 0);
        button.Cursor = Cursors.Hand;
        void UpdateColour()
        {
            button.BackColor = primary && button.Enabled ? UiTheme.Accent : UiTheme.Background;
            button.ForeColor = !button.Enabled ? UiTheme.Muted : primary ? UiTheme.OnAccent : UiTheme.Text;
        }
        button.EnabledChanged += (_, _) => UpdateColour();
        UpdateColour();
    }

    private async Task StartAsync()
    {
        if (_closing || _busy || _engine.IsRunning) return;
        _busy = true;
        _stopFailed = false;
        _startError = null;
        _start.Enabled = false;
        _state.Text = "正在連接…";
        try
        {
            await _engine.StartAsync();
            if (_closing || IsDisposed || Disposing) return;
            _stop.Enabled = true;
        }
        catch (Exception e)
        {
            if (_closing || IsDisposed || Disposing) return;
            _startError = $"{e.GetType().Name}: 0x{e.HResult:X8}";
            _state.Text = "無法開始，請檢查裝置";
            _start.Enabled = true;
        }
        finally { _busy = false; }
    }

    private async Task<bool> StopSyncAsync()
    {
        if (_closing || _busy) return false;
        _busy = true;
        _stoppingSync = true;
        _stop.Enabled = false;
        _start.Enabled = false;
        _state.Text = "正在停止並釋放資源…";
        try
        {
            await _engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(6));
            if (_closing || IsDisposed || Disposing) return false;
            _start.Enabled = true;
            _preview.UpdateFrame(new MusicSync.Lighting.LedColor[8]);
            _state.Text = "已停止";
            return true;
        }
        catch (Exception ex)
        {
            if (_closing || IsDisposed || Disposing) return false;
            _stopFailed = true;
            _lifecycleError = $"EngineStop:{ex.GetType().Name}:0x{ex.HResult:X8}";
            _state.Text = "停止未完成，請關閉程式";
            return false;
        }
        finally { _busy = false; _stoppingSync = false; }
    }

    private async Task ExerciseLifecycleAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _lifecycleCancellation.Token);
            if (_closing || !_engine.IsRunning || !await StopSyncAsync()) return;
            if (_closing) return;
            _lifecycleStopSnapshot = _engine.ShutdownSnapshot;
            if (_lifecycleStopSnapshot.AudioWorkerRunning || _lifecycleStopSnapshot.RenderWorkerRunning ||
                _lifecycleStopSnapshot.LightingDiscoveryRunning || _lifecycleStopSnapshot.CleanupWorkerRunning ||
                _lifecycleStopSnapshot.CleanupErrors.Length != 0)
            {
                _lifecycleError = "Stop completed while an owned worker was still active.";
                return;
            }
            await StartAsync();
            if (!_closing) _stopRestartPassed = _engine.IsRunning && _startError == null;
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested) { }
        catch (Exception ex) { _lifecycleError = $"LifecycleExercise:{ex.GetType().Name}:0x{ex.HResult:X8}"; }
    }

    private void UpdateStatus()
    {
        if (_closing) return;
        if (_options.ExerciseEffects && _options.DiagnosticSeconds > 0) ExerciseEffectControls();
        _everAvailable |= _engine.HasLightingControl;
        if (_options.ReportPath != null)
        {
            using var process = Process.GetCurrentProcess();
            _maxWorkingSet = Math.Max(_maxWorkingSet, process.WorkingSet64);
            _maxPrivateBytes = Math.Max(_maxPrivateBytes, process.PrivateMemorySize64);
            if (_steadyCpuStart == null && _clock.Elapsed.TotalSeconds >= 5)
            {
                _steadyCpuStart = process.TotalProcessorTime;
                _steadyStartSeconds = _clock.Elapsed.TotalSeconds;
            }
            var second = (int)_clock.Elapsed.TotalSeconds;
            if (second != _lastDiagnosticSecond)
            {
                _lastDiagnosticSecond = second;
                _diagnosticSamples.Add(new { Second = second, Rms = _engine.Rms, Level = _engine.Level,
                    BassRms = _engine.BassRms, MidRms = _engine.MidRms, TrebleRms = _engine.TrebleRms, TempoBpm = _engine.TempoBpm,
                    HasControl = _engine.HasLightingControl, SentFrames = _engine.SentFrames });
            }
        }
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            _state.Text = _busy ? _stoppingSync ? "正在停止並釋放資源…" : "正在連接…" :
                _stopFailed ? "停止未完成，請關閉程式" :
                _engine.IsRunning ? _engine.HasLightingControl ? "● 同步中" : "○ 等待燈光控制權" :
                _startError == null ? "○ 已停止" : "無法開始，請檢查裝置";
            _audio.Text = _engine.AudioStatus;
            _lighting.Text = _engine.LightingStatus;
            var rms = _engine.Rms;
            _level.Text = rms > 0.00001 ? $"{20 * Math.Log10(rms):F1} dBFS" : "靜音";
            Span<MusicSync.Lighting.LedColor> colors = stackalloc MusicSync.Lighting.LedColor[8];
            _engine.CopyPreview(colors);
            _preview.UpdateFrame(colors);
            var bpm = _engine.TempoBpm;
            _tempoLabel.Text = _engine.Settings.Speed == SpeedMode.Beat
                ? bpm > 0 ? $"估測 {bpm:F0} BPM" : "節奏估測中 · 慢速"
                : _engine.Settings.Speed == SpeedMode.Style ? "曲風快慢 · 手動" : $"自訂 {_engine.Settings.CustomCyclesPerSecond:F1} 次／秒";
        }
        if (_options.DiagnosticSeconds > 0 && !_diagnosticEnded && _clock.Elapsed.TotalSeconds >= _options.DiagnosticSeconds)
        {
            _diagnosticEnded = true;
            InvokeDiagnosticClose();
        }
    }

    private void InvokeDiagnosticClose()
    {
        if (_options.CloseViaButton) _exit.PerformClick();
        else if (_options.CloseViaTray) _trayExit.PerformClick();
        else RequestClose("diagnostic-window");
    }

    private void RequestClose(string route)
    {
        if (_closing) return;
        _closeRoute = route;
        Close();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownComplete) { base.OnFormClosing(e); return; }
        e.Cancel = true;
        base.OnFormClosing(e);
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _lifecycleCancellation.Cancel();
        _shutdownClock.Start();
        if (_options.DiagnosticSeconds == 0 && _options.CloseAfterMilliseconds == 0) SavePreferences();
        CaptureBeforeClose();
        _uiTimer.Stop();
        _closeTestTimer.Stop();
        _start.Enabled = _stop.Enabled = _minimize.Enabled = _exit.Enabled = false;
        _gain.Enabled = _brightness.Enabled = false;
        _effectsPanel.Enabled = false;
        _state.Text = "正在關閉並釋放資源…";
        try
        {
            await _engine.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(6));
            if (!await _engine.WaitForShutdownAsync(TimeSpan.FromSeconds(2)))
                _uiCleanupErrors.Add("EngineShutdown: background cleanup exceeded 2000 ms; Windows reclaims remaining resources on process exit.");
        }
        catch (Exception ex) { _uiCleanupErrors.Add($"EngineShutdown:{ex.GetType().Name}:0x{ex.HResult:X8}"); }
        finally
        {
            ReleaseUiComponents();
            _shutdownComplete = true;
            if (!IsDisposed) Close();
        }
    }

    private void CaptureBeforeClose()
    {
        try
        {
            if (_options.ScreenshotPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_options.ScreenshotPath)!);
                using var bitmap = new Bitmap(_content.Width, _content.Height);
                _content.DrawToBitmap(bitmap, new Rectangle(Point.Empty, _content.Size));
                bitmap.Save(_options.ScreenshotPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            if (_options.ReportPath == null) return;
            using var process = Process.GetCurrentProcess();
            var cpuMs = (process.TotalProcessorTime - _initialCpu).TotalMilliseconds;
            uint identityLength = 0;
            _report = new Dictionary<string, object?>
            {
                ["ObservedAtUtc"] = DateTimeOffset.UtcNow, ["Version"] = Application.ProductVersion,
                ["Runtime"] = RuntimeInformation.FrameworkDescription, ["LanguageVersion"] = "C# 10.0",
                ["PackageIdentity"] = GetCurrentPackageFullName(ref identityLength, IntPtr.Zero) != 15700,
                ["DurationSeconds"] = _clock.Elapsed.TotalSeconds, ["AudioPackets"] = _engine.AudioPackets,
                ["MaximumRms"] = _engine.MaximumRms, ["AudioDevice"] = _engine.AudioDevice,
                ["AudioStatus"] = _engine.AudioStatus, ["ConnectedCount"] = _engine.ConnectedCount,
                ["LightingStatus"] = _engine.LightingStatus, ["EverHadLightingControl"] = _everAvailable,
                ["SentFrames"] = _engine.SentFrames, ["BlackFramesSent"] = _engine.BlackFramesSent,
                ["CpuMilliseconds"] = cpuMs, ["CpuPercentOfOneCore"] = cpuMs / _clock.Elapsed.TotalMilliseconds * 100,
                ["CpuPercentOfMachine"] = cpuMs / _clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                ["SteadyStateSeconds"] = _steadyCpuStart == null ? 0 : _clock.Elapsed.TotalSeconds - _steadyStartSeconds,
                ["SteadyCpuPercentOfOneCore"] = _steadyCpuStart == null ? null :
                    (process.TotalProcessorTime - _steadyCpuStart.Value).TotalSeconds / (_clock.Elapsed.TotalSeconds - _steadyStartSeconds) * 100,
                ["PeakWorkingSetBytes"] = _maxWorkingSet, ["PeakPrivateBytes"] = _maxPrivateBytes,
                ["Threads"] = process.Threads.Count, ["StartError"] = _startError, ["Samples"] = _diagnosticSamples,
                ["PhysicalLedObservation"] = "API results do not replace physical observation.", ["AudioStored"] = false,
                ["Theme"] = "Tiffany blue / graphite", ["CloseRequestedBy"] = _closeRoute
            };
            _report["EffectSettings"] = _engine.Settings;
            _report["EffectControlChecks"] = _effectControlChecks;
            _report["SettingsLoaded"] = _preferencesLoaded;
            _report["PreferenceSaveSucceeded"] = _preferenceSaveSucceeded;
            _report["LifecycleExerciseRequested"] = _options.ExerciseLifecycle;
            _report["StopRestartPassed"] = _stopRestartPassed;
            _report["LifecycleStopSnapshot"] = _lifecycleStopSnapshot;
            _report["LifecycleError"] = _lifecycleError;
        }
        catch (Exception ex) { _uiCleanupErrors.Add($"DiagnosticCapture:{ex.GetType().Name}:0x{ex.HResult:X8}"); }
    }

    internal void SaveExitReport()
    {
        if (_options.ReportPath == null) return;
        _report ??= new Dictionary<string, object?> { ["Version"] = Application.ProductVersion, ["CloseRequestedBy"] = _closeRoute };
        _report["Shutdown"] = _engine.ShutdownSnapshot;
        _report["FormDisposed"] = IsDisposed;
        _report["UiComponentsDisposed"] = _uiResourcesDisposed;
        _report["OwnedFontsDisposed"] = _fontsDisposed;
        _report["UiCleanupErrors"] = _uiCleanupErrors;
        _report["ShutdownElapsedMilliseconds"] = _shutdownClock.Elapsed.TotalMilliseconds;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.ReportPath)!);
        File.WriteAllText(_options.ReportPath, JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ReleaseUiComponents()
    {
        if (_uiResourcesDisposed) return;
        void Release(string name, Action release)
        {
            try { release(); }
            catch (Exception ex) { _uiCleanupErrors.Add($"{name}:{ex.GetType().Name}:0x{ex.HResult:X8}"); }
        }
        Release("UiTimer", () => { _uiTimer.Stop(); _uiTimer.Dispose(); });
        Release("CloseTestTimer", () => { _closeTestTimer.Stop(); _closeTestTimer.Dispose(); });
        Release("LifecycleCancellation", () => { _lifecycleCancellation.Cancel(); _lifecycleCancellation.Dispose(); });
        Release("TrayIcon", () => { _tray.Visible = false; _tray.Dispose(); });
        Release("TrayMenu", () => _trayMenu.Dispose());
        Release("ToolTip", () => _toolTip.Dispose());
        Release("WindowsThemeEvents", UnsubscribeWindowsTheme);
        _uiResourcesDisposed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || _disposeStarted) { base.Dispose(disposing); return; }
        _disposeStarted = true;
        try
        {
            try { if (!_engine.IsDisposed) _engine.Dispose(); }
            catch (Exception ex) { _uiCleanupErrors.Add($"EngineDispose:{ex.GetType().Name}:0x{ex.HResult:X8}"); }
            ReleaseUiComponents();
            base.Dispose(true);
        }
        finally
        {
            void ReleaseVisual(string name, IDisposable? resource)
            {
                try { resource?.Dispose(); }
                catch (Exception ex) { _uiCleanupErrors.Add($"{name}:{ex.GetType().Name}:0x{ex.HResult:X8}"); }
            }
            ReleaseVisual("HeaderImage", _headerImage);
            ReleaseVisual("WindowIcon", _ownedIcon);
            ReleaseVisual("BodyFont", _bodyFont);
            ReleaseVisual("SmallFont", _smallFont);
            ReleaseVisual("HeadingFont", _headingFont);
            ReleaseVisual("StatusFont", _statusFont);
            ReleaseVisual("NumberFont", _numberFont);
            _fontsDisposed = true;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!SystemInformation.HighContrast)
        {
            var dark = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            var caption = ColorTranslator.ToWin32(UiTheme.Background);
            _ = DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            var text = ColorTranslator.ToWin32(UiTheme.Text);
            _ = DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }
    }

    private static void OpenLightingSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:personalization-lighting") { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("請開啟 Windows 設定 → 個人化 → 動態光效。", "Windows 動態光效"); }
    }

    private void RestoreWindow()
    {
        if (_closing) return;
        Show(); WindowState = FormWindowState.Normal; Activate(); _tray.Visible = false;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr value);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int length);

    private sealed class SurfacePanel : Panel
    {
        public SurfacePanel() { DoubleBuffered = true; BackColor = UiTheme.Background; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = UiTheme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 8 * DeviceDpi / 96f);
            using var fill = new SolidBrush(UiTheme.Surface);
            using var border = new Pen(UiTheme.Border);
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        }
    }
}
