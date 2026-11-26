namespace MultiInputGamepad.Core;

/// <summary>
/// 虚拟手柄按键位值。<see cref="MappingRule.TargetButton"/> 存的就是这些位的组合（int）。
/// 位序与 UI/输出端的 14 个按键一一对应，不含 Guide。
/// </summary>
[Flags]
public enum GamepadButton
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftBumper = 1 << 4,
    RightBumper = 1 << 5,
    LeftStick = 1 << 6,   // L3
    RightStick = 1 << 7,  // R3
    Start = 1 << 8,
    Back = 1 << 9,
    DPadUp = 1 << 10,
    DPadDown = 1 << 11,
    DPadLeft = 1 << 12,
    DPadRight = 1 << 13,
}
