using MultiInputGamepad.Input;
using MultiInputGamepad.Output;

namespace MultiInputGamepad.App;

/// <summary>线程安全的最新输入/输出快照：钩子/转发线程写入，UI 定时读取。</summary>
public sealed class InputState
{
    public sealed class Snapshot
    {
        public GamepadSnapshot? Pad;          // 输入端（物理手柄）
        public VirtualGamepadState? OutputPad; // 输出端（最近一次提交给虚拟手柄的状态）
        public HashSet<int> Keys = new();
        public bool ML, MR, MM, MX1, MX2;
        public int Dx, Dy;
        public int Wheel;
        public bool Capturing;
    }

    private readonly object _gate = new();
    private GamepadSnapshot? _pad;
    private VirtualGamepadState? _outputPad;
    private readonly HashSet<int> _keys = new();
    private bool _ml, _mr, _mm, _mx1, _mx2;
    private int _dx, _dy, _wheel;
    private bool _capturing;

    // 复用同一个快照对象：Read 只由 UI 线程调用，消费完即丢弃引用，不需要每次分配
    private readonly Snapshot _snapshot = new();

    public void SetPad(GamepadSnapshot? s) { lock (_gate) _pad = s; }

    public void SetOutputPad(VirtualGamepadState? s) { lock (_gate) _outputPad = s; }

    public void SetKey(int vk, bool down)
    {
        lock (_gate)
        {
            if (down) _keys.Add(vk);
            else _keys.Remove(vk);
        }
    }

    public void ApplyMouse(in MouseInputEvent e)
    {
        lock (_gate)
        {
            switch (e.Button)
            {
                case 1: _ml = e.ButtonDown; break;
                case 2: _mr = e.ButtonDown; break;
                case 3: _mm = e.ButtonDown; break;
                case 4: _mx1 = e.ButtonDown; break;
                case 5: _mx2 = e.ButtonDown; break;
            }
            _dx += e.DeltaX;
            _dy += e.DeltaY;
            _wheel += e.WheelDelta;
        }
    }

    public void SetCapturing(bool on) { lock (_gate) _capturing = on; }

    /// <summary>
    /// 读取并清空鼠标位移累计（每次 UI 刷新只显示该帧内的移动）。
    /// 返回的是**复用对象**，仅供调用方当拍消费，不要跨拍持有。
    /// </summary>
    public Snapshot Read()
    {
        lock (_gate)
        {
            var snap = _snapshot;
            snap.Pad = _pad;
            snap.OutputPad = _outputPad;
            snap.Keys.Clear();
            foreach (int vk in _keys) snap.Keys.Add(vk);
            snap.ML = _ml;
            snap.MR = _mr;
            snap.MM = _mm;
            snap.MX1 = _mx1;
            snap.MX2 = _mx2;
            snap.Dx = _dx;
            snap.Dy = _dy;
            snap.Wheel = _wheel;
            snap.Capturing = _capturing;

            _dx = 0;
            _dy = 0;
            _wheel = 0;
            return snap;
        }
    }
}
