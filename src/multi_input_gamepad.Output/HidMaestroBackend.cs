using HIDMaestro;

namespace MultiInputGamepad.Output;

/// <summary>
/// HIDMaestro 输出后端：呈现 Xbox Series（xbox-series-xs-bt，045E:0B13）。
/// 内部 [-1,1] 摇杆 / [0,1] 扳机 → HIDMaestro [0,1] 均匀轴（0.5=中位）。
/// </summary>
public sealed class HidMaestroBackend : IVirtualController
{
    public const string DefaultProfileId = "xbox-series-xs-bt";

    private HMContext? _ctx;
    private HMController? _pad;

    public event Action<byte, byte, int>? Rumble;

    /// <summary>清理所有残留/幻影虚拟手柄（含上次 Ctrl+C 未清理的）。需管理员。</summary>
    public static void CleanupResidual()
    {
        HMContext.RemoveAllVirtualControllers(HMCleanupScope.Everything, preserveInstall: true);
    }

    /// <summary>加载目录、安装驱动、创建虚拟手柄。需管理员权限。</summary>
    public void Start(string profileId = DefaultProfileId, string? identityKey = null)
    {
        _ctx = new HMContext();
        _ctx.LoadDefaultProfiles();
        _ctx.InstallDriver();

        var profile = _ctx.GetProfile(profileId)
            ?? throw new InvalidOperationException($"profile not found: {profileId}");

        _pad = _ctx.CreateController(profile, identityKey ?? "multiinput-0");
        _pad.OutputReceived += OnOutput;
    }

    private void OnOutput(HMController sender, HMOutputPacket packet)
    {

        if (TryDecodeRumble(packet.Source, packet.ReportId, packet.Data, out byte left, out byte right, out int durationMs))
            Rumble?.Invoke(left, right, durationMs);
    }

    /// <summary>XInput 源是电平量（无时长字段），重复下发续期；给个保守的失效窗口。</summary>
    private const int LevelRumbleMs = 250;

    /// <summary>
    /// 解码主机发来的震动包。
    /// Xbox Series 走 xinputhid，震动以 HID 输出报告 0x0F 到达，载荷 7 字节：
    /// <c>[0]=LT [1]=RT [2]=左主电机 [3]=右主电机 [4]=0xFF 常量 [5]=重复次数 [6]=时长(10ms)</c>。
    /// 对照 SDL 的 GIP 包 <c>{09 00 00 09 00 0F 00 00 LF HF FF 00 DUR}</c> 去掉报告 ID 即得。
    /// <para>
    /// 关键：<c>[4]</c> 的 0xFF 是常量标记，**不是电机值**。把它当左电机读，会让设备枚举/初始化包
    /// （实测 <c>00 00 00 00 FF 00 EB</c>，电机其实全 0）变成满幅左电机震动。
    /// </para>
    /// </summary>
    internal static bool TryDecodeRumble(HMOutputSource source, byte reportId, ReadOnlyMemory<byte> data,
        out byte left, out byte right, out int durationMs)
    {
        left = 0;
        right = 0;
        durationMs = 0;
        var span = data.Span;

        // Xbox 360（XUSB companion）：5 字节电平量，[2]=左、[3]=右，无时长字段。
        if (source == HMOutputSource.XInput)
        {
            if (span.Length < 4) return false;

            left = span[2];
            right = span[3];
            durationMs = LevelRumbleMs;
            return true;
        }

        if (source == HMOutputSource.HidOutput && reportId == 0x0F && span.Length >= 7)
        {
            left = span[2];
            right = span[3];
            durationMs = span[6] * 10;
            return true;
        }

        return false;
    }

    public void Submit(in VirtualGamepadState s)
    {
        if (_pad is null) return;

        var state = new HMGamepadState
        {
            Axes = HMGamepadStateHelpers.StandardAxes(_pad.Profile,
                // XInput：右/上为正。HID 轴：0 = 左/上，满值 = 右/下。
                // 所以 X 是线性映射，Y 必须翻转，否则虚拟手柄的纵向是反的。
                leftStickX:  (s.LeftX + 1f) / 2f,
                leftStickY:  (1f - s.LeftY) / 2f,
                rightStickX: (s.RightX + 1f) / 2f,
                rightStickY: (1f - s.RightY) / 2f,
                leftTrigger:  s.LeftTrigger,
                rightTrigger: s.RightTrigger),
            Buttons = ToButtons(s),
            Hat = ToHat(s),
        };

        _pad.SubmitState(in state);
    }

    private static HMButton ToButtons(in VirtualGamepadState s)
    {
        HMButton b = HMButton.None;
        if (s.A) b |= HMButton.A;
        if (s.B) b |= HMButton.B;
        if (s.X) b |= HMButton.X;
        if (s.Y) b |= HMButton.Y;
        if (s.LeftBumper) b |= HMButton.LeftBumper;
        if (s.RightBumper) b |= HMButton.RightBumper;
        if (s.Guide) b |= HMButton.Guide;
        // TODO(M0): 确认 L3/R3、Start/Back 的 HMButton 成员名（编译时修正）。
        if (s.LeftStick) b |= HMButton.LeftStick;
        if (s.RightStick) b |= HMButton.RightStick;
        if (s.Start) b |= HMButton.Start;
        if (s.Back) b |= HMButton.Back;
        return b;
    }

    private static HMHat ToHat(in VirtualGamepadState s)
    {
        if (s.DPadUp && s.DPadRight) return HMHat.NorthEast;
        if (s.DPadUp && s.DPadLeft) return HMHat.NorthWest;
        if (s.DPadDown && s.DPadRight) return HMHat.SouthEast;
        if (s.DPadDown && s.DPadLeft) return HMHat.SouthWest;
        if (s.DPadUp) return HMHat.North;
        if (s.DPadDown) return HMHat.South;
        if (s.DPadLeft) return HMHat.West;
        if (s.DPadRight) return HMHat.East;
        return HMHat.None;
    }

    public void Dispose()
    {
        if (_pad is not null)
        {
            _pad.OutputReceived -= OnOutput;
            _pad.Dispose();
            _pad = null;
        }
        _ctx?.Dispose();
        _ctx = null;
    }
}
