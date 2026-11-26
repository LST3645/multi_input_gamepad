using System.Diagnostics;

namespace MultiInputGamepad.Core;

/// <summary>一拍求值的合成结果：按键位 + 扳机百分比 + 摇杆增量（调用方叠加到物理手柄输入上）。</summary>
public struct MappingResult
{
    public GamepadButton Buttons;
    public float LeftTrigger, RightTrigger;
    public float LeftX, LeftY, RightX, RightY;
}

/// <summary>
/// 映射规则求值引擎。
/// <para>线程约定：<see cref="OnKey"/>/<see cref="OnMouseMove"/> 等由钩子线程调用，<see cref="Evaluate"/> 由转发线程按拍调用；
/// 内部单锁串行化，规则表整体替换（不可变快照）。</para>
/// <para>时间基准统一用 <see cref="Environment.TickCount64"/>（毫秒），避免跨线程时钟差。</para>
/// </summary>
public sealed class MappingEngine
{
    /// <summary>鼠标速度的一阶平滑时间常数（毫秒）：平滑输入速度，摇杆偏转本身不再平滑。</summary>
    private const float InputSmoothTauMs = 50f;

    /// <summary>
    /// 速度 → 偏转的换算系数：偏转 = 速度(单位/ms) × 移动量 × 位移量 × 本系数。
    /// 取 10 时，默认 1.0×0.02 对应「约 5000 单位/秒满偏」，适合瞄准类操作。
    /// </summary>
    private const float RateToDeflectionScale = 10f;

    /// <summary>滚轮停滚多久视为松开（毫秒）。</summary>
    private const long WheelHoldMs = 120;

    private readonly object _gate = new();
    private readonly Dictionary<int, long> _keyDownAtMs = new();
    private readonly Dictionary<int, long> _mouseDownAtMs = new();

    // 每个鼠标轴规则：平滑后的输入速度（单位/ms），偏转由速度直接换算
    private readonly Dictionary<Guid, float> _rateX = new();
    private readonly Dictionary<Guid, float> _rateY = new();

    private long _wheelUpStartMs, _wheelUpLastMs, _wheelDownStartMs, _wheelDownLastMs;
    private MappingRule[] _rules = Array.Empty<MappingRule>();
    private double _pendingDx, _pendingDy;
    private long _lastEvalTicks;   // Stopwatch 计时：TickCount64 只有 ~15.6ms 分辨率，算速度不够

    public IReadOnlyList<MappingRule> Rules => _rules;

    public void SetRules(IEnumerable<MappingRule> rules)
    {
        lock (_gate)
        {
            _rules = rules.ToArray();
            ResetDeflection();
            _pendingDx = _pendingDy = 0;   // 旧规则的待处理位移不应带入新规则
        }
    }

    public void OnKey(int vk, bool down)
    {
        lock (_gate)
        {
            if (down) _keyDownAtMs.TryAdd(vk, Environment.TickCount64);
            else _keyDownAtMs.Remove(vk);
        }
    }

    public void OnMouseButton(int button, bool down)
    {
        lock (_gate)
        {
            if (down) _mouseDownAtMs.TryAdd(button, Environment.TickCount64);
            else _mouseDownAtMs.Remove(button);
        }
    }

    /// <summary>滚轮：同方向连续滚动沿用同一「按下时刻」，间歇超过 <see cref="WheelHoldMs"/> 则重新计时。</summary>
    public void OnWheel(int delta)
    {
        if (delta == 0) return;
        long now = Environment.TickCount64;
        lock (_gate)
        {
            if (delta > 0)
            {
                if (now - _wheelUpLastMs > WheelHoldMs) _wheelUpStartMs = now;
                _wheelUpLastMs = now;
            }
            else
            {
                if (now - _wheelDownLastMs > WheelHoldMs) _wheelDownStartMs = now;
                _wheelDownLastMs = now;
            }
        }
    }

    /// <summary>鼠标相对位移（钩子线程）。每拍被 <see cref="Evaluate"/> 一次性取走。</summary>
    public void OnMouseMove(int dx, int dy)
    {
        lock (_gate)
        {
            _pendingDx += dx;
            _pendingDy += dy;
        }
    }

    /// <summary>
    /// 求值一拍。<paramref name="enabled"/> 为 false 时输出恒空并清空摇杆偏转
    /// （捕获关闭 → 键鼠不驱动手柄，避免与系统输入重复触发）。
    /// </summary>
    public MappingResult Evaluate(bool enabled)
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;   // 按住类来源用毫秒足够
            long nowTicks = Stopwatch.GetTimestamp();
            bool first = _lastEvalTicks == 0;
            double dtMs = first
                ? 2.0
                : (nowTicks - _lastEvalTicks) * 1000.0 / Stopwatch.Frequency;

