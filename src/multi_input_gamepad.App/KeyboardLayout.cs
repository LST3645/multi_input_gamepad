namespace MultiInputGamepad.App;

/// <summary>
/// 键盘可视化布局：108 键全尺寸（功能键行 + 主键区 + 编辑键区 + 数字小键盘 + 4 个媒体键）。
/// 坐标以「键位单位」表示，1 = 一个标准键宽/键高；渲染时乘以 <see cref="Unit"/>。
/// </summary>
public static class KeyboardLayout
{
    public sealed record KeyDef(string Label, int Vk, double X, double Y, double W = 1, double H = 1);

    public const double Unit = 38;   // 每单位像素
    public const double Gap = 4;     // 键间隙（像素）

    private const double MainWidth = 15;            // 主键区宽度（单位）
    private const double SectionGap = Gap / Unit;   // 分区间额外偏移：视觉间隔 = 2×键间隙
    private const double EditStart = MainWidth + SectionGap;
    private const double EditWidth = 3;
    private const double NumStart = EditStart + EditWidth + SectionGap;
    private const double NumWidth = 4;
    private const double WidthUnits = NumStart + NumWidth;
    private const double HeightUnits = 6;           // 6 行

    public static double PixelWidth => WidthUnits * Unit;
    public static double PixelHeight => HeightUnits * Unit;

    // —— 虚拟键码 ——
    private const int VkBack = 0x08;
    private const int VkEnter = 0x0D;
    private const int VkTab = 0x09;
    private const int VkEsc = 0x1B;
    private const int VkPause = 0x13;
    private const int VkCaps = 0x14;
    private const int VkSpace = 0x20;
    private const int VkPageUp = 0x21;
    private const int VkPageDown = 0x22;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkPrintScreen = 0x2C;
    private const int VkInsert = 0x2D;
    private const int VkDelete = 0x2E;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkApps = 0x5D;
    private const int VkF1 = 0x70;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;
    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;
    private const int VkLaunchCalc = 0xB7;
    private const int VkOem1 = 0xBA;    // ;
    private const int VkOemPlus = 0xBB; // =
    private const int VkOemComma = 0xBC;// ,
    private const int VkOemMinus = 0xBD;// -
    private const int VkOemPeriod = 0xBE;// .
    private const int VkOem2 = 0xBF;    // /
    private const int VkOem3 = 0xC0;    // `
    private const int VkOem4 = 0xDB;    // [
    private const int VkOem5 = 0xDC;    // \
    private const int VkOem6 = 0xDD;    // ]
    private const int VkOem7 = 0xDE;    // '
    // 小键盘
    private const int VkNumPad0 = 0x60;
    private const int VkNumPad1 = 0x61;
    private const int VkNumPad2 = 0x62;
    private const int VkNumPad3 = 0x63;
    private const int VkNumPad4 = 0x64;
    private const int VkNumPad5 = 0x65;
    private const int VkNumPad6 = 0x66;
    private const int VkNumPad7 = 0x67;
    private const int VkNumPad8 = 0x68;
    private const int VkNumPad9 = 0x69;
    private const int VkMultiply = 0x6A;
    private const int VkAdd = 0x6B;
    private const int VkSubtract = 0x6D;
    private const int VkDecimal = 0x6E;
    private const int VkDivide = 0x6F;

    public static IReadOnlyList<KeyDef> Keys { get; } = Build();

