namespace MultiInputGamepad.App;

/// <summary>虚拟键码 → 可视化标签（复用键盘布局的键名，未知键回落为十六进制）。</summary>
internal static class KeyNames
{
    private static readonly Dictionary<int, string> Labels = Build();

    public static string Of(int vk) => Labels.TryGetValue(vk, out var label) ? label : $"0x{vk:X2}";

    private static Dictionary<int, string> Build()
    {
        var map = new Dictionary<int, string>();
        foreach (var key in KeyboardLayout.Keys)
            map[key.Vk] = key.Label; // 重码（主/小键盘 Enter 同为 0x0D）标签一致，直接覆盖
        return map;
    }
}
