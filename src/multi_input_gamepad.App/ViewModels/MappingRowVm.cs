using MultiInputGamepad.Core;

namespace MultiInputGamepad.App.ViewModels;

/// <summary>手柄目标与摇杆轴的中文标签（列表与编辑器共用）。</summary>
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
        GamepadTarget.LeftXMinus => "左X−",
        GamepadTarget.LeftXPlus => "左X+",
        GamepadTarget.LeftYMinus => "左Y−",
        GamepadTarget.LeftYPlus => "左Y+",
        GamepadTarget.RightXMinus => "右X−",
        GamepadTarget.RightXPlus => "右X+",
        GamepadTarget.RightYMinus => "右Y−",
        GamepadTarget.RightYPlus => "右Y+",
        GamepadTarget.LeftTrigger => "LT",
        _ => "RT",
    };

    public static bool IsTrigger(GamepadTarget target) =>
        target is GamepadTarget.LeftTrigger or GamepadTarget.RightTrigger;

    public static string Axis(StickAxis axis) => axis switch
    {
        StickAxis.LeftX => "左摇杆X",
        StickAxis.LeftY => "左摇杆Y",
        StickAxis.RightX => "右摇杆X",
        _ => "右摇杆Y",
    };

    /// <summary>输入来源的可读描述：<c>键盘 W</c> / <c>鼠标左键</c> / <c>鼠标滚轮上</c> / <c>鼠标移动</c>。</summary>
    public static string Input(MappingRule rule) => rule.Source switch
    {
        MappingSource.Keyboard => $"键盘 {KeyNames.Of(rule.KeyCode)}",
        MappingSource.MouseButton => $"鼠标{MouseButton(rule.KeyCode)}",
        MappingSource.MouseWheel => $"鼠标滚轮{(rule.KeyCode == 0 ? "上" : "下")}",
        _ => "鼠标移动",
    };

    public static string MouseButton(int button) => button switch
    {
        1 => "左键",
        2 => "右键",
        3 => "中键",
        4 => "X1",
        _ => "X2",
    };

    /// <summary>单条输出的文字：<c>延迟100ms RT 90%</c> / <c>A</c>。</summary>
    public static string Output(MappingOutput output)
    {
        string prefix = output.DelayMs > 0 ? $"延迟{output.DelayMs}ms " : "";
        return IsTrigger(output.Target)
            ? $"{prefix}{Target(output.Target)} {output.Percent}%"
            : $"{prefix}{Target(output.Target)}";
    }

    public static string Outputs(IEnumerable<MappingOutput> outputs) =>
        string.Join(" + ", outputs.Select(Output));
}

/// <summary>映射列表的一行（展示用文字在构造时定稿，编辑后整行替换）。</summary>
public sealed class MappingRowVm
{
    public MappingRule Rule { get; }
    public string InputText { get; }
    public string DetailText { get; }

    public MappingRowVm(MappingRule rule)
    {
        Rule = rule;
        InputText = BuildInput(rule);
        DetailText = BuildDetail(rule);
    }

    private static string BuildInput(MappingRule rule) => rule.Source switch
    {
        MappingSource.Keyboard =>
            $"{KeyNames.Of(rule.KeyCode)} → {MappingLabels.Outputs(rule.Outputs)}",
        MappingSource.MouseButton =>
            $"鼠标{MappingLabels.MouseButton(rule.KeyCode)} → {MappingLabels.Outputs(rule.Outputs)}",
        MappingSource.MouseWheel =>
            $"鼠标滚轮{(rule.KeyCode == 0 ? "上" : "下")} → {MappingLabels.Outputs(rule.Outputs)}",
        _ => BuildAxisInput(rule),
    };

    /// <summary>鼠标轴向：<c>鼠标X/Y → 左摇杆X/右摇杆Y</c>，未使用的轴显示 <c>-</c>。</summary>
    private static string BuildAxisInput(MappingRule rule)
    {
        string x = rule.AxisX >= 0 ? MappingLabels.Axis((StickAxis)rule.AxisX) : "-";
        string y = rule.AxisY >= 0 ? MappingLabels.Axis((StickAxis)rule.AxisY) : "-";
        return $"鼠标X/Y → {x}/{y}";
    }

    private static string BuildDetail(MappingRule rule)
    {
        if (rule.Source == MappingSource.MouseAxis)
        {
            var parts = new List<string>();
            if (rule.AxisX >= 0)
                parts.Add($"水平 {rule.MoveAmountX:0.00}×{rule.DisplacementX:0.000}");
            if (rule.AxisY >= 0)
                parts.Add($"垂直 {rule.MoveAmountY:0.00}×{rule.DisplacementY:0.000}");
            return string.Join(" · ", parts);
        }

        var detail = new List<string>();
        int total = rule.DelayMs + rule.Outputs.Sum(output => output.DelayMs);
        detail.Add(total > 0 ? $"末项 {total}ms" : "立即");
        if (rule.RepeatEnabled) detail.Add($"连按 {rule.RepeatIntervalMs}ms");
        return string.Join(" · ", detail);
    }
}
