using MusicSync.Core;
using MusicSync.Lighting;
using Windows.UI.ViewManagement;

namespace MusicSync.UI;

internal sealed partial class MainForm
{
    private readonly ComboBox _signalMode = Choice("顏色變化依據", "音量", "聲頻（低／中／高頻）", "高低音平衡");
    private readonly ComboBox _paletteMode = Choice("色彩定義", "Windows 佈景主題", "彩虹", "自訂顏色");
    private readonly ComboBox _animationMode = Choice("色彩變化方式", "呼吸", "流動", "單顆依序");
    private readonly ComboBox _speedMode = Choice("變化快慢", "音樂節奏（自動估測）", "曲風快慢（手動）", "自訂速度");
    private readonly ComboBox _colorCount = Choice("自訂色彩數量", "2 色", "3 色", "4 色");
    private readonly ComboBox _styleSpeed = Choice("曲風快慢預設", "慢 · 抒情", "中 · 一般", "快 · 活潑");
    private readonly AccentSlider _customSpeed = new() { Minimum = 1, Maximum = 40, Value = 10, AccessibleName = "自訂速度，每秒循環次數的十分之一" };
    private readonly Label _speedValue = new();
    private readonly Label _tempoLabel = new();
    private readonly Label _modeHint = new();
    private readonly Label _automaticSpeedHint = new();
    private readonly Panel _speedDetails = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel _customSpeedLayout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
    private readonly Button[] _colorButtons = Enumerable.Range(0, 4).Select(i => (Button)new ActionButton
        { Width = 44, Height = 29, Text = (i + 1).ToString(), Margin = new Padding(4, 0, 0, 0), AccessibleName = $"自訂顏色 {i + 1}" }).ToArray();
    private readonly LedColor[] _customColors = new LedColor[4];
    private readonly Control _effectsPanel = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 18, 10), Margin = new Padding(0, 0, 0, 12) };
    private UISettings? _windowsUi;
    private bool _preferencesLoaded;
    private bool _loadingEffects;
    private bool? _preferenceSaveSucceeded;
    private readonly List<object> _effectControlChecks = new();
    private int _diagnosticEffectStep = -1;

    private static ComboBox Choice(string name, params string[] values)
    {
        var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = UiTheme.Background, ForeColor = UiTheme.Text, Dock = DockStyle.Fill,
            AccessibleName = name, IntegralHeight = true, DrawMode = DrawMode.OwnerDrawFixed,
            Margin = new Padding(0, 5, 10, 5) };
        control.DrawItem += (_, e) =>
        {
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(selected ? UiTheme.Border : UiTheme.Background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, control.Items[e.Index]?.ToString(), e.Font,
                Rectangle.Inflate(e.Bounds, -3, 0), control.Enabled ? UiTheme.Text : UiTheme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        control.Items.AddRange(values);
        control.SelectedIndex = 0;
        return control;
    }

    private Control BuildEffectsPanel()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, BackColor = UiTheme.Surface };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var height in new[] { 38, 38, 38 }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(Label("變色依據", _smallFont, UiTheme.Muted), 0, 0);
        grid.Controls.Add(_signalMode, 1, 0);
        grid.Controls.Add(Label("色彩定義", _smallFont, UiTheme.Muted), 2, 0);
        grid.Controls.Add(_paletteMode, 3, 0);
        grid.Controls.Add(Label("變化方式", _smallFont, UiTheme.Muted), 0, 1);
        grid.Controls.Add(_animationMode, 1, 1);
        grid.Controls.Add(Label("變化快慢", _smallFont, UiTheme.Muted), 2, 1);
        grid.Controls.Add(_speedMode, 3, 1);
        var colors = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 4, 10, 4), BackColor = UiTheme.Surface };
        _colorCount.Dock = DockStyle.None;
        _colorCount.Width = 68;
        _colorCount.Margin = Padding.Empty;
        colors.Controls.Add(_colorCount);
        colors.Controls.AddRange(_colorButtons);
        grid.Controls.Add(colors, 0, 2);
        grid.SetColumnSpan(colors, 2);
        _customSpeedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _customSpeedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        ConfigureLabel(_speedValue, "1.0 次／秒", _smallFont, UiTheme.Accent);
        _speedValue.TextAlign = ContentAlignment.MiddleRight;
        _customSpeedLayout.Controls.Add(_customSpeed, 0, 0);
        _customSpeedLayout.Controls.Add(_speedValue, 1, 0);
        _styleSpeed.Margin = new Padding(0, 4, 10, 4);
        ConfigureLabel(_automaticSpeedHint, "節拍不足時先用慢速", _smallFont, UiTheme.Muted);
        _speedDetails.Controls.Add(_automaticSpeedHint);
        _speedDetails.Controls.Add(_styleSpeed);
        _speedDetails.Controls.Add(_customSpeedLayout);
        grid.Controls.Add(_speedDetails, 2, 2);
        grid.SetColumnSpan(_speedDetails, 2);
        ConfigureLabel(_modeHint, "", _smallFont, UiTheme.Muted);
        grid.Controls.Add(_modeHint, 0, 3);
        grid.SetColumnSpan(_modeHint, 4);
        _effectsPanel.Controls.Add(grid);
        _toolTip.SetToolTip(_signalMode, "音量控制明暗；聲頻使用寬頻帶能量分布，高低音使用低音與高音的相對比例決定配色位置。");
        _toolTip.SetToolTip(_paletteMode, "Windows 佈景主題讀取系統輔色，並衍生深淺配色；App 介面維持蒂芬妮藍。");
        _toolTip.SetToolTip(_speedMode, "自動節奏是音訊起音間隔的估測，可能出現半拍／倍拍；曲風快慢為手動預設。");
        return _effectsPanel;
    }

    private void LoadPreferences()
    {
        _loadingEffects = true;
        var preferences = PreferenceStore.Load();
        var value = preferences.Effect;
        _gain.Value = preferences.GainDb;
        _brightness.Value = preferences.BrightnessPercent;
        _engine.GainDb = _gain.Value;
        _engine.Brightness = _brightness.Value / 100.0;
        _gainValue.Text = $"{_gain.Value:+0;-0;0} dB";
        _brightnessValue.Text = $"{_brightness.Value}%";
        _engine.Settings = value;
        _signalMode.SelectedIndex = (int)value.Signal;
        _paletteMode.SelectedIndex = (int)value.Palette;
        _animationMode.SelectedIndex = (int)value.Animation;
        _speedMode.SelectedIndex = (int)value.Speed;
        _colorCount.SelectedIndex = value.CustomColorCount - 2;
        _styleSpeed.SelectedIndex = value.StyleCyclesPerSecond < 0.55 ? 0 : value.StyleCyclesPerSecond > 1.1 ? 2 : 1;
        _customSpeed.Value = Math.Clamp((int)Math.Round(value.CustomCyclesPerSecond * 10), 1, 40);
        _customColors[0] = value.Color1; _customColors[1] = value.Color2;
        _customColors[2] = value.Color3; _customColors[3] = value.Color4;
        _loadingEffects = false;
        _preferencesLoaded = true;
        ApplyEffects();
        try
        {
            _windowsUi = new UISettings();
            _windowsUi.ColorValuesChanged += WindowsThemeChanged;
            RefreshWindowsTheme();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeLoadException) { }
    }

    private void WireEffectEvents()
    {
        foreach (var box in new[] { _signalMode, _paletteMode, _animationMode, _speedMode, _colorCount, _styleSpeed })
            box.SelectedIndexChanged += (_, _) => ApplyEffects();
        _customSpeed.ValueChanged += (_, _) => ApplyEffects();
        for (var index = 0; index < _colorButtons.Length; index++)
        {
            var colorIndex = index;
            _colorButtons[index].Click += (_, _) =>
            {
                var current = _customColors[colorIndex];
                using var dialog = new ColorDialog { FullOpen = true, Color = Color.FromArgb(current.R, current.G, current.B) };
                if (dialog.ShowDialog(this) != DialogResult.OK || _closing) return;
                _customColors[colorIndex] = new LedColor(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                ApplyEffects();
            };
        }
    }

    private void ApplyEffects()
    {
        if (_loadingEffects || _closing) return;
        var value = _engine.Settings with
        {
            Signal = (SignalMode)_signalMode.SelectedIndex, Palette = (PaletteMode)_paletteMode.SelectedIndex,
            Animation = (AnimationMode)_animationMode.SelectedIndex, Speed = (SpeedMode)_speedMode.SelectedIndex,
            CustomColorCount = _colorCount.SelectedIndex + 2,
            Color1 = _customColors[0], Color2 = _customColors[1], Color3 = _customColors[2], Color4 = _customColors[3],
            StyleCyclesPerSecond = _styleSpeed.SelectedIndex switch { 0 => 0.35, 2 => 1.5, _ => 0.75 },
            CustomCyclesPerSecond = _customSpeed.Value / 10.0
        };
        _engine.Settings = value;
        _colorCount.Enabled = value.Palette == PaletteMode.Custom;
        for (var index = 0; index < _colorButtons.Length; index++)
        {
            var button = _colorButtons[index];
            var color = _customColors[index];
            var background = Color.FromArgb(color.R, color.G, color.B);
            button.Visible = index < value.CustomColorCount;
            button.Enabled = value.Palette == PaletteMode.Custom;
            button.BackColor = button.Enabled ? background : UiTheme.Background;
            button.ForeColor = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 145 ? UiTheme.Background : Color.White;
            button.FlatAppearance.BorderColor = button.Enabled ? background : UiTheme.Border;
            button.FlatAppearance.MouseOverBackColor = background;
            button.FlatAppearance.MouseDownBackColor = background;
            _toolTip.SetToolTip(button, $"顏色 {index + 1}：#{color.R:X2}{color.G:X2}{color.B:X2}，按一下選色");
        }
        _customSpeedLayout.Visible = value.Speed == SpeedMode.Custom;
        _styleSpeed.Visible = value.Speed == SpeedMode.Style;
        _automaticSpeedHint.Visible = value.Speed == SpeedMode.Beat;
        _speedValue.Text = $"{value.CustomCyclesPerSecond:F1} 次／秒";
        _modeHint.Text = value.Signal switch
        {
            SignalMode.Frequency => "依低／中／高頻能量變色，音量控制明暗；非精確音高辨識。",
            SignalMode.BassTreble => "低音與高音的相對比例決定色彩，音量控制明暗。",
            _ => "音量決定色彩與明暗；暫停音樂後，各種效果皆會漸暗。"
        };
    }

    private void WindowsThemeChanged(UISettings sender, object args)
    {
        if (_closing || !IsHandleCreated || IsDisposed) return;
        try { BeginInvoke((Action)(() => { if (!_closing) RefreshWindowsTheme(); })); }
        catch (InvalidOperationException) { }
    }

    private void RefreshWindowsTheme()
    {
        if (_windowsUi == null) return;
        try
        {
            var color = _windowsUi.GetColorValue(UIColorType.Accent);
            _engine.Settings = _engine.Settings with { ThemeColor = new LedColor(color.R, color.G, color.B) };
        }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private void UnsubscribeWindowsTheme()
    {
        if (_windowsUi != null) _windowsUi.ColorValuesChanged -= WindowsThemeChanged;
        _windowsUi = null;
    }

    private void SavePreferences() => _preferenceSaveSucceeded = PreferenceStore.Save(new UserPreferences
        { GainDb = _gain.Value, BrightnessPercent = _brightness.Value, Effect = _engine.Settings });

    // Opt-in diagnostics drive the actual dropdown events, without saving test choices.
    private void ExerciseEffectControls()
    {
        var step = Math.Min(5, (int)_clock.Elapsed.TotalSeconds);
        if (step == _diagnosticEffectStep) return;
        _diagnosticEffectStep = step;
        var signal = step % 3;
        var palette = step % 3;
        var animation = (step + 1) % 3;
        var speed = (step + 2) % 3;
        var count = 2 + step % 3;
        _signalMode.SelectedIndex = signal;
        _paletteMode.SelectedIndex = palette;
        _animationMode.SelectedIndex = animation;
        _speedMode.SelectedIndex = speed;
        _colorCount.SelectedIndex = count - 2;
        _styleSpeed.SelectedIndex = step % 3;
        _customSpeed.Value = step == 5 ? 40 : 10;
        var value = _engine.Settings;
        _effectControlChecks.Add(new { Step = step, Signal = value.Signal.ToString(), Palette = value.Palette.ToString(),
            Animation = value.Animation.ToString(), Speed = value.Speed.ToString(), CustomColorCount = value.CustomColorCount,
            Passed = value.Signal == (SignalMode)signal && value.Palette == (PaletteMode)palette &&
                value.Animation == (AnimationMode)animation && value.Speed == (SpeedMode)speed && value.CustomColorCount == count,
            CustomColorEditingEnabled = _colorCount.Enabled, CustomSpeedVisible = _customSpeedLayout.Visible });
    }
}
