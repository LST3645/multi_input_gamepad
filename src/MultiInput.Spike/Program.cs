using MultiInput.Core;
using MultiInput.Input;
using MultiInput.Output;

// M0 技术验证 spike v11（需以管理员运行）。
// 顺序：清理残留 → 探测 Elite 2 槽位 → 创建虚拟手柄 → 装钩子 → 消息泵(可 Ctrl+C 退出)。
// 500Hz 检测循环读 Elite 2（仅 XInputGetState，廉价）；按变化提交 + 最小回报率 125Hz。
// 控制台实时显示手柄按键/摇杆/扳机 + 捕获后的鼠标事件。

Console.WriteLine("MultiInput M0 Spike v11");
Console.WriteLine("（需要管理员权限；连接 Xbox Elite Series 2）");
Console.WriteLine("SHIFT+ALT+\\ 切换捕获；Ctrl+C 退出");

if (args.Contains("--test-math"))
{
    RunMathTests();
    return;
}

if (args.Contains("--list-slots"))
{
    for (uint i = 0; i < 4; i++)
    {
        var st = new XInputState();
        uint r = XInput.GetState(i, ref st);
        Console.WriteLine($"slot {i}: {(r == XInput.ErrorSuccess ? "已连接" : "空")}");
    }
    return;
}

if (args.Contains("--probe-slots"))
{
    var first = new uint[4];
    for (uint i = 0; i < 4; i++)
    {
        var st = new XInputState();
        if (XInput.GetState(i, ref st) == XInput.ErrorSuccess) first[i] = st.dwPacketNumber;
        else first[i] = uint.MaxValue;
    }
    Console.WriteLine("等待 2 秒观察包序号变化...");
    Thread.Sleep(2000);
    for (uint i = 0; i < 4; i++)
    {
        var st = new XInputState();
        if (XInput.GetState(i, ref st) != XInput.ErrorSuccess)
        {
            Console.WriteLine($"slot {i}: 空");
            continue;
        }
        bool changing = first[i] != uint.MaxValue && st.dwPacketNumber != first[i];
        Console.WriteLine($"slot {i}: packet {first[i]} -> {st.dwPacketNumber}  {(changing ? "活动(物理手柄)" : "静止(虚拟/残留)")}");
    }
    return;
}

