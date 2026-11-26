using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiInputGamepad.App.Converters;

/// <summary>
/// 按 bool 切换画刷：true 用 ConverterParameter 指定的颜色，false 用暗色底。
/// ConverterParameter 支持 <c>"#启用色|#禁用色"</c> 形式，便于描边等需要透明/异色的场景。
/// </summary>
public sealed class OnOffBrushConverter : IValueConverter
{
    private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool on = value is true;
        string text = parameter as string ?? "";

        int split = text.IndexOf('|');
        if (split >= 0)
        {
            string onColor = text[..split].Trim();
            string offColor = text[(split + 1)..].Trim();
            return Parse(on ? onColor : offColor) ?? (on ? Brushes.Lime : OffBrush);
        }

        if (!on) return OffBrush;
        return Parse(text) ?? Brushes.Lime;
    }

    private static Brush? Parse(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        if (ColorConverter.ConvertFromString(color) is not Color c) return null;

        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
