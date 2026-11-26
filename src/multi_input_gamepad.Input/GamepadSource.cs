namespace MultiInputGamepad.Input;

/// <summary>归一化手柄快照。</summary>
public readonly record struct GamepadSnapshot(
    XInputButtons Buttons,
    float LeftTrigger,
    float RightTrigger,
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    uint PacketNumber);

/// <summary>轮询 XInput 读取物理手柄（Elite 2），提供归一化状态与震动输出。</summary>
public sealed class GamepadSource
{
    public uint Index { get; set; }

    public GamepadSource(uint index = 0) => Index = index;

    public bool Connected { get; private set; }

    /// <summary>返回第一个已连接的 XInput 槽位（0–3），无则 -1。</summary>
    public static int FindConnectedIndex()
    {
        var state = new XInputState();
        for (uint i = 0; i < 4; i++)
        {
            if (XInput.GetState(i, ref state) == XInput.ErrorSuccess)
                return (int)i;
        }
        return -1;
    }

    /// <summary>读取并归一化。未连接返回 null。</summary>
    public GamepadSnapshot? Poll()
    {
        var state = new XInputState();
        uint r = XInput.GetState(Index, ref state);
        if (r != XInput.ErrorSuccess)
        {
            Connected = false;
            return null;
        }

        Connected = true;
        var g = state.Gamepad;
        return new GamepadSnapshot(
            (XInputButtons)g.wButtons,
            g.bLeftTrigger / 255f,
            g.bRightTrigger / 255f,
            Normalize(g.sThumbLX),
            Normalize(g.sThumbLY),
            Normalize(g.sThumbRX),
            Normalize(g.sThumbRY),
            state.dwPacketNumber);
    }

    /// <summary>设置震动（左右主电机，0..1）。</summary>
    public void SetVibration(float left, float right)
    {
        var vib = new XInputVibration
        {
            wLeftMotorSpeed = (ushort)(Math.Clamp(left, 0f, 1f) * 65535),
            wRightMotorSpeed = (ushort)(Math.Clamp(right, 0f, 1f) * 65535),
        };
        XInput.SetState(Index, ref vib);
    }

    private static float Normalize(short v) => v < 0 ? v / 32768f : v / 32767f;
}
