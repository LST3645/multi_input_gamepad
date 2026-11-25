namespace MultiInput.Core;

/// <summary>
/// 不能参与映射的键盘按键：修饰键、系统键、锁定键。
/// <para>这些都是系统/全局快捷键的组成部分或会改变系统状态，参与映射会与捕获热键、
/// 系统快捷键、大小写/小键盘状态冲突，因此一律禁止绑定（编辑器取键与规则校验共同拦截）。</para>
/// </summary>
public static class KeyBlacklist
{
    private static readonly HashSet<int> Forbidden = new()
    {
        0x09,             // Tab
        0x13,             // Pause / Break
        0x14,             // CapsLock
        0x1B,             // Esc
        0x2C,             // PrintScreen
        0x5B, 0x5C,       // LWin / RWin
        0x5D,             // Menu（应用键）
        0x5F,             // Sleep
        0x90,             // NumLock
        0x91,             // ScrollLock

        // 编辑键区（方向键上方的九键，与 PrtSc/ScrLk/Pause 同区）
        0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E, // PgUp / PgDn / End / Home / Ins / Del

        0x10, 0x11, 0x12, // 泛化 Shift / Ctrl / Alt
        0xA0, 0xA1,       // LShift / RShift
        0xA2, 0xA3,       // LCtrl / RCtrl
        0xA4, 0xA5,       // LAlt / RAlt

        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, // 浏览器 后退/前进/刷新/停止/搜索/收藏/主页
        0xAD, 0xAE, 0xAF,                          // 静音 / 音量- / 音量+
        0xB0, 0xB1, 0xB2, 0xB3,                    // 媒体 下一曲/上一曲/停止/播放暂停
        0xB4, 0xB5, 0xB6, 0xB7,                    // 邮件 / 媒体选择 / 应用1 / 应用2（含计算器）
    };

    /// <summary>该虚拟键码是否禁止参与映射。</summary>
    public static bool Contains(int vk) => Forbidden.Contains(vk);
}
