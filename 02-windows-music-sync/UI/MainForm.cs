using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MusicSync.Core;

namespace MusicSync.UI;

internal sealed class MainForm : Form
{
    private readonly SyncEngine _engine = new();
    private readonly LaunchOptions _options;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _initialCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private readonly Label _state = TextLabel("已停止", true);
    private readonly Label _audio = TextLabel("播放音訊：尚未開始");
    private readonly Label _lighting = TextLabel("燈條：尚未開始");
    private readonly Label _level = TextLabel("音量：—");
    private readonly Label _gainLabel = TextLabel("靈敏度：+0 dB");
    private readonly Label _brightnessLabel = TextLabel("燈光亮度：65%");
    private readonly MeterPreview _preview = new();
    private readonly Button _start = new() { Text = "開始同步 (&S)", AutoSize = true };
    private readonly Button _stop = new() { Text = "停止 (&T)", AutoSize = true, Enabled = false };
    private readonly TrackBar _gain = new() { Minimum = -12, Maximum = 24, Value = 0, TickFrequency = 6, Dock = DockStyle.Fill, AccessibleName = "靈敏度，分貝" };
    private readonly TrackBar _brightness = new() { Minimum = 5, Maximum = 100, Value = 65, TickFrequency = 25, Dock = DockStyle.Fill, AccessibleName = "燈光亮度，百分比" };
    private readonly NotifyIcon _tray = new();
    private bool _busy;
    private bool _closing;
    private string? _startError;
    private long _maxWorkingSet;
    private long _maxPrivateBytes;
    private bool _everAvailable;
    private bool _diagnosticEnded;
    private TimeSpan? _steadyCpuStart;
    private double _steadyStartSeconds;
    private int _lastDiagnosticSecond = -1;
    private readonly List<object> _diagnosticSamples = new();

