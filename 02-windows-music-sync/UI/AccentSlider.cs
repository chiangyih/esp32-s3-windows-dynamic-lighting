using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace MusicSync.UI;

/// <summary>A lightweight keyboard-accessible slider with no timer or animation.</summary>
internal sealed class AccentSlider : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;
    private bool _hovered;

    public AccentSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);
        Height = 34;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Accent;
        AccessibleRole = AccessibleRole.Slider;
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    [DefaultValue(0)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value) return;
            _minimum = value;
            if (_maximum < value) _maximum = value;
            SetValue(_value);
            Invalidate();
        }
    }

    [DefaultValue(100)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value) return;
            _maximum = value;
            if (_minimum > value) _minimum = value;
            SetValue(_value);
            Invalidate();
        }
    }

    [DefaultValue(0)]
    public int Value { get => _value; set => SetValue(value); }

    [DefaultValue(1)]
    public int SmallChange { get; set; } = 1;

    [Browsable(false)]
    public int LargeChange => (int)Math.Max(1, ((long)_maximum - _minimum) / 10);

    public event EventHandler? ValueChanged;

    private void SetValue(int value)
    {
        value = Math.Clamp(value, _minimum, _maximum);
        if (_value == value) return;
        _value = value;
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private float Scaled(float value) => value * DeviceDpi / 96f;
    private float TrackLeft => Scaled(12);
    private float TrackRight => Math.Max(TrackLeft, ClientSize.Width - Scaled(12));

    private void SetFromPosition(int x)
    {
        var span = TrackRight - TrackLeft;
        var fraction = span > 0 ? Math.Clamp((x - TrackLeft) / span, 0, 1) : 0;
        SetValue((int)Math.Clamp(Math.Round(_minimum + fraction * ((double)_maximum - _minimum)), int.MinValue, int.MaxValue));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromPosition(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && Capture) SetFromPosition(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_dragging) return;
        SetFromPosition(e.X);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        _dragging = false;
        base.OnMouseCaptureChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        long next = _value;
        var small = Math.Max(1, SmallChange);
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down: next -= small; break;
            case Keys.Right:
            case Keys.Up: next += small; break;
            case Keys.Home: next = _minimum; break;
            case Keys.End: next = _maximum; break;
            case Keys.PageDown: next -= LargeChange; break;
            case Keys.PageUp: next += LargeChange; break;
            default: return;
        }
        SetValue((int)Math.Clamp(next, _minimum, _maximum));
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < Scaled(26) || ClientSize.Height < Scaled(14)) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var y = ClientSize.Height / 2f;
        var fraction = _maximum == _minimum ? 0 : (_value - (double)_minimum) / (_maximum - (double)_minimum);
        var thumbX = TrackLeft + (float)fraction * (TrackRight - TrackLeft);
        using var track = new Pen(UiTheme.Border, Scaled(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progress = new Pen(Enabled ? ForeColor : UiTheme.Muted, Scaled(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, TrackLeft, y, TrackRight, y);
        if (thumbX > TrackLeft) e.Graphics.DrawLine(progress, TrackLeft, y, thumbX, y);
        var diameter = Scaled(_dragging ? 16 : 14);
        using var thumb = new SolidBrush(!Enabled ? UiTheme.Muted : _hovered || Focused ? UiTheme.AccentHover : ForeColor);
        e.Graphics.FillEllipse(thumb, thumbX - diameter / 2, y - diameter / 2, diameter, diameter);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(UiTheme.Accent, Scaled(1)) { DashStyle = DashStyle.Dot };
            using var outline = UiTheme.RoundedRectangle(new RectangleF(Scaled(2), Scaled(2), ClientSize.Width - Scaled(4), ClientSize.Height - Scaled(4)), Scaled(6));
            e.Graphics.DrawPath(focus, outline);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

    private sealed class SliderAccessibleObject : ControlAccessibleObject
    {
        private readonly AccentSlider _slider;
        public SliderAccessibleObject(AccentSlider slider) : base(slider) => _slider = slider;
        public override AccessibleRole Role => AccessibleRole.Slider;
        public override string? Value
        {
            get => _slider.Value.ToString(CultureInfo.CurrentCulture);
            set
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
                    _slider.Value = parsed;
            }
        }
    }
}
