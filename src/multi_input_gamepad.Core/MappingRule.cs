namespace MultiInputGamepad.Core;

/// <summary>映射的输入来源。</summary>
public enum MappingSource
{
    Keyboard = 0,    // KeyCode = 虚拟键码
    MouseButton = 1, // KeyCode = 1 左 / 2 右 / 3 中 / 4 X1 / 5 X2
    MouseWheel = 2,  // KeyCode = 0 上滚 / 1 下滚；持续滚动视为按住，停滚超时视为松开
    MouseAxis = 3,   // 相对位移 → 摇杆轴（AxisX / AxisY）
    Gamepad = 4,     // PadInput = 手柄按键 / 扳机档位 / 摇杆方向（物理手柄，捕获开启时生效）
}

/// <summary>摇杆轴编号（<see cref="MappingRule.AxisX"/> 等）。Y 轴向上为正。</summary>
public enum StickAxis
{
    LeftX = 0,
    LeftY = 1,
    RightX = 2,
    RightY = 3,
}

/// <summary>可映射的手柄目标：14 个按键 + 左右扳机 + 8 个摇杆方向（每根轴 ±）。</summary>
public enum GamepadTarget
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    LeftBumper = 4,
    RightBumper = 5,
    LeftStick = 6,   // L3
    RightStick = 7,  // R3
    Back = 8,        // View
    Start = 9,       // Menu
    DPadUp = 10,
    DPadDown = 11,
    DPadLeft = 12,
    DPadRight = 13,
    LeftTrigger = 14,
    RightTrigger = 15,

    // —— 摇杆方向输出：按住 = 该轴满偏 ±1 ——
    LeftXMinus = 16,
    LeftXPlus = 17,
    LeftYMinus = 18,
    LeftYPlus = 19,
    RightXMinus = 20,
    RightXPlus = 21,
    RightYMinus = 22,
    RightYPlus = 23,
}

/// <summary>一条输出：手柄目标 + （扳机的）按下百分比 + 该输出自己的延迟。</summary>
public sealed class MappingOutput
{
    public GamepadTarget Target { get; set; }

    /// <summary>扳机按下程度 1..100（按键目标恒为 100）。</summary>
    public int Percent { get; set; } = 100;

    /// <summary>该输出相对前一项的延迟（毫秒），逐项累加；0 表示与上一项同时按下。</summary>
    public int DelayMs { get; set; }
}

/// <summary>一条映射规则。JSON 序列化持久化。</summary>
public sealed class MappingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MappingSource Source { get; set; }

    // —— 输入侧 ——
    /// <summary>键盘：虚拟键码；鼠标键：1..5；滚轮：0=上滚 1=下滚；鼠标移动/手柄不适用。</summary>
    public int KeyCode { get; set; }

    /// <summary>手柄来源：具体是哪个按键 / 扳机档位 / 摇杆方向（仅 <see cref="MappingSource.Gamepad"/> 有效）。</summary>
    public GamepadInput PadInput { get; set; }

    /// <summary>鼠标移动：水平灵敏度倍率。</summary>
    public float MoveAmountX { get; set; } = 1f;

    /// <summary>鼠标移动：水平每单位位移产生的摇杆偏转。</summary>
    public float DisplacementX { get; set; } = 0.02f;

    /// <summary>鼠标移动：垂直灵敏度倍率。</summary>
    public float MoveAmountY { get; set; } = 1f;

    /// <summary>鼠标移动：垂直每单位位移产生的摇杆偏转。</summary>
    public float DisplacementY { get; set; } = 0.02f;

    /// <summary>鼠标水平位移的目标轴；-1 = 不使用（非轴向规则恒为 -1）。</summary>
    public int AxisX { get; set; } = -1;

    /// <summary>鼠标垂直位移的目标轴；-1 = 不使用。不得与 <see cref="AxisX"/> 相同。</summary>
    public int AxisY { get; set; } = -1;

    // —— 输出侧 ——
    /// <summary>按键/滚轮来源的输出列表（最多 3 个）。</summary>
    public List<MappingOutput> Outputs { get; set; } = new();

    // —— 触发 ——
    /// <summary>触发延迟（毫秒）：按下后经过该时间才输出全部目标。</summary>
    public int DelayMs { get; set; }

    /// <summary>连按：延迟到点后整组按周期脉冲。</summary>
    public bool RepeatEnabled { get; set; }
    public int RepeatIntervalMs { get; set; } = 100;

    /// <summary>输出数量上限（UI 与引擎共同遵守）。</summary>
    public const int MaxOutputs = 3;

    /// <summary>扳机档位（10 级 → 10%..100%）。</summary>
    public const int TriggerSteps = 10;
}