    public MainForm(LaunchOptions options)
    {
        _options = options;
        Text = "ESP32 音樂同步";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft JhengHei UI", 10);
        ClientSize = new Size(620, 610);
        MinimumSize = new Size(550, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 12 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(TextLabel("電腦播放音訊 → 8 顆 WS2812", true), 0, 0);
        layout.Controls.Add(TextLabel("請在 YouTube 播放音樂，再按「開始同步」。"), 0, 1);
        layout.Controls.Add(_state, 0, 2);
        layout.Controls.Add(_preview, 0, 3);
        layout.Controls.Add(_level, 0, 4);
        layout.Controls.Add(_audio, 0, 5);
        layout.Controls.Add(_lighting, 0, 6);

        var sliders = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoSize = true };
        sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sliders.Controls.Add(_gainLabel, 0, 0);
        sliders.Controls.Add(_gain, 1, 0);
        sliders.Controls.Add(_brightnessLabel, 0, 1);
        sliders.Controls.Add(_brightness, 1, 1);
        layout.Controls.Add(sliders, 0, 7);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var settings = new Button { Text = "Windows 動態光效", AutoSize = true };
        var minimize = new Button { Text = "縮至通知區", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { _start, _stop, settings, minimize });
        layout.Controls.Add(buttons, 0, 8);
        var help = TextLabel("首次使用：在 Windows「動態光效 → 背景光源控制項」將本 App 排在前面。App 取得控制權後，不必更改內建效果。無控制權時預覽仍會顯示音量。");
        help.MaximumSize = new Size(1200, 0);
        layout.Controls.Add(help, 0, 9);
        layout.Controls.Add(TextLabel("只在記憶體分析預設播放裝置的聲音（含其他 App）。不使用麥克風、不儲存錄音、不傳送音訊。"), 0, 10);
        layout.Controls.Add(TextLabel("關閉視窗即結束同步；可按「縮至通知區」繼續在背景執行。"), 0, 11);
        for (var i = 0; i < layout.RowCount; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[3] = new RowStyle(SizeType.Absolute, 60);
        layout.RowStyles[6] = new RowStyle(SizeType.Absolute, 66);
        Controls.Add(layout);

        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => StopSync();
        settings.Click += (_, _) => OpenLightingSettings();
        minimize.Click += (_, _) => { _tray.Visible = true; Hide(); };
        _gain.ValueChanged += (_, _) =>
        {
            _engine.GainDb = _gain.Value;
            _gainLabel.Text = $"靈敏度：{_gain.Value:+0;-0;0} dB";
        };
        _brightness.ValueChanged += (_, _) =>
        {
            _engine.Brightness = _brightness.Value / 100.0;
            _brightnessLabel.Text = $"燈光亮度：{_brightness.Value}%";
        };
        _tray.Icon = Icon;
        _tray.Text = "ESP32 音樂同步";
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("開啟", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add("停止同步", null, (_, _) => StopSync());
        trayMenu.Items.Add("結束", null, (_, _) => Close());
        _tray.ContextMenuStrip = trayMenu;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _uiTimer.Tick += (_, _) => UpdateStatus();
        Shown += async (_, _) =>
        {
            _uiTimer.Start();
            if (_options.AutoStart) await StartAsync();
        };
    }

    private static Label TextLabel(string text, bool emphasis = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill,
        Margin = new Padding(0, 0, 0, 10),
        Font = new Font("Microsoft JhengHei UI", emphasis ? 12 : 10, emphasis ? FontStyle.Bold : FontStyle.Regular)
    };

    private async Task StartAsync()
    {
        if (_busy || _engine.IsRunning) return;
        _busy = true;
        _startError = null;
        _start.Enabled = false;
        _state.Text = "正在連接播放音訊與燈條…";
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
            _state.Text = "無法開始，請確認音訊裝置與 Windows 動態光效。";
            _start.Enabled = true;
        }
        finally { _busy = false; }
    }

    private void StopSync()
    {
        if (_busy) return;
        _engine.Stop();
        _stop.Enabled = false;
        _start.Enabled = true;
        _preview.UpdateLevel(0, _engine.Brightness);
        _state.Text = "已停止；Windows 可恢復原先燈效";
    }

    private void UpdateStatus()
    {
        _everAvailable |= _engine.HasLightingControl;
        if (_options.DiagnosticSeconds > 0)
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
                    HasControl = _engine.HasLightingControl, SentFrames = _engine.SentFrames });
            }
        }
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            _state.Text = _engine.IsRunning
                ? _engine.HasLightingControl ? "同步中" : "音訊分析中，等待燈條控制權"
                : _startError == null ? "已停止" : "無法開始：" + _startError;
            _audio.Text = "播放音訊：" + _engine.AudioStatus;
            _lighting.Text = "燈條：" + _engine.LightingStatus;
            var rms = _engine.Rms;
            _level.Text = $"音量：{(rms > 0 ? 20 * Math.Log10(rms) : -120):F1} dBFS　已送出 {_engine.SentFrames} 幀";
            _preview.UpdateLevel(_engine.Level, _engine.Brightness);
        }
        if (_options.DiagnosticSeconds > 0 && !_diagnosticEnded && _clock.Elapsed.TotalSeconds >= _options.DiagnosticSeconds)
        {
            _diagnosticEnded = true;
            SaveDiagnosticReport();
            Close();
        }
    }

    private void SaveDiagnosticReport()
    {
        if (_options.ScreenshotPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_options.ScreenshotPath)!);
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(_options.ScreenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        if (_options.ReportPath == null) return;
        using var process = Process.GetCurrentProcess();
        var cpuMs = (process.TotalProcessorTime - _initialCpu).TotalMilliseconds;
        var report = new
        {
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Version = Application.ProductVersion,
            Runtime = RuntimeInformation.FrameworkDescription,
            LanguageVersion = "C# 10.0",
            PackageIdentity = GetCurrentPackageFullName(ref _identityLength, null) != 15700,
            DurationSeconds = _clock.Elapsed.TotalSeconds,
            AudioPackets = _engine.AudioPackets,
            MaximumRms = _engine.MaximumRms,
            AudioDevice = _engine.AudioDevice,
            AudioStatus = _engine.AudioStatus,
            ConnectedCount = _engine.ConnectedCount,
            LightingStatus = _engine.LightingStatus,
            EverHadLightingControl = _everAvailable,
            SentFrames = _engine.SentFrames,
            BlackFramesSent = _engine.BlackFramesSent,
            CpuMilliseconds = cpuMs,
            CpuPercentOfOneCore = cpuMs / _clock.Elapsed.TotalMilliseconds * 100,
            CpuPercentOfMachine = cpuMs / _clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
            SteadyStateSeconds = _steadyCpuStart == null ? 0 : _clock.Elapsed.TotalSeconds - _steadyStartSeconds,
            SteadyCpuPercentOfOneCore = _steadyCpuStart == null ? (double?)null :
                (process.TotalProcessorTime - _steadyCpuStart.Value).TotalSeconds /
                (_clock.Elapsed.TotalSeconds - _steadyStartSeconds) * 100,
            PeakWorkingSetBytes = _maxWorkingSet,
            PeakPrivateBytes = _maxPrivateBytes,
            Threads = process.Threads.Count,
            StartError = _startError,
            Samples = _diagnosticSamples,
            PhysicalLedObservation = "Requires user confirmation; API results do not verify physical LEDs.",
            AudioStored = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.ReportPath)!);
        File.WriteAllText(_options.ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private uint _identityLength;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);

    private static void OpenLightingSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:personalization-lighting") { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("請開啟 Windows 設定 → 個人化 → 動態光效。", "Windows 動態光效"); }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _tray.Visible = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop();
        _engine.Dispose();
        _uiTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
        if (e.Cancel) _closing = false;
    }
}
