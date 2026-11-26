using MultiInputGamepad.Core;

namespace MultiInputGamepad.App.ViewModels;

/// <summary>手柄目标 / 摇杆轴 / 输入来源的可读标签（列表与编辑器共用）。</summary>
internal static class MappingLabels
{
    public static string Target(GamepadTarget target) => target switch
    {
        GamepadTarget.A => "A",
        GamepadTarget.B => "B",
        GamepadTarget.X => "X",
        GamepadTarget.Y => "Y",
        GamepadTarget.LeftBumper => "LB",
        GamepadTarget.RightBumper => "RB",
        GamepadTarget.LeftStick => "L3",
        GamepadTarget.RightStick => "R3",
        GamepadTarget.Back => "View",
        GamepadTarget.Start => "Menu",
        GamepadTarget.DPadUp => "↑",
        GamepadTarget.DPadDown => "↓",
        GamepadTarget.DPadLeft => "←",
        GamepadTarget.DPadRight => "→",
        GamepadTarget.LeftXMinus => "LS左",
        GamepadTarget.LeftXPlus => "LS右",
        GamepadTarget.LeftYMinus => "LS下",
        GamepadTarget.LeftYPlus => "LS上",
        GamepadTarget.RightXMinus => "RS左",
        GamepadTarget.RightXPlus => "RS右",
        GamepadTarget.RightYMinus => "RS下",
        GamepadTarget.RightYPlus => "RS上",
        GamepadTarget.LeftTrigger => "LT",
        _ => "RT",
    };

    public static bool IsTrigger(GamepadTarget target) =>
        target is GamepadTarget.LeftTrigger or GamepadTarget.RightTrigger;

    /// <summary>鼠标轴向的目标轴：<c>LS X</c> / <c>RS Y</c>。</summary>
    public static string Axis(StickAxis axis) => axis switch
    {
        StickAxis.LeftX => "LS X",
        StickAxis.LeftY => "LS Y",
        StickAxis.RightX => "RS X",
        _ => "RS Y",
    };

    /// <summary>来源前缀：<c>键盘</c> / <c>鼠标</c> / <c>手柄</c>。</summary>
    public static string Source(MappingRule rule) => rule.Source switch
    {
        MappingSource.Keyboard => "键盘",
        MappingSource.MouseButton or MappingSource.MouseWheel or MappingSource.MouseAxis => "鼠标",
        _ => "手柄",
    };

    /// <summary>输入名称（不含来源）：<c>K</c> / <c>左键</c> / <c>滚轮上</c> / <c>移动</c> / <c>LS上</c>。</summary>
    public static string InputName(MappingRule rule) => rule.Source switch
    {
        MappingSource.Keyboard => KeyNames.Of(rule.KeyCode),
        MappingSource.MouseButton => MouseButton(rule.KeyCode),
        MappingSource.MouseWheel => rule.KeyCode == 0 ? "滚轮上" : "滚轮下",
        MappingSource.MouseAxis => "移动",
        _ => PadInput(rule.PadInput),
    };

    /// <summary>带来源前缀的输入：<c>键盘, K</c> / <c>鼠标, 左键</c> / <c>手柄, LS上</c>。</summary>
    public static string Input(MappingRule rule) => $"{Source(rule)}, {InputName(rule)}";

    /// <summary>手柄输入的可读名称：<c>A</c> / <c>LT 75%</c> / <c>LS上</c>。</summary>
    public static string PadInput(GamepadInput input)
    {
        if (GamepadInputs.TryGetTriggerLevel(input, out bool left, out int level))
            return $"{(left ? "LT" : "RT")} {level * 100 / GamepadInputs.TriggerLevels}%";

        if (GamepadInputs.TryGetStickDirection(input, out bool leftStick, out float center))
        {
            string name = center switch
            {
                90f => "上",
                -90f => "下",
                180f => "左",
                _ => "右",
            };
            return $"{(leftStick ? "LS" : "RS")}{name}";
        }

        return input switch
        {
            GamepadInput.A => "A",
            GamepadInput.B => "B",
            GamepadInput.X => "X",
            GamepadInput.Y => "Y",
            GamepadInput.LeftBumper => "LB",
            GamepadInput.RightBumper => "RB",
            GamepadInput.LeftStick => "L3",
            GamepadInput.RightStick => "R3",
            GamepadInput.Start => "Menu",
            GamepadInput.Back => "View",
            GamepadInput.DPadUp => "↑",
            GamepadInput.DPadDown => "↓",
            GamepadInput.DPadLeft => "←",
            GamepadInput.DPadRight => "→",
            _ => "?",
        };
    }

    public static string MouseButton(int button) => button switch
    {
        1 => "左键",
        2 => "右键",
        3 => "中键",
        4 => "X1",
        _ => "X2",
    };

    /// <summary>单条输出的文字：<c>100ms RT 90%</c> / <c>A</c>（不含来源前缀，供列表与编辑器 chip 用）。</summary>
    public static string Output(MappingOutput output)
    {
        string prefix = output.DelayMs > 0 ? $"{output.DelayMs}ms " : "";
        return IsTrigger(output.Target)
            ? $"{prefix}{Target(output.Target)} {output.Percent}%"
            : $"{prefix}{Target(output.Target)}";
    }

    public static string Outputs(IEnumerable<MappingOutput> outputs) =>
        string.Join(" + ", outputs.Select(Output));
}

/// <summary>映射列表的一行：<c>键盘, K → A</c>（展示文字在构造时定稿，编辑后整行替换）。</summary>
public sealed class MappingRowVm
{
    public MappingRule Rule { get; }
    public string Text { get; }

    /// <summary>连按说明（仅在开启连按时显示）：<c>连按, 每 100 ms</c>。</summary>
    public string RepeatText { get; }
    public bool HasRepeat { get; }

    public MappingRowVm(MappingRule rule)
    {
        Rule = rule;
        Text = BuildText(rule);
        HasRepeat = rule.RepeatEnabled;
        RepeatText = rule.RepeatEnabled ? $"连按, 每 {rule.RepeatIntervalMs} ms" : "";
    }

    /// <summary>整行：<c>来源, 输入 → 输出</c>（输出端只有手柄，不再重复写来源）；轴向规则的目标是 LS/RS 的 X/Y 轴。</summary>
    private static string BuildText(MappingRule rule) => rule.Source == MappingSource.MouseAxis
        ? $"{MappingLabels.Input(rule)} → {AxisText(rule)}"
        : $"{MappingLabels.Input(rule)} → {MappingLabels.Outputs(rule.Outputs)}";

    /// <summary>轴向目标：<c>LS X / RS Y</c>，未使用的轴显示 <c>-</c>。</summary>
    private static string AxisText(MappingRule rule)
    {
        string x = rule.AxisX >= 0 ? MappingLabels.Axis((StickAxis)rule.AxisX) : "-";
        string y = rule.AxisY >= 0 ? MappingLabels.Axis((StickAxis)rule.AxisY) : "-";
        return $"{x} / {y}";
    }
}
