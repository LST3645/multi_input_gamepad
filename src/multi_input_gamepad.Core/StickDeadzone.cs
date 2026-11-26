namespace MultiInputGamepad.Core;

/// <summary>
/// 物理摇杆死区参数（从 `settings.json` 的 `Deadzone` 段读取；缺省时用这里的默认值）。
/// </summary>
public sealed class StickDeadzoneConfig
{
    /// <summary>出死区半径：<c>r &gt; RadiusOn</c> 立即输出原值。</summary>
    public float RadiusOn { get; set; } = 0.12f;

    /// <summary>回死区半径（滞回，应 ≤ <see cref="RadiusOn"/>）。</summary>
    public float RadiusOff { get; set; } = 0.07f;

    /// <summary>回中确认包数。</summary>
    public int OutConfirmPackets { get; set; } = 3;

    /// <summary>死区内同向推进的确认包数。</summary>
    public int MoveStreakPackets { get; set; } = 3;

    /// <summary>单包最小位移（低于此值视为噪声，不计入确认）。</summary>
    public float MinStep { get; set; } = 0.02f;

    /// <summary>确认序列的最小累计净位移（防止原地抖动误触发）。</summary>
    public float MinAccum { get; set; } = 0.04f;

    public static StickDeadzoneConfig Default => new();

    /// <summary>把参数夹到合理区间，避免手改配置写出失效状态机。</summary>
    public void Sanitize()
    {
        RadiusOn = Math.Clamp(RadiusOn, 0f, 1f);
        RadiusOff = Math.Clamp(RadiusOff, 0f, RadiusOn);   // 滞回：回中半径不得超过出死区半径
        OutConfirmPackets = Math.Clamp(OutConfirmPackets, 1, 60);
        MoveStreakPackets = Math.Clamp(MoveStreakPackets, 1, 60);
        MinStep = Math.Clamp(MinStep, 0f, 1f);
        MinAccum = Math.Clamp(MinAccum, 0f, 1f);
    }
}

/// <summary>
/// 单根物理摇杆的死区状态（按「新 XInput 包」推进，同一包不得重复调用）。
/// </summary>
public struct StickDeadzoneState
{
    /// <summary>当前是否处于死区外（输出原值）。</summary>
    public bool Live;

    /// <summary>回中确认计数：Live 时连续落入 R_OFF 内的包数。</summary>
    public int OutConfirm;

    /// <summary>同向位移连续包数。</summary>
    public int MoveStreak;

    /// <summary>是否已有上一包位置。</summary>
    public bool HasLast;

    public float LastX, LastY;

    /// <summary>确认序列的起点（用于累计净位移判定）。</summary>
    public float AnchorX, AnchorY;

    /// <summary>上一包的位移向量（用于同向判定）。</summary>
    public float PrevDx, PrevDy;

    /// <summary>是否已有一个可比较的上一包位移。</summary>
    public bool StreakValid;
}

/// <summary>
/// 物理摇杆径向死区（漂移补偿 + 死区内小幅推进确认）。
/// <para>只作用于**输入端手柄**的左右摇杆；映射合成值（键盘方向目标 / 鼠标轴）不套死区。</para>
/// <para>状态机：<c>r &gt; RadiusOn</c> 立即出死区；死区内连续 <c>MoveStreakPackets</c> 包
/// 同向位移（单包 ≥ <c>MinStep</c>）且累计净位移 ≥ <c>MinAccum</c> 也视为真实输入。
/// Live 后 <c>r ≤ RadiusOff</c> 连续 <c>OutConfirmPackets</c> 包才判定回中（滞回）。</para>
/// </summary>
public static class StickDeadzone
{
    /// <summary>推进一个 XInput 包并返回该摇杆的输出（参数由配置提供）。</summary>
    public static (float X, float Y) Apply(in StickDeadzoneConfig config, ref StickDeadzoneState state, float x, float y)
    {
        float radiusOnSquared = config.RadiusOn * config.RadiusOn;
        float radiusOffSquared = config.RadiusOff * config.RadiusOff;
        float minStepSquared = config.MinStep * config.MinStep;
        float minAccumSquared = config.MinAccum * config.MinAccum;

        float r2 = x * x + y * y;

        if (state.Live)
        {
            if (r2 <= radiusOffSquared)
            {
                if (++state.OutConfirm >= config.OutConfirmPackets)
                {
                    state.Live = false;
                    state.OutConfirm = 0;
                    ResetStreak(ref state);
                }
            }
            else
            {
                state.OutConfirm = 0;
            }
        }
        else if (r2 > radiusOnSquared)
        {
            state.Live = true;
            state.OutConfirm = 0;
            ResetStreak(ref state);
        }
        else if (state.HasLast)
        {
            // 死区内：识别「同向连续推进」，放行小于阈值但有明确意图的慢推
            float dx = x - state.LastX;
            float dy = y - state.LastY;

            if (dx * dx + dy * dy >= minStepSquared)
            {
                bool sameDirection = state.StreakValid && (dx * state.PrevDx + dy * state.PrevDy) > 0f;
                if (sameDirection)
                {
                    state.MoveStreak++;
                }
                else
                {
                    state.MoveStreak = 1;
                    state.AnchorX = state.LastX;
                    state.AnchorY = state.LastY;
                }

                state.PrevDx = dx;
                state.PrevDy = dy;
                state.StreakValid = true;

                if (state.MoveStreak >= config.MoveStreakPackets)
                {
                    float ax = x - state.AnchorX;
                    float ay = y - state.AnchorY;
                    if (ax * ax + ay * ay >= minAccumSquared)
                    {
                        state.Live = true;
                        state.OutConfirm = 0;
                        ResetStreak(ref state);
                    }
                }
            }
            else
            {
                ResetStreak(ref state);
            }
        }

        state.HasLast = true;
        state.LastX = x;
        state.LastY = y;

        return state.Live ? (x, y) : (0f, 0f);
    }

    private static void ResetStreak(ref StickDeadzoneState state)
    {
        state.MoveStreak = 0;
        state.StreakValid = false;
    }
}