try
{
    NativeMethods.timeBeginPeriod(1);
    Console.WriteLine("[1/6] 定时器精度已设置");

    // ① 先清理残留虚拟手柄（必须在探测槽位之前，否则残留占用 XInput 槽位）
    HidMaestroBackend.CleanupResidual();
    Thread.Sleep(1000);
    Console.WriteLine("[2/6] 已清理残留虚拟手柄");

    // ② 现在 XInput 上只剩 Elite 2，探测其槽位
    int padIndex = GamepadSource.FindConnectedIndex();
    if (padIndex < 0)
        Console.WriteLine("[3/6] [警告] 未检测到 XInput 手柄，仅捕获键鼠");
    else
        Console.WriteLine($"[3/6] Elite 2 槽位 {padIndex}");
    var pad = new GamepadSource((uint)Math.Max(0, padIndex));

    // ③ 创建虚拟手柄
    using var backend = new HidMaestroBackend();

    Console.WriteLine("[4/6] 安装驱动 + 创建虚拟手柄...");
    backend.Start();
    Console.WriteLine("[4/6] 虚拟手柄已创建（xbox-series-xs-bt）");

    // 诊断：打印当前所有 XInput 槽位，确认 Elite 2 与虚拟手柄各占哪个槽
    Console.Write("[诊断] XInput 槽位：");
    for (uint i = 0; i < 4; i++)
    {
        var st = new XInputState();
        if (XInput.GetState(i, ref st) == XInput.ErrorSuccess)
            Console.Write($" #{i}=连接");
    }
    Console.WriteLine();

    bool once = args.Contains("--once");
    if (once)
    {
        Console.WriteLine("[--once] 8 秒后退出...");
        Thread.Sleep(8000);
        Console.WriteLine("[--once] 退出");
        return;
    }

    // ④ 装钩子
    using var capture = new InputCapture();
    capture.KeyStateChanged += (vk, down) =>
        Console.WriteLine($"[key] vk=0x{vk:X2} {(down ? "down" : "up")}");

    // 鼠标可视化：移动按 100ms 聚合节流，按钮/滚轮即时
    var mouseSw = System.Diagnostics.Stopwatch.StartNew();
    long mouseIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 10;
    long lastMouseTick = 0;
    int accDx = 0, accDy = 0;
    capture.MouseInput += e =>
    {
        if (e.Button != 0)
            Console.WriteLine($"[mouse] {MouseButtonName(e.Button)} {(e.ButtonDown ? "down" : "up")}");
        else if (e.WheelDelta != 0)
            Console.WriteLine($"[mouse] wheel {e.WheelDelta}");
        else
        {
            accDx += e.DeltaX;
            accDy += e.DeltaY;
            long t = mouseSw.ElapsedTicks;
            if (t - lastMouseTick >= mouseIntervalTicks && (accDx != 0 || accDy != 0))
            {
                lastMouseTick = t;
                Console.WriteLine($"[mouse] d({accDx}, {accDy})");
                accDx = accDy = 0;
            }
        }
    };
    capture.Install();
    Console.WriteLine("[5/6] 钩子已安装");

    // ⑤ Ctrl+C 干净退出：CancellationTokenSource 协调关停
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var forward = new Thread(() => ForwardLoop(pad, backend, capture, cts.Token)) { IsBackground = true };
    forward.Start();
    Console.WriteLine("[6/6] 转发线程已启动，进入消息泵（Ctrl+C 退出）...");

    while (!cts.IsCancellationRequested)
    {
        while (NativeMethods.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
        {
            if (msg.message == NativeMethods.WM_QUIT)
            {
                cts.Cancel();
                break;
            }
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
        Thread.Sleep(1);
    }
    Console.WriteLine("[退出] 正在清理...");
    forward.Join();  // 等转发线程停止后再 Dispose，避免 ObjectDisposedException
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Environment.ExitCode = 1;
}

static string MouseButtonName(int b) => b switch
{
    1 => "L", 2 => "R", 3 => "M", 4 => "X1", 5 => "X2", _ => "?"
};

static void ForwardLoop(GamepadSource pad, HidMaestroBackend backend, InputCapture capture, CancellationToken ct)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    long intervalTicks = System.Diagnostics.Stopwatch.Frequency / 500;  // 2ms @ 500Hz
    long spinThreshold = System.Diagnostics.Stopwatch.Frequency / 1000; // 1ms
    long nextTick = sw.ElapsedTicks + intervalTicks;
    bool lastCapturing = false;
    XInputButtons prevButtons = XInputButtons.None;
    bool axesActive = false;
    long lastAxisTick = 0;
    long axisIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 10; // 100ms
    bool hasPacket = false;
    uint lastPacket = 0;
    long lastSubmitTick = 0;
    long minSubmitTicks = System.Diagnostics.Stopwatch.Frequency / 125; // 8ms：最小回报率 125Hz

    var padButtons = new (XInputButtons Flag, string Name)[]
    {
        (XInputButtons.A, "A"), (XInputButtons.B, "B"), (XInputButtons.X, "X"), (XInputButtons.Y, "Y"),
        (XInputButtons.DPadUp, "DpadUp"), (XInputButtons.DPadDown, "DpadDown"),
        (XInputButtons.DPadLeft, "DpadLeft"), (XInputButtons.DPadRight, "DpadRight"),
        (XInputButtons.LeftShoulder, "LB"), (XInputButtons.RightShoulder, "RB"),
        (XInputButtons.LeftThumb, "L3"), (XInputButtons.RightThumb, "R3"),
        (XInputButtons.Start, "Start"), (XInputButtons.Back, "Back"),
    };

    while (!ct.IsCancellationRequested)
    {
        long now = sw.ElapsedTicks;
        long remaining = nextTick - now;
        if (remaining > 0)
        {
            // 混合等待：剩余 >1ms 用 Sleep(1) 释放 CPU（timeBeginPeriod(1) 已设，粒度~1ms），
            // 最后 <1ms 精确自旋。避免满核自旋饿死系统/驱动线程。
            if (remaining > spinThreshold)
                Thread.Sleep(1);
            else
                System.Threading.SpinWait.SpinUntil(() => sw.ElapsedTicks >= nextTick);
            continue;
        }
        nextTick = now + intervalTicks;

        var snap = pad.Poll();
        if (snap is { } s)
        {
            // 可视化：手柄按键按下/释放
            var cur = s.Buttons;
            var changed = cur ^ prevButtons;
            if (changed != XInputButtons.None)
            {
                foreach (var (flag, name) in padButtons)
                    if (changed.HasFlag(flag))
                        Console.WriteLine($"[pad] {name} {(cur.HasFlag(flag) ? "down" : "up")}");
                prevButtons = cur;
            }

            // 可视化：摇杆/扳机按 100ms 节流（避免刷屏）
            bool active = MathF.Abs(s.LeftX) >= 0.05f || MathF.Abs(s.LeftY) >= 0.05f
                       || MathF.Abs(s.RightX) >= 0.05f || MathF.Abs(s.RightY) >= 0.05f
                       || s.LeftTrigger >= 0.05f || s.RightTrigger >= 0.05f;
            if (active)
            {
                if (now - lastAxisTick >= axisIntervalTicks)
                {
                    lastAxisTick = now;
                    Console.WriteLine($"[pad] L({s.LeftX:0.00}, {s.LeftY:0.00}) R({s.RightX:0.00}, {s.RightY:0.00}) LT={s.LeftTrigger:0.00} RT={s.RightTrigger:0.00}");
                }
            }
            else if (axesActive)
            {
                Console.WriteLine("[pad] 摇杆/扳机归中");
            }
            axesActive = active;

            var state = new VirtualGamepadState
            {
                A = s.Buttons.HasFlag(XInputButtons.A),
                B = s.Buttons.HasFlag(XInputButtons.B),
                X = s.Buttons.HasFlag(XInputButtons.X),
                Y = s.Buttons.HasFlag(XInputButtons.Y),
                DPadUp = s.Buttons.HasFlag(XInputButtons.DPadUp),
                DPadDown = s.Buttons.HasFlag(XInputButtons.DPadDown),
                DPadLeft = s.Buttons.HasFlag(XInputButtons.DPadLeft),
                DPadRight = s.Buttons.HasFlag(XInputButtons.DPadRight),
                LeftBumper = s.Buttons.HasFlag(XInputButtons.LeftShoulder),
                RightBumper = s.Buttons.HasFlag(XInputButtons.RightShoulder),
                LeftStick = s.Buttons.HasFlag(XInputButtons.LeftThumb),
                RightStick = s.Buttons.HasFlag(XInputButtons.RightThumb),
                Start = s.Buttons.HasFlag(XInputButtons.Start),
                Back = s.Buttons.HasFlag(XInputButtons.Back),
                LeftX = s.LeftX, LeftY = s.LeftY,
                RightX = s.RightX, RightY = s.RightY,
                LeftTrigger = s.LeftTrigger, RightTrigger = s.RightTrigger,
            };
            // 按变化提交 + 最小回报率 125Hz：新输入包一到立即提交（最低延迟）；
            // 若超过 8ms 无新包（空闲/蓝牙降速），补发一次保持 ≥125Hz。
            bool newPacket = !hasPacket || s.PacketNumber != lastPacket;
            bool minElapsed = now - lastSubmitTick >= minSubmitTicks;
            if (newPacket || minElapsed)
            {
                backend.Submit(state);
                lastSubmitTick = now;
                if (newPacket)
                {
                    lastPacket = s.PacketNumber;
                    hasPacket = true;
                }
            }
        }

        if (capture.Capturing != lastCapturing)
        {
            lastCapturing = capture.Capturing;
            if (lastCapturing)
            {
                // 开启捕获时震动一下作为确认（150ms 后停止）
                pad.SetVibration(0.8f, 0.8f);
                _ = Task.Run(async () => { await Task.Delay(150); pad.SetVibration(0f, 0f); });
            }
            Console.WriteLine(lastCapturing ? "[捕获] 开启（键鼠全拦截）" : "[捕获] 关闭（键鼠恢复）");
        }
    }
}

static void RunMathTests()
{
    int pass = 0, fail = 0;
    void Check(string name, bool cond)
    {
        if (cond) { pass++; Console.WriteLine($"  [PASS] {name}"); }
        else { fail++; Console.WriteLine($"  [FAIL] {name}"); }
    }

    Console.WriteLine("=== 摇杆死区（radial）===");
    var d = new DeadzoneConfig(0.1f, DeadzoneType.Radial);
    var (x, y) = StickMath.ApplyDeadzone(0.5f, 0.5f, d);
    Check("对角 (0.5,0.5) 重映射到 ~0.477", MathF.Abs(x - 0.477f) < 0.005f && MathF.Abs(y - 0.477f) < 0.005f);
    var (x0, y0) = StickMath.ApplyDeadzone(0.05f, 0.05f, d);
    Check("死区内 (0.05,0.05) 归零", x0 == 0f && y0 == 0f);

    Console.WriteLine("=== 键盘→摇杆 方形/圆形 ===");
    var (cx, cy) = StickMath.ApplyShape(1f, 1f, StickShape.Circular);
    Check("圆形 W+D 对角 → 0.707", MathF.Abs(cx - 0.7071f) < 0.001f && MathF.Abs(cy - 0.7071f) < 0.001f);
    var (sx, sy) = StickMath.ApplyShape(1f, 1f, StickShape.Square);
    Check("方形 W+D 对角保持 (1,1)", sx == 1f && sy == 1f);

    Console.WriteLine("=== 扳机分段（含迟滞）===");
    var segs = new[]
    {
        new TriggerSegment(0f, 0.33f, 0),
        new TriggerSegment(0.33f, 0.66f, 1),
        new TriggerSegment(0.66f, 1.01f, 2),
    };
    int last = -1;
    Check("t=0.5 → 段 1", StickMath.SegmentIndex(0.5f, segs, ref last, 0.05f) == 1);
    Check("t=0.68 迟滞保持段 1", StickMath.SegmentIndex(0.68f, segs, ref last, 0.05f) == 1);
    Check("t=0.72 越界 → 段 2", StickMath.SegmentIndex(0.72f, segs, ref last, 0.05f) == 2);

    Console.WriteLine($"结果：{pass} 通过，{fail} 失败");
    if (fail > 0) Environment.Exit(1);
}
