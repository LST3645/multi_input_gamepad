using System.Runtime.InteropServices;

namespace MultiInputGamepad.Input;

public static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;      // Alt
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;
    public const int VK_TAB = 0x09;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_CAPITAL = 0x14;   // CapsLock
    public const int VK_NUMLOCK = 0x90;   // NumLock
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_OEM_5 = 0xDC;     // 反斜杠 '\'

    public const uint PM_REMOVE = 0x0001;
    public const uint PM_NOREMOVE = 0x0000;

    /// <summary>唤醒钩子线程消息泵的自定义消息（值与系统消息不冲突）。</summary>
    public const int WM_APP_WAKE = 0x8000 + 1;

    public const int WM_QUIT = 0x0012;

    public delegate nint LowLevelHookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int idHook, LowLevelHookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// 调用线程消息队列视角的键状态；最低位为 CapsLock / NumLock 的「锁定开启」。
    /// （<see cref="GetAsyncKeyState"/> 的最低位是「自上次调用后被按下」，不能用作锁定状态。）
    /// </summary>
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int vKey);

    // —— 单实例激活 / 托盘图标清理 ——
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ShowCursor(bool bShow);

    // —— 消息泵（钩子回调必须在装有钩子的线程上泵消息）——
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessageW(uint idThread, int msg, nint wParam, nint lParam);

    // —— 定时器精度（500Hz 需 1ms 分辨率）——
    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    // —— 高精度等待定时器：一次等待精确睡到死线（替代 Sleep(1) 的多次累积）——
    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;
    public const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWaitableTimerExW(nint lpTimerAttributes, string? lpTimerName,
        uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetWaitableTimer(nint hTimer, ref long lpDueTime, int lPeriod,
        nint pfnCompletionRoutine, nint lpArgToCompletionRoutine, bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    // —— RawInput ——
    // 捕获期吞掉 WM_MOUSEMOVE 会冻结光标，MSLLHOOKSTRUCT.pt 不再累积（实测：连发 +15 只出现一次、
    // 随后恒为 +1，反向时出现 -22 的放大跳变），差值不可用。实测 RawInput 在 LL 钩子吞键时
    // 仍按原始位移逐事件到达，因此鼠标位移/按键/滚轮统一从 RawInput 取。
    public const int WM_INPUT = 0x00FF;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    public const uint RIM_TYPEMOUSE = 0;
    public const uint WS_POPUP = 0x80000000;

    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;
    public const ushort RI_MOUSE_WHEEL = 0x0400;

    public delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WindowClassEx wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(nint hRawInput, uint command, nint data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(nint hRawInput, uint command, byte[] data, ref uint size, uint headerSize);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WindowClassEx
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public nint lpszMenuName;
    public nint lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
public struct RawInputDevice
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public nint hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
public struct RawInputHeader
{
    public uint dwType;
    public uint dwSize;
    public nint hDevice;
    public nint wParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct RawMouseData
{
    public ushort usFlags;
    // RAWMOUSE 里 usButtonFlags/usButtonData 与 ULONG ulButtons 是 union，起点按 4 字节对齐；
    // 少了这 2 字节填充，usButtonFlags 会读到 padding（恒 0），按键与滚轮全部失效。
    public ushort usButtonsPadding;
    public ushort usButtonFlags;
    public ushort usButtonData;
    public uint ulRawButtons;
    public int lLastX;
    public int lLastY;
    public uint ulExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
public struct RawInputData
{
    public RawInputHeader header;
    public RawMouseData mouse;
}

[StructLayout(LayoutKind.Sequential)]
public struct KbdLlHookStruct
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MouseLlHookStruct
{
    public int ptX;
    public int ptY;
    public uint mouseData;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}
