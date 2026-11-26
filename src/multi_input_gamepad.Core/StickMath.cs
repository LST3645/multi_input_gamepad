namespace MultiInputGamepad.Core;

/// <summary>摇杆输出形状：方形（对角更快）或圆形（速度均匀）。</summary>
public enum StickShape
{
    Square = 0,
    Circular = 1,
}

/// <summary>死区类型：radial（圆形，方向无损，推荐）或 axial（各轴独立）。</summary>
public enum DeadzoneType
{
    Radial = 0,
    Axial = 1,
}

public readonly record struct DeadzoneConfig(float Size = 0.08f, DeadzoneType Type = DeadzoneType.Radial);

/// <summary>扳机分段：t∈[From, To) 映射到一个输出档位/按键值。</summary>
public readonly record struct TriggerSegment(float From, float To, int OutputValue);

/// <summary>
/// 摇杆死区、方形/圆形裁剪、扳机分段的纯算法实现（无外部依赖，可单测）。
/// 输入输出一律使用归一化区间 [-1,1]（扳机为 [0,1]）。
/// </summary>
public static class StickMath
{
    /// <summary>死区 + 重映射到 [0,1] 幅值（保留方向）。返回 [-1,1]²。</summary>
    public static (float X, float Y) ApplyDeadzone(float x, float y, DeadzoneConfig d)
    {
        x = Math.Clamp(x, -1f, 1f);
        y = Math.Clamp(y, -1f, 1f);

        if (d.Type == DeadzoneType.Radial)
        {
            float m = MathF.Sqrt(x * x + y * y);
            if (m < d.Size)
                return (0f, 0f);
            float scale = (m - d.Size) / (1f - d.Size);
            return (x / m * scale, y / m * scale);
        }

        return (Axial(x, d.Size), Axial(y, d.Size));
    }

    private static float Axial(float a, float deadzone)
    {
        if (MathF.Abs(a) < deadzone)
            return 0f;
        return MathF.Sign(a) * ((MathF.Abs(a) - deadzone) / (1f - deadzone));
    }

    /// <summary>方形/圆形裁剪。方形保留原值（对角半径 √2）；圆形在半径&gt;1 时归一到单位圆。</summary>
    public static (float X, float Y) ApplyShape(float x, float y, StickShape shape)
    {
        x = Math.Clamp(x, -1f, 1f);
        y = Math.Clamp(y, -1f, 1f);

        if (shape == StickShape.Circular)
        {
            float r = MathF.Sqrt(x * x + y * y);
            if (r > 1f)
            {
                x /= r;
                y /= r;
            }
        }

        return (x, y);
    }

    /// <summary>
    /// 物理摇杆与合成值合并：**逐轴取绝对值大者**，再按形状裁剪（圆形归一化 / 方形原样）。
    /// 这是「物理输入 + 映射输出」唯一的合并入口，键盘方向、鼠标轴都走这里。
    /// </summary>
    public static (float X, float Y) ComposeStick(float physX, float physY, float mappedX, float mappedY, StickShape shape)
    {
        float x = MathF.Abs(mappedX) > MathF.Abs(physX) ? mappedX : physX;
        float y = MathF.Abs(mappedY) > MathF.Abs(physY) ? mappedY : physY;
        return ApplyShape(x, y, shape);
    }

    /// <summary>
    /// 扳机分段（含迟滞）。segments 按 From 升序；lastIndex 为上一拍所在段（用于迟滞）。
    /// 返回当前段下标，无匹配返回 -1。
    /// </summary>
    public static int SegmentIndex(float t, IReadOnlyList<TriggerSegment> segments, ref int lastIndex, float hysteresis)
    {
        if (segments.Count == 0)
        {
            lastIndex = -1;
            return -1;
        }

        // 迟滞：仍落在上一段外扩区间内则不切换，抑制边界抖动。
        if (lastIndex >= 0 && lastIndex < segments.Count)
        {
            var prev = segments[lastIndex];
            if (t >= prev.From - hysteresis && t < prev.To + hysteresis)
                return lastIndex;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (t >= segments[i].From && t < segments[i].To)
            {
                lastIndex = i;
                return i;
            }
        }

        lastIndex = -1;
        return -1;
    }
}
