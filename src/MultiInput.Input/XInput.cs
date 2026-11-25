using System.Runtime.InteropServices;

namespace MultiInput.Input;

[Flags]
public enum XInputButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputGamepad
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputVibration
{
    public ushort wLeftMotorSpeed;
    public ushort wRightMotorSpeed;
}

/// <summary>XInput（xinput1_4.dll）P/Invoke：读物理手柄、写震动。</summary>
public static class XInput
{
    private const string Dll = "xinput1_4.dll";
    public const uint ErrorSuccess = 0;
    public const uint ErrorDeviceNotConnected = 1167;

    [DllImport(Dll, EntryPoint = "XInputGetState")]
    public static extern uint GetState(uint dwUserIndex, ref XInputState pState);

    [DllImport(Dll, EntryPoint = "XInputSetState")]
    public static extern uint SetState(uint dwUserIndex, ref XInputVibration pVibration);
}
