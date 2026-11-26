using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MultiInputGamepad.Core;

namespace MultiInputGamepad.App.Converters;

/// <summary>手柄面板按键描边：位标志命中（已参与映射）→ 金色，否则默认灰。</summary>
public sealed class MappedBorderConverter : IValueConverter
{
    private static readonly Brush MappedBrush = Frozen(0xE0, 0xA0, 0x30);
    private static readonly Brush NormalBrush = Frozen(0x3A, 0x3A, 0x3E);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not GamepadButton mapped || parameter is not string name) return NormalBrush;
        return Enum.TryParse<GamepadButton>(name, out var button) && (mapped & button) != 0
            ? MappedBrush
            : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>bool → 光标：true = 手型（可点选编辑），false = 默认箭头。</summary>
public sealed class BoolToCursorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Cursors.Hand : Cursors.Arrow;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
