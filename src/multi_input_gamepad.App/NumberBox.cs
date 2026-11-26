using System.Globalization;
using System.Windows.Controls;

namespace MultiInputGamepad.App;

/// <summary>界面数值输入框的解析：格式非法回落默认值，合法则夹到区间内。</summary>
internal static class NumberBox
{
    public static int Int(TextBox box, int fallback, int min, int max) =>
        int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, min, max)
            : fallback;

    public static float Float(TextBox box, float fallback, float min, float max) =>
        float.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
