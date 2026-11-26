using System.Runtime.InteropServices;

namespace MultiInputGamepad.Input;

/// <summary>
/// 原始输入（RawInput）鼠标源：提供设备级相对位移、按键、滚轮。
/// <para>为什么不用 WH_MOUSE_LL 的 <c>MSLLHOOKSTRUCT.pt</c> 求差：捕获期吞掉 WM_MOUSEMOVE 后光标被冻结，
/// pt 不再累积（实测连发 +15 只出现一次、随后恒为 +1，反向时出现 -22 的放大跳变），差值不可用；
/// 实测 RawInput 在 LL 钩子吞键时仍按原始位移逐事件到达。</para>
/// <para>必须在将泵消息的线程上 <see cref="Install"/>；事件在消息泵线程上触发。</para>
/// </summary>
public sealed class RawMouse : IDisposable
{
    private const string ClassName = "MultiInputGamepad.RawMouseSink";

    private nint _hwnd;
    private NativeMethods.WndProcDelegate? _wndProc;
    private readonly byte[] _buffer = new byte[256];

    /// <summary>相对位移（设备计数）。</summary>
    public event Action<int, int>? Moved;

    /// <summary>按键变化：1 左 / 2 右 / 3 中 / 4 X1 / 5 X2。</summary>
    public event Action<int, bool>? ButtonChanged;

    /// <summary>滚轮增量（每格 ±120，正=向上）。</summary>
    public event Action<int>? Wheel;

    public bool Install()
    {
        _wndProc = WndProc;
        nint hInstance = NativeMethods.GetModuleHandleW(null);

        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = Marshal.StringToHGlobalUni(ClassName),
        };

        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
            return false;

        // 不可见窗口（WS_POPUP 且从不 Show），仅用于接收 WM_INPUT
        _hwnd = NativeMethods.CreateWindowExW(0, ClassName, "", NativeMethods.WS_POPUP,
            0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
            return false;

        var device = new RawInputDevice
        {
            usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
            usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
            dwFlags = NativeMethods.RIDEV_INPUTSINK, // 窗口不在前台也接收
            hwndTarget = _hwnd,
        };

        return NativeMethods.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == NativeMethods.WM_INPUT)
        {
            HandleRaw(lParam);
            return 0;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleRaw(nint hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, 0, ref size, headerSize) != 0)
            return;
        if (size == 0 || size > _buffer.Length)
            return;

        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, _buffer, ref size, headerSize) != size)
            return;

        var raw = MemoryMarshal.Read<RawInputData>(_buffer);
        if (raw.header.dwType != NativeMethods.RIM_TYPEMOUSE)
            return;

        ref readonly RawMouseData mouse = ref raw.mouse;

        if (mouse.lLastX != 0 || mouse.lLastY != 0)
            Moved?.Invoke(mouse.lLastX, mouse.lLastY);

        ushort flags = mouse.usButtonFlags;
        if ((flags & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) ButtonChanged?.Invoke(1, true);
        if ((flags & NativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0) ButtonChanged?.Invoke(1, false);
        if ((flags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) ButtonChanged?.Invoke(2, true);
        if ((flags & NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0) ButtonChanged?.Invoke(2, false);
        if ((flags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) ButtonChanged?.Invoke(3, true);
        if ((flags & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0) ButtonChanged?.Invoke(3, false);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_4_DOWN) != 0) ButtonChanged?.Invoke(4, true);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_4_UP) != 0) ButtonChanged?.Invoke(4, false);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_5_DOWN) != 0) ButtonChanged?.Invoke(5, true);
        if ((flags & NativeMethods.RI_MOUSE_BUTTON_5_UP) != 0) ButtonChanged?.Invoke(5, false);

        // 横向滚轮（RI_MOUSE_HWHEEL）不处理
        if ((flags & NativeMethods.RI_MOUSE_WHEEL) != 0)
            Wheel?.Invoke(unchecked((short)mouse.usButtonData));
    }

    public void Dispose()
    {
        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        NativeMethods.UnregisterClassW(ClassName, NativeMethods.GetModuleHandleW(null));
        _wndProc = null;
    }
}
