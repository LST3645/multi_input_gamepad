using System.Runtime.InteropServices;

namespace MultiInputGamepad.Input;

/// <summary>鼠标事件（原始输入，两种模式都上报）。</summary>
public readonly record struct MouseInputEvent(
    int DeltaX,       // 相对位移（RawInput 设备计数）
    int DeltaY,
    int Button,       // 0=无按钮；1 左 2 右 3 中 4=X1 5=X2
    bool ButtonDown,
    int WheelDelta);  // 滚轮增量（120 的倍数），0=无

/// <summary>
/// 捕获模式状态机 + 键盘/鼠标低层钩子。
/// 钩子判定优先级：① 开关热键 SHIFT+ALT+\ → ② Win/Alt 系统组合透传 → ③ 捕获开启吞其余 → ④ 透传。
/// 修饰键状态在钩子内部跟踪（不依赖 GetAsyncKeyState），保证开关热键在吞键后仍可靠。
/// 注意：钩子回调在调用 Install() 的线程上触发，该线程必须泵消息（见 Spike 的消息泵）。
/// <para>鼠标位移/按键/滚轮数据来自 <see cref="RawMouse"/>：捕获期吞掉 WM_MOUSEMOVE 会冻结光标，
/// <c>MSLLHOOKSTRUCT.pt</c> 不再累积，只有 RawInput 能拿到真实相对位移。LL 鼠标钩子只负责「吞或不吞」。</para>
/// </summary>
public sealed class InputCapture : IDisposable
{
    private nint _kbHook;
    private nint _mouseHook;
    private NativeMethods.LowLevelHookProc? _kbProc;
    private NativeMethods.LowLevelHookProc? _mouseProc;
    private RawMouse? _rawMouse;
    private readonly HashSet<int> _pressed = new();

    /// <summary>
    /// 已经透传给系统的按下键。它们的「抬起」必须也透传——否则开启捕获的那一刻，
    /// 之前正常按下的修饰键（热键的 SHIFT/ALT）抬起被吞掉，系统层面会一直认为它按着。
    /// </summary>
    private readonly HashSet<int> _osPassed = new();

    private volatile bool _capturing;
    private bool _shiftDown;
    private bool _altDown;
    private bool _winDown;

    public bool Capturing => _capturing;

    /// <summary>RawInput 是否成功接入（失败则鼠标位移/按键/滚轮不可用）。</summary>
    public bool RawMouseReady { get; private set; }

    /// <summary>键盘键状态变化（vKey，按下=true）。两种模式都上报，仅捕获开启时吞键。</summary>
    public event Action<int, bool>? KeyStateChanged;

    /// <summary>鼠标位移/按键/滚轮。两种模式都上报，仅捕获开启时吞事件。</summary>
    public event Action<MouseInputEvent>? MouseInput;

    /// <summary>捕获开关变化（热键在钩子回调里切换，UI 也可能直接切换）。</summary>
    public event Action<bool>? CapturingChanged;

    public bool Install()
    {
        // RawInput 必须先建（建窗口 + 注册），再用 LL 钩子决定是否吞噬
        _rawMouse = new RawMouse();
        _rawMouse.Moved += (dx, dy) => MouseInput?.Invoke(new MouseInputEvent(dx, dy, 0, false, 0));
        _rawMouse.ButtonChanged += (button, down) => MouseInput?.Invoke(new MouseInputEvent(0, 0, button, down, 0));
        _rawMouse.Wheel += delta => MouseInput?.Invoke(new MouseInputEvent(0, 0, 0, false, delta));
        RawMouseReady = _rawMouse.Install();

        _kbProc = KbHook;
        _mouseProc = MouseHook;
        nint hMod = NativeMethods.GetModuleHandleW(null);
        _kbHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        return _kbHook != 0 && _mouseHook != 0;
    }

    public void Toggle() => SetCapturing(!_capturing);

    public void SetCapturing(bool on)
    {
        if (_capturing == on) return;
        _capturing = on;
        CapturingChanged?.Invoke(on);
    }

    private nint KbHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int vk = (int)kb.vkCode;
            bool down = wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN;

            if (HandleKey(vk, down))
                return 1;   // 吞（不落到桌面/游戏）
        }

        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    /// <summary>
    /// 键盘事件判定：返回 true = 吞掉，false = 透传给系统。抽出来是为了可断言（含吞键时序）。
    /// </summary>
    internal bool HandleKey(int vk, bool down)
    {
        // 跟踪修饰键物理状态（在吞键之前，保证开关热键可靠）
        // WH_KEYBOARD_LL 的 vkCode 对 Shift/Alt 可能报左/右变体（VK_LSHIFT 等）或通用（VK_SHIFT）
        if (vk == NativeMethods.VK_SHIFT || vk == NativeMethods.VK_LSHIFT || vk == NativeMethods.VK_RSHIFT)
            _shiftDown = down;
        else if (vk == NativeMethods.VK_MENU || vk == NativeMethods.VK_LMENU || vk == NativeMethods.VK_RMENU)
            _altDown = down;
        else if (vk == NativeMethods.VK_LWIN || vk == NativeMethods.VK_RWIN)
            _winDown = down;

        // ① 开关热键 SHIFT+ALT+\ → 切换 + 吞（按下已被吞，其抬起也吞，系统视角从未发生）
        if (down && vk == NativeMethods.VK_OEM_5 && _shiftDown && _altDown)
        {
            Toggle();
            return true;
        }

        // ② Win 修饰 / Alt+Tab 系统组合 → 永不拦截
        if (_winDown || (_altDown && vk == NativeMethods.VK_TAB))
        {
            TrackOsKey(vk, down);
            return false;
        }

        // ③ 记录键状态（两种模式都记录，供可视化与映射；捕获开启时同时吞键）
        if (down)
        {
            if (_pressed.Add(vk)) KeyStateChanged?.Invoke(vk, true);
        }
        else
        {
            if (_pressed.Remove(vk)) KeyStateChanged?.Invoke(vk, false);
        }

        if (_capturing)
        {
            // 这颗键的按下发生在开捕获之前、已经透传给系统 —— 抬起必须放行，否则系统卡住修饰键
            if (!down && _osPassed.Remove(vk)) return false;

            return true;
        }

        TrackOsKey(vk, down);
        return false;
    }

    private void TrackOsKey(int vk, bool down)
    {
        if (down) _osPassed.Add(vk);
        else _osPassed.Remove(vk);
    }

    /// <summary>只决定吞或不吞；位移等数据来自 RawInput。</summary>
    private nint MouseHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _capturing)
            return 1; // 捕获期吞掉全部鼠标事件（移动被吞 → 光标冻结）

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_kbHook != 0) NativeMethods.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _kbHook = _mouseHook = 0;
        _rawMouse?.Dispose();
        _rawMouse = null;
    }
}
