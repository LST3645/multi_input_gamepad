using System.ComponentModel;

namespace MultiInput.App.ViewModels;

/// <summary>鼠标可视化状态：左/右/中/侧键、滚轮增量、平滑后的位移向量。</summary>
public sealed class MouseVm : INotifyPropertyChanged
{
    public const double CanvasSize = 112;          // 位移盘直径
    public const double Center = CanvasSize / 2;   // 中心坐标
    private const double MaxOffset = 44;           // 圆点最大偏移
    private const double Smooth = 0.35;            // 位移平滑系数
    private const int HoldFrames = 6;              // 滚轮高亮/读数保持帧数（≈200ms @30fps）

    private double _sx, _sy;      // 平滑位移（像素/帧）
    private int _upHold, _downHold, _textHold;
    private int _lastWheel;       // 最近一次非零滚轮增量（原始值）
    private int _lastDx, _lastDy;  // 与上次显示相同则跳过字符串格式化
    private int _shownWheel = int.MinValue;

    private bool _left; public bool Left { get => _left; private set => Set(ref _left, value); }
    private bool _right; public bool Right { get => _right; private set => Set(ref _right, value); }
    private bool _middle; public bool Middle { get => _middle; private set => Set(ref _middle, value); }
    private bool _x1; public bool X1 { get => _x1; private set => Set(ref _x1, value); }
    private bool _x2; public bool X2 { get => _x2; private set => Set(ref _x2, value); }

    // —— 已参与映射的输入（可视化描边）——
    private bool _leftMapped; public bool LeftMapped { get => _leftMapped; private set => Set(ref _leftMapped, value); }
    private bool _rightMapped; public bool RightMapped { get => _rightMapped; private set => Set(ref _rightMapped, value); }
    private bool _middleMapped; public bool MiddleMapped { get => _middleMapped; private set => Set(ref _middleMapped, value); }
    private bool _x1Mapped; public bool X1Mapped { get => _x1Mapped; private set => Set(ref _x1Mapped, value); }
    private bool _x2Mapped; public bool X2Mapped { get => _x2Mapped; private set => Set(ref _x2Mapped, value); }
    private bool _wheelUpMapped; public bool WheelUpMapped { get => _wheelUpMapped; private set => Set(ref _wheelUpMapped, value); }
    private bool _wheelDownMapped; public bool WheelDownMapped { get => _wheelDownMapped; private set => Set(ref _wheelDownMapped, value); }

    /// <summary>更新「已参与映射」标记（鼠标按键编号 1..5；滚轮按方向）。</summary>
    public void SetMapped(IReadOnlySet<int> buttons, bool wheelUp, bool wheelDown)
    {
        LeftMapped = buttons.Contains(1);
        RightMapped = buttons.Contains(2);
        MiddleMapped = buttons.Contains(3);
        X1Mapped = buttons.Contains(4);
        X2Mapped = buttons.Contains(5);
        WheelUpMapped = wheelUp;
        WheelDownMapped = wheelDown;
    }

    /// <summary>滚轮上滚（顶部三角形高亮）。</summary>
    private bool _wheelUp; public bool WheelUp { get => _wheelUp; private set => Set(ref _wheelUp, value); }

    /// <summary>滚轮下滚（底部三角形高亮）。</summary>
    private bool _wheelDown; public bool WheelDown { get => _wheelDown; private set => Set(ref _wheelDown, value); }

    private string _wheelText = "滚轮 0";
    public string WheelText { get => _wheelText; private set => Set(ref _wheelText, value); }

    private string _moveText = "Δ(0, 0)";
    public string MoveText { get => _moveText; private set => Set(ref _moveText, value); }

    private double _dotX; public double DotX { get => _dotX; private set => Set(ref _dotX, value); }
    private double _dotY; public double DotY { get => _dotY; private set => Set(ref _dotY, value); }

    private double _lineX2 = Center; public double LineX2 { get => _lineX2; private set => Set(ref _lineX2, value); }
    private double _lineY2 = Center; public double LineY2 { get => _lineY2; private set => Set(ref _lineY2, value); }

    /// <summary>
    /// 每帧调用（UI 线程，约 30 Hz）。dx/dy 为自上次调用以来的累计位移，
    /// wheel 为累计滚轮增量（原始值，120 = 一格）。
    /// </summary>
    public void Update(bool left, bool right, bool middle, bool x1, bool x2, int dx, int dy, int wheel)
    {
        Left = left;
        Right = right;
        Middle = middle;
        X1 = x1;
        X2 = x2;

        // 滚轮：直接显示原始增量（保持若干帧便于阅读），上下三角对应滚动方向
        if (wheel > 0) { _upHold = HoldFrames; _downHold = 0; }
        else if (wheel < 0) { _downHold = HoldFrames; _upHold = 0; }
        if (wheel != 0) { _lastWheel = wheel; _textHold = HoldFrames; }

        WheelUp = _upHold > 0;
        WheelDown = _downHold > 0;
        if (_upHold > 0) _upHold--;
        if (_downHold > 0) _downHold--;

        int shownWheel = _textHold > 0 ? _lastWheel : 0;
        if (shownWheel != _shownWheel)
        {
            WheelText = shownWheel == 0 ? "滚轮 0" : $"滚轮 {shownWheel:+#;-#;0}";
            _shownWheel = shownWheel;
        }
        if (_textHold > 0) _textHold--;

        // 位移：指数平滑，静止时自动回中
        _sx = _sx * (1 - Smooth) + dx * Smooth;
        _sy = _sy * (1 - Smooth) + dy * Smooth;
        double ox = Math.Clamp(_sx, -MaxOffset, MaxOffset);
        double oy = Math.Clamp(_sy, -MaxOffset, MaxOffset);
        DotX = ox;
        DotY = oy;
        LineX2 = Center + ox;
        LineY2 = Center + oy;

        if (dx != _lastDx || dy != _lastDy)
        {
            MoveText = $"Δ({dx}, {dy})";
            _lastDx = dx;
            _lastDy = dy;
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
