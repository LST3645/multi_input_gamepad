namespace MultiInputGamepad.Core;

/// <summary>
/// 手柄输入源：14 个按键 + LT/RT 各 4 档（每档当作独立按键）+ 左右摇杆各 4 个方向。
/// <para>前 14 项与 <see cref="GamepadButton"/> 的位序一一对应（0–13），两处改动必须同步。</para>
/// </summary>
public enum GamepadInput
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    LeftBumper = 4,
    RightBumper = 5,
    LeftStick = 6,   // L3
    RightStick = 7,  // R3
    Start = 8,
    Back = 9,
    DPadUp = 10,
    DPadDown = 11,
    DPadLeft = 12,
    DPadRight = 13,

    // —— 扳机档位：25% / 50% / 75% / 100%。档位是阈值，按得越深，越低的档位也同时成立 ——
    LeftTrigger25 = 14,
    LeftTrigger50 = 15,
    LeftTrigger75 = 16,
    LeftTrigger100 = 17,
    RightTrigger25 = 18,
    RightTrigger50 = 19,
    RightTrigger75 = 20,
    RightTrigger100 = 21,

    // —— 摇杆方向：按扇区判定（见 <see cref="StickSectors"/>），不是按键 ——
    LeftStickUp = 22,
    LeftStickDown = 23,
    LeftStickLeft = 24,
    LeftStickRight = 25,
    RightStickUp = 26,
    RightStickDown = 27,
    RightStickLeft = 28,
    RightStickRight = 29,
}

/// <summary>手柄输入源的分类与换算。</summary>
public static class GamepadInputs
{
    /// <summary>每根扳机的档位数（25% 一档）。</summary>
    public const int TriggerLevels = 4;

    /// <summary>按键类来源 → 位标志；扳机档位与摇杆方向不占位，返回 <see cref="GamepadButton.None"/>。</summary>
    public static GamepadButton ToButton(GamepadInput input) => input switch
    {
        GamepadInput.A => GamepadButton.A,
        GamepadInput.B => GamepadButton.B,
        GamepadInput.X => GamepadButton.X,
        GamepadInput.Y => GamepadButton.Y,
        GamepadInput.LeftBumper => GamepadButton.LeftBumper,
        GamepadInput.RightBumper => GamepadButton.RightBumper,
        GamepadInput.LeftStick => GamepadButton.LeftStick,
        GamepadInput.RightStick => GamepadButton.RightStick,
        GamepadInput.Start => GamepadButton.Start,
        GamepadInput.Back => GamepadButton.Back,
        GamepadInput.DPadUp => GamepadButton.DPadUp,
        GamepadInput.DPadDown => GamepadButton.DPadDown,
        GamepadInput.DPadLeft => GamepadButton.DPadLeft,
        GamepadInput.DPadRight => GamepadButton.DPadRight,
        _ => GamepadButton.None,
    };

    /// <summary>扳机档位：返回哪根扳机、第几档（1..<see cref="TriggerLevels"/>）；非档位返回 false。</summary>
    public static bool TryGetTriggerLevel(GamepadInput input, out bool left, out int level)
    {
        if (input >= GamepadInput.LeftTrigger25 && input <= GamepadInput.LeftTrigger100)
        {
            left = true;
            level = input - GamepadInput.LeftTrigger25 + 1;
            return true;
        }

        if (input >= GamepadInput.RightTrigger25 && input <= GamepadInput.RightTrigger100)
        {
            left = false;
            level = input - GamepadInput.RightTrigger25 + 1;
            return true;
        }

        left = false;
        level = 0;
        return false;
    }

    /// <summary>反查：某根扳机第 <paramref name="level"/> 档（1..<see cref="TriggerLevels"/>）的来源值。</summary>
    public static GamepadInput TriggerLevel(bool left, int level)
    {
        int index = Math.Clamp(level, 1, TriggerLevels) - 1;
        return left
            ? (GamepadInput)((int)GamepadInput.LeftTrigger25 + index)
            : (GamepadInput)((int)GamepadInput.RightTrigger25 + index);
    }