    private static List<KeyDef> Build()
    {
        static KeyDef K(string label, int vk, double x, double y, double w = 1, double h = 1)
            => new(label, vk, x, y, w, h);

        var keys = new List<KeyDef>();

        // —— 功能键行 y=0 ——
        keys.Add(K("Esc", VkEsc, 0, 0));
        for (int i = 0; i < 12; i++)
            keys.Add(K($"F{i + 1}", VkF1 + i, 2 + i, 0));

        // —— 主键区 ——
        // y=1：数字行 + Backspace（1..9 = 0x31..0x39，0 = 0x30）
        keys.Add(K("`", VkOem3, 0, 1));
        for (int i = 0; i < 9; i++)
            keys.Add(K((i + 1).ToString(), 0x31 + i, 1 + i, 1));
        keys.Add(K("0", 0x30, 10, 1));
        keys.Add(K("-", VkOemMinus, 11, 1));
        keys.Add(K("=", VkOemPlus, 12, 1));
        keys.Add(K("Back", VkBack, 13, 1, 2));

        // y=2：Tab + QWERTY 行
        keys.Add(K("Tab", VkTab, 0, 2, 1.5));
        string[] qwerty = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
        int[] qwertyVk = { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50 };
        for (int i = 0; i < qwerty.Length; i++)
            keys.Add(K(qwerty[i], qwertyVk[i], 1.5 + i, 2));
        keys.Add(K("[", VkOem4, 11.5, 2));
        keys.Add(K("]", VkOem6, 12.5, 2));
        keys.Add(K("\\", VkOem5, 13.5, 2, 1.5));

        // y=3：Caps + ASDF 行
        keys.Add(K("Caps", VkCaps, 0, 3, 1.75));
        string[] asdf = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
        int[] asdfVk = { 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C };
        for (int i = 0; i < asdf.Length; i++)
            keys.Add(K(asdf[i], asdfVk[i], 1.75 + i, 3));
        keys.Add(K(";", VkOem1, 10.75, 3));
        keys.Add(K("'", VkOem7, 11.75, 3));
        keys.Add(K("Enter", VkEnter, 12.75, 3, 2.25));

        // y=4：Shift + ZXCV 行
        keys.Add(K("LShift", 0xA0, 0, 4, 2.25));
        string[] zxcv = { "Z", "X", "C", "V", "B", "N", "M" };
        int[] zxcvVk = { 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D };
        for (int i = 0; i < zxcv.Length; i++)
            keys.Add(K(zxcv[i], zxcvVk[i], 2.25 + i, 4));
        keys.Add(K(",", VkOemComma, 9.25, 4));
        keys.Add(K(".", VkOemPeriod, 10.25, 4));
        keys.Add(K("/", VkOem2, 11.25, 4));
        keys.Add(K("RShift", 0xA1, 12.25, 4, 2.75));

        // y=5：底行
        keys.Add(K("LCtrl", 0xA2, 0, 5, 1.5));
        keys.Add(K("LWin", VkLWin, 1.5, 5));
        keys.Add(K("LAlt", 0xA4, 2.5, 5, 1.5));
        keys.Add(K("Space", VkSpace, 4, 5, 6));
        keys.Add(K("RAlt", 0xA5, 10, 5, 1.5));
        keys.Add(K("RWin", VkRWin, 11.5, 5));
        keys.Add(K("Menu", VkApps, 12.5, 5));
        keys.Add(K("RCtrl", 0xA3, 13.5, 5, 1.5));

        // —— 编辑键区：方向键上方的九键，连续 3 行构成同一个区域
        //    （PrtSc/ScrLk/Pause → Ins/Home/PgUp → Del/End/PgDn；与真实键盘对齐：首行同功能键行）——
        keys.Add(K("PrtSc", VkPrintScreen, EditStart + 0, 0));
        keys.Add(K("ScrLk", VkScrollLock, EditStart + 1, 0));
        keys.Add(K("Pause", VkPause, EditStart + 2, 0));
        keys.Add(K("Ins", VkInsert, EditStart + 0, 1));
        keys.Add(K("Home", VkHome, EditStart + 1, 1));
        keys.Add(K("PgUp", VkPageUp, EditStart + 2, 1));
        keys.Add(K("Del", VkDelete, EditStart + 0, 2));
        keys.Add(K("End", VkEnd, EditStart + 1, 2));
        keys.Add(K("PgDn", VkPageDown, EditStart + 2, 2));
        keys.Add(K("↑", VkUp, EditStart + 1, 4));
        keys.Add(K("←", VkLeft, EditStart + 0, 5));
        keys.Add(K("↓", VkDown, EditStart + 1, 5));
        keys.Add(K("→", VkRight, EditStart + 2, 5));

        // —— 数字小键盘（顶部 4 个媒体键，凑满 108 键）——
        keys.Add(K("Mute", VkVolumeMute, NumStart + 0, 0));
        keys.Add(K("Vol-", VkVolumeDown, NumStart + 1, 0));
        keys.Add(K("Vol+", VkVolumeUp, NumStart + 2, 0));
        keys.Add(K("Calc", VkLaunchCalc, NumStart + 3, 0));

        keys.Add(K("NumLk", VkNumLock, NumStart + 0, 1));
        keys.Add(K("/", VkDivide, NumStart + 1, 1));
        keys.Add(K("*", VkMultiply, NumStart + 2, 1));
        keys.Add(K("-", VkSubtract, NumStart + 3, 1));

        keys.Add(K("7", VkNumPad7, NumStart + 0, 2));
        keys.Add(K("8", VkNumPad8, NumStart + 1, 2));
        keys.Add(K("9", VkNumPad9, NumStart + 2, 2));
        keys.Add(K("+", VkAdd, NumStart + 3, 2, 1, 2));

        keys.Add(K("4", VkNumPad4, NumStart + 0, 3));
        keys.Add(K("5", VkNumPad5, NumStart + 1, 3));
        keys.Add(K("6", VkNumPad6, NumStart + 2, 3));

        keys.Add(K("1", VkNumPad1, NumStart + 0, 4));
        keys.Add(K("2", VkNumPad2, NumStart + 1, 4));
        keys.Add(K("3", VkNumPad3, NumStart + 2, 4));
        keys.Add(K("Enter", VkEnter, NumStart + 3, 4, 1, 2));

        keys.Add(K("0", VkNumPad0, NumStart + 0, 5, 2));
        keys.Add(K(".", VkDecimal, NumStart + 2, 5));

        return keys;
    }
}
