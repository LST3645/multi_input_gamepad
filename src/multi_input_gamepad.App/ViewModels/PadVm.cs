using System.ComponentModel;
using MultiInputGamepad.Input;
using MultiInputGamepad.Output;

namespace MultiInputGamepad.App.ViewModels;

/// <summary>一个手柄面板的可视化状态（输入端 / 输出端共用）。</summary>
public sealed class PadVm : INotifyPropertyChanged
{
    private const double StickRadius = 26; // 摇杆圆点最大偏移（像素）

    public string Title { get; }

    public PadVm(string title) => Title = title;

    private string _footer = "";
    public string Footer { get => _footer; private set => Set(ref _footer, value); }

    private bool _a; public bool A { get => _a; private set => Set(ref _a, value); }
    private bool _b; public bool B { get => _b; private set => Set(ref _b, value); }
    private bool _x; public bool X { get => _x; private set => Set(ref _x, value); }
    private bool _y; public bool Y { get => _y; private set => Set(ref _y, value); }
    private bool _lb; public bool LB { get => _lb; private set => Set(ref _lb, value); }
    private bool _rb; public bool RB { get => _rb; private set => Set(ref _rb, value); }
    private bool _l3; public bool L3 { get => _l3; private set => Set(ref _l3, value); }
    private bool _r3; public bool R3 { get => _r3; private set => Set(ref _r3, value); }
    private bool _start; public bool Start { get => _start; private set => Set(ref _start, value); }
    private bool _back; public bool Back { get => _back; private set => Set(ref _back, value); }
    private bool _dUp; public bool DUp { get => _dUp; private set => Set(ref _dUp, value); }
    private bool _dDown; public bool DDown { get => _dDown; private set => Set(ref _dDown, value); }
    private bool _dLeft; public bool DLeft { get => _dLeft; private set => Set(ref _dLeft, value); }
    private bool _dRight; public bool DRight { get => _dRight; private set => Set(ref _dRight, value); }

    private double _leftTrigger; public double LeftTrigger { get => _leftTrigger; private set => Set(ref _leftTrigger, value); }
    private double _rightTrigger; public double RightTrigger { get => _rightTrigger; private set => Set(ref _rightTrigger, value); }

    private string _leftTriggerText = "0%"; public string LeftTriggerText { get => _leftTriggerText; private set => Set(ref _leftTriggerText, value); }
    private string _rightTriggerText = "0%"; public string RightTriggerText { get => _rightTriggerText; private set => Set(ref _rightTriggerText, value); }

    private double _lDotX; public double LStickDotX { get => _lDotX; private set => Set(ref _lDotX, value); }
    private double _lDotY; public double LStickDotY { get => _lDotY; private set => Set(ref _lDotY, value); }
    private double _rDotX; public double RStickDotX { get => _rDotX; private set => Set(ref _rDotX, value); }
    private double _rDotY; public double RStickDotY { get => _rDotY; private set => Set(ref _rDotY, value); }

    /// <summary>摇杆区域形状：true = 方形（关归一化），false = 圆形（开归一化，默认）。仅输出端会改。</summary>
    private bool _squareSticks;
    public bool SquareSticks
    {
        get => _squareSticks;
        set
        {
            if (_squareSticks == value) return;
            _squareSticks = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SquareSticks)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCircular)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSquare)));
        }
    }

    public bool IsCircular => !_squareSticks;
    public bool IsSquare => _squareSticks;

    private string _lStickText = "(0.00, 0.00)"; public string LStickText { get => _lStickText; private set => Set(ref _lStickText, value); }
    private string _rStickText = "(0.00, 0.00)"; public string RStickText { get => _rStickText; private set => Set(ref _rStickText, value); }

    // 文本只在数值真正变化时重新格式化（避免每帧 4 次字符串分配）
    private int _lastLtPercent = -1, _lastRtPercent = -1;
    private float _lastLx = float.NaN, _lastLy = float.NaN, _lastRx = float.NaN, _lastRy = float.NaN;

    /// <summary>输入端：物理手柄快照。</summary>
    public void Show(in GamepadSnapshot s)
    {
        var b = s.Buttons;
        A = b.HasFlag(XInputButtons.A);
        B = b.HasFlag(XInputButtons.B);
        X = b.HasFlag(XInputButtons.X);
        Y = b.HasFlag(XInputButtons.Y);
        LB = b.HasFlag(XInputButtons.LeftShoulder);
        RB = b.HasFlag(XInputButtons.RightShoulder);
        L3 = b.HasFlag(XInputButtons.LeftThumb);
        R3 = b.HasFlag(XInputButtons.RightThumb);
        Start = b.HasFlag(XInputButtons.Start);
        Back = b.HasFlag(XInputButtons.Back);
        DUp = b.HasFlag(XInputButtons.DPadUp);
        DDown = b.HasFlag(XInputButtons.DPadDown);
        DLeft = b.HasFlag(XInputButtons.DPadLeft);
        DRight = b.HasFlag(XInputButtons.DPadRight);
        ApplyAxes(s.LeftTrigger, s.RightTrigger, s.LeftX, s.LeftY, s.RightX, s.RightY);
        Footer = "";
    }

    /// <summary>输出端：实际提交给虚拟手柄的状态。</summary>
    public void Show(in VirtualGamepadState s)
    {
        A = s.A;
        B = s.B;
        X = s.X;
        Y = s.Y;
        LB = s.LeftBumper;
        RB = s.RightBumper;
        L3 = s.LeftStick;
        R3 = s.RightStick;
        Start = s.Start;
        Back = s.Back;
        DUp = s.DPadUp;
        DDown = s.DPadDown;
        DLeft = s.DPadLeft;
        DRight = s.DPadRight;
        ApplyAxes(s.LeftTrigger, s.RightTrigger, s.LeftX, s.LeftY, s.RightX, s.RightY);
        Footer = "";
    }

    /// <summary>无数据（未连接 / 输出未启动）。</summary>
    public void Clear(string footer)
    {
        A = B = X = Y = LB = RB = L3 = R3 = Start = Back = false;
        DUp = DDown = DLeft = DRight = false;
        ApplyAxes(0, 0, 0, 0, 0, 0);
        Footer = footer;
    }

    private void ApplyAxes(float lt, float rt, float lx, float ly, float rx, float ry)
    {
        LeftTrigger = lt;
        RightTrigger = rt;
        LStickDotX = lx * StickRadius;
        LStickDotY = -ly * StickRadius;
        RStickDotX = rx * StickRadius;
        RStickDotY = -ry * StickRadius;

        int ltPercent = (int)Math.Round(lt * 100);
        if (ltPercent != _lastLtPercent)
        {
            LeftTriggerText = $"{ltPercent}%";
            _lastLtPercent = ltPercent;
        }

        int rtPercent = (int)Math.Round(rt * 100);
        if (rtPercent != _lastRtPercent)
        {
            RightTriggerText = $"{rtPercent}%";
            _lastRtPercent = rtPercent;
        }

        float lxq = MathF.Round(lx, 2), lyq = MathF.Round(ly, 2);
        if (lxq != _lastLx || lyq != _lastLy)
        {
            LStickText = $"({lxq:0.00}, {lyq:0.00})";
            _lastLx = lxq;
            _lastLy = lyq;
        }

        float rxq = MathF.Round(rx, 2), ryq = MathF.Round(ry, 2);
        if (rxq != _lastRx || ryq != _lastRy)
        {
            RStickText = $"({rxq:0.00}, {ryq:0.00})";
            _lastRx = rxq;
            _lastRy = ryq;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