    /// <summary>
    /// 摇杆方向：返回哪根摇杆、扇区中心角（数学约定：0°=右、90°=上、逆时针）；
    /// 非摇杆方向返回 false。
    /// </summary>
    public static bool TryGetStickDirection(GamepadInput input, out bool left, out float centerDegrees)
    {
        switch (input)
        {
            case GamepadInput.LeftStickUp: left = true; centerDegrees = 90f; return true;
            case GamepadInput.LeftStickDown: left = true; centerDegrees = -90f; return true;
            case GamepadInput.LeftStickLeft: left = true; centerDegrees = 180f; return true;
            case GamepadInput.LeftStickRight: left = true; centerDegrees = 0f; return true;
            case GamepadInput.RightStickUp: left = false; centerDegrees = 90f; return true;
            case GamepadInput.RightStickDown: left = false; centerDegrees = -90f; return true;
            case GamepadInput.RightStickLeft: left = false; centerDegrees = 180f; return true;
            case GamepadInput.RightStickRight: left = false; centerDegrees = 0f; return true;
            default: left = false; centerDegrees = 0f; return false;
        }
    }

    /// <summary>是否摇杆方向来源（摇杆不参与「停止透传」，按键与扳机才停）。</summary>
    public static bool IsStickDirection(GamepadInput input) =>
        input >= GamepadInput.LeftStickUp && input <= GamepadInput.RightStickRight;

    /// <summary>是否扳机档位来源（任意档位有规则 → 整根扳机停止透传）。</summary>
    public static bool IsTriggerLevel(GamepadInput input) =>
        input >= GamepadInput.LeftTrigger25 && input <= GamepadInput.RightTrigger100;
}

/// <summary>一拍的手柄输入快照（Core 侧镜像，避免 Core 依赖 Input/Output 程序集）。</summary>
public readonly record struct GamepadInputState(
    GamepadButton Buttons,
    float LeftTrigger,
    float RightTrigger,
    float LeftX,
    float LeftY,
    float RightX,
    float RightY);

/// <summary>
/// 摇杆方向扇区判定。
/// <para>每个象限 90° 拆成「本方向独立区（<see cref="StickMappingConfig.SectorDegrees"/>，默认 70°）」
/// 与两侧各一半的「共同区」（默认各 10°）。共同区同时属于相邻两个方向：摇杆指向上右偏 40° 时，
/// 「左X+」「左Y+」两条规则都成立。等价判定：方向 D 成立 ⇔ 与 D 的中心角相差 ≤ 90° − 扇区/2。</para>
/// <para>位移为**径向**：<c>√(x²+y²) &gt; 阈值</c> 才算按下，默认 0.25。</para>
/// <para>摇杆是否透传与扇区无关：只要该摇杆有任意一个方向参与映射，整根摇杆就停止透传。</para>
/// </summary>
public static class StickSectors
{
    /// <summary>方向 D 的成立半角（度）：独立区一半 + 共同区。<c>70° → 55°</c>，<c>90° → 45°</c>。</summary>
    public static float PressHalfWidthDegrees(float sectorDegrees) => 90f - sectorDegrees / 2f;

    /// <summary>摇杆位置是否落在某方向的按下区（角度按数学约定：0°=右、90°=上）。</summary>
    public static bool IsPressed(float x, float y, float threshold, float sectorDegrees, float centerDegrees)
    {
        if (x * x + y * y <= threshold * threshold) return false;

        float angle = MathF.Atan2(y, x) * (180f / MathF.PI);
        return MathF.Abs(AngleDelta(angle, centerDegrees)) <= PressHalfWidthDegrees(sectorDegrees);
    }

    /// <summary>两个角度之差，归一到 (−180, 180]。</summary>
    public static float AngleDelta(float a, float b)
    {
        float delta = (a - b) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta <= -180f) delta += 360f;
        return delta;
    }
}

/// <summary>摇杆 → 按键 的判定参数（配置级；一套配置一份）。</summary>
public sealed class StickMappingConfig
{
    /// <summary>按下阈值：径向位移严格大于该值才算按下。</summary>
    public float PressThreshold { get; set; } = 0.25f;

    /// <summary>方向独立区角度（度）；两侧共同区各占 (90 − 本值)/2。</summary>
    public float SectorDegrees { get; set; } = 70f;

    public static StickMappingConfig Default => new();

    /// <summary>夹到合理区间：扇区越接近 90°，共同区越窄；小于 20° 时四向几乎总是同时成立。</summary>
    public void Sanitize()
    {
        PressThreshold = Math.Clamp(PressThreshold, 0.05f, 0.95f);
        SectorDegrees = Math.Clamp(SectorDegrees, 20f, 90f);
    }
}