            // dt 过小（同一 tick 内重复求值）时不消费位移，沿用上一次速度，避免速度被放大
            bool consume = first || dtMs >= 0.5;
            if (consume) _lastEvalTicks = nowTicks;
            if (dtMs > 200.0) dtMs = 200.0;

            float smooth = 1f - MathF.Exp(-(float)dtMs / InputSmoothTauMs);

            double dx = 0, dy = 0;
            if (consume)
            {
                dx = _pendingDx;
                dy = _pendingDy;
                _pendingDx = _pendingDy = 0;
            }

            var result = new MappingResult();
            if (!enabled)
            {
                ResetDeflection();
                return result;
            }

            foreach (var rule in _rules)
            {
                if (rule.Source == MappingSource.MouseAxis)
                    ApplyMouseAxis(rule, dx, dy, (float)dtMs, smooth, consume, ref result);
                else
                    ApplyHold(rule, now, ref result);
            }

            // 方向输出可有多个、同轴相抵：逐轴求和后钳位
            result.LeftX = Math.Clamp(result.LeftX, -1f, 1f);
            result.LeftY = Math.Clamp(result.LeftY, -1f, 1f);
            result.RightX = Math.Clamp(result.RightX, -1f, 1f);
            result.RightY = Math.Clamp(result.RightY, -1f, 1f);
            return result;
        }
    }

    private void ResetDeflection()
    {
        _rateX.Clear();
        _rateY.Clear();
        _pendingDx = _pendingDy = 0;
    }

    /// <summary>
    /// 按住类来源（键盘 / 鼠标键 / 滚轮）：各输出延迟**依次累加**——
    /// <c>A(0) + B(100) + C(200)</c> → A 立即、B 100ms、C 300ms；
    /// 连按开启时，等所有输出都按下后整组按周期脉冲。
    /// </summary>
    private void ApplyHold(MappingRule rule, long now, ref MappingResult result)
    {
        if (!TryGetHoldStart(rule, now, out long startMs))
            return;

        var outputs = rule.Outputs;
        if (outputs.Count == 0)
            return;

        double held = now - startMs;

        double total = rule.DelayMs;
        foreach (var output in outputs)
            total += output.DelayMs;

        double period = Math.Max(20, rule.RepeatIntervalMs);
        double threshold = rule.DelayMs;

        foreach (var output in outputs)
        {
            threshold += output.DelayMs;
            if (held < threshold)
                continue;

            bool on = true;
            if (rule.RepeatEnabled)
                on = held >= total && (held - total) % period < period * 0.5;

            if (on) Emit(output, ref result);
        }
    }

    private bool TryGetHoldStart(MappingRule rule, long now, out long startMs)
    {
        switch (rule.Source)
        {
            case MappingSource.Keyboard:
                return _keyDownAtMs.TryGetValue(rule.KeyCode, out startMs);
            case MappingSource.MouseButton:
                return _mouseDownAtMs.TryGetValue(rule.KeyCode, out startMs);
            case MappingSource.MouseWheel:
                if (rule.KeyCode == 0)
                {
                    startMs = _wheelUpStartMs;
                    return now - _wheelUpLastMs <= WheelHoldMs;
                }
                startMs = _wheelDownStartMs;
                return now - _wheelDownLastMs <= WheelHoldMs;
            default:
                startMs = 0;
                return false;
        }
    }

    private static void Emit(MappingOutput output, ref MappingResult result)
    {
        switch (output.Target)
        {
            case GamepadTarget.LeftTrigger:
                result.LeftTrigger = Math.Max(result.LeftTrigger, Math.Clamp(output.Percent, 1, 100) / 100f);
                return;
            case GamepadTarget.RightTrigger:
                result.RightTrigger = Math.Max(result.RightTrigger, Math.Clamp(output.Percent, 1, 100) / 100f);
                return;

            // 摇杆方向：直接给该轴 ±1（多个同轴方向由外层求和后钳位）
            case GamepadTarget.LeftXMinus: result.LeftX -= 1f; return;
            case GamepadTarget.LeftXPlus: result.LeftX += 1f; return;
            case GamepadTarget.LeftYMinus: result.LeftY -= 1f; return;
            case GamepadTarget.LeftYPlus: result.LeftY += 1f; return;
            case GamepadTarget.RightXMinus: result.RightX -= 1f; return;
            case GamepadTarget.RightXPlus: result.RightX += 1f; return;
            case GamepadTarget.RightYMinus: result.RightY -= 1f; return;
            case GamepadTarget.RightYPlus: result.RightY += 1f; return;

            default:
                result.Buttons |= ToFlag(output.Target);
                return;
        }
    }

    private static GamepadButton ToFlag(GamepadTarget target) => target switch
    {
        GamepadTarget.A => GamepadButton.A,
        GamepadTarget.B => GamepadButton.B,
        GamepadTarget.X => GamepadButton.X,
        GamepadTarget.Y => GamepadButton.Y,
        GamepadTarget.LeftBumper => GamepadButton.LeftBumper,
        GamepadTarget.RightBumper => GamepadButton.RightBumper,
        GamepadTarget.LeftStick => GamepadButton.LeftStick,
        GamepadTarget.RightStick => GamepadButton.RightStick,
        GamepadTarget.Back => GamepadButton.Back,
        GamepadTarget.Start => GamepadButton.Start,
        GamepadTarget.DPadUp => GamepadButton.DPadUp,
        GamepadTarget.DPadDown => GamepadButton.DPadDown,
        GamepadTarget.DPadLeft => GamepadButton.DPadLeft,
        GamepadTarget.DPadRight => GamepadButton.DPadRight,
        _ => GamepadButton.None,
    };

    /// <summary>
    /// 鼠标移动 → 摇杆（水平、垂直各一套灵敏度，可分别指向任意轴，互不重复）。
    /// <para><b>速度模型</b>（参考 US20050176504A1：鼠标速度映射为摇杆位置）：
    /// 本拍位移 ÷ Δt 得到速度（单位/ms），先做一阶低通（τ=50ms）平滑，再乘以
    /// 「移动量 × 位移量 × <see cref="RateToDeflectionScale"/>=10」得到偏转并钳位。</para>
    /// <para>偏转**直接正比于鼠标速度**：匀速画圆时输出就是同样形状的圆，不会因为累积而瞬间打满
    /// （旧的「积分 + 泄漏」模型稳态放大 ~1/(1−衰减) ≈ 35 倍，任何连续移动都会饱和成方形）。
    /// 鼠标停下后速度衰减到 0，摇杆随之回中。</para>
    /// </summary>
    private void ApplyMouseAxis(MappingRule rule, double dx, double dy, float dtMs, float smooth,
        bool consume, ref MappingResult result)
    {
        float dt = dtMs <= 0f ? 1f : dtMs;

        if (rule.AxisX >= 0)
        {
            float gain = rule.MoveAmountX * rule.DisplacementX * RateToDeflectionScale;
            float rate = consume
                ? SmoothValue(_rateX, rule.Id, (float)(dx / dt), smooth)
                : CurrentValue(_rateX, rule.Id);
            AddAxis(rule.AxisX, Math.Clamp(rate * gain, -1f, 1f), ref result);
        }

        if (rule.AxisY >= 0)
        {
            float gain = rule.MoveAmountY * rule.DisplacementY * RateToDeflectionScale;
            float rate = consume
                ? SmoothValue(_rateY, rule.Id, (float)(dy / dt), smooth)
                : CurrentValue(_rateY, rule.Id);
            AddAxis(rule.AxisY, Math.Clamp(-rate * gain, -1f, 1f), ref result); // 鼠标下移 = 摇杆向下
        }
    }

    private static float CurrentValue(Dictionary<Guid, float> state, Guid id)
        => state.TryGetValue(id, out float value) ? value : 0f;

    /// <summary>一阶低通：<c>平滑值 += (目标 − 平滑值) × α</c>。</summary>
    private static float SmoothValue(Dictionary<Guid, float> state, Guid id, float target, float smooth)
    {
        state.TryGetValue(id, out float previous);
        float value = previous + (target - previous) * smooth;
        if (MathF.Abs(value) < 1e-5f) value = 0f;
        state[id] = value;
        return value;
    }


    private static void AddAxis(int axis, float value, ref MappingResult result)
    {
        if (value == 0f) return;

        switch ((StickAxis)axis)
        {
            case StickAxis.LeftX: result.LeftX += value; break;
            case StickAxis.LeftY: result.LeftY += value; break;
            case StickAxis.RightX: result.RightX += value; break;
            case StickAxis.RightY: result.RightY += value; break;
        }
    }
}
