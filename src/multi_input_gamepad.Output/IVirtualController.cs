namespace MultiInputGamepad.Output;

/// <summary>归一化虚拟手柄状态（内部约定：摇杆 [-1,1]，扳机 [0,1]）。</summary>
public struct VirtualGamepadState
{
    public bool A, B, X, Y;
    public bool DPadUp, DPadDown, DPadLeft, DPadRight;
    public bool LeftBumper, RightBumper;
    public bool LeftStick, RightStick;   // L3 / R3
    public bool Start, Back;
    public bool Guide;
    public float LeftX, LeftY, RightX, RightY;   // [-1,1]
    public float LeftTrigger, RightTrigger;      // [0,1]
}

/// <summary>虚拟手柄输出抽象。</summary>
public interface IVirtualController : IDisposable
{
    void Submit(in VirtualGamepadState state);

    /// <summary>
    /// 游戏发来的震动：左/右主电机 0..255，持续 <paramref name="durationMs"/> 毫秒。
    /// Xbox One/Series 的 HID 震动包是**一次性脉冲**（时长编码在包里，由设备固件到期自动停），
    /// 不是电平量；丢掉时长就会把一次脉冲变成永久震动。
    /// </summary>
    event Action<byte, byte, int>? Rumble;
}
