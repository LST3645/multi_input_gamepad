#:project ../../src/multi_input_gamepad.Core/multi_input_gamepad.Core.csproj
using MultiInputGamepad.Core;

int fails = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok) fails++;
}

MappingOutput Out(GamepadTarget target, int percent = 100, int delayMs = 0) =>
    new() { Target = target, Percent = percent, DelayMs = delayMs };

var engine = new MappingEngine();

// 1) 键盘 → 按键，立即
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x41, Outputs = { Out(GamepadTarget.A) } },
});
engine.OnKey(0x41, true);
Check("键盘→A：按下即输出", engine.Evaluate(true).Buttons == GamepadButton.A);
engine.OnKey(0x41, false);
Check("键盘→A：松开即释放", engine.Evaluate(true).Buttons == GamepadButton.None);

// 2) 规则级延迟 + 提前松开取消
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x42, DelayMs = 100, Outputs = { Out(GamepadTarget.B) } },
});
engine.OnKey(0x42, true);
Check("延迟 100ms：未到点不输出", engine.Evaluate(true).Buttons == GamepadButton.None);
engine.OnKey(0x42, false);
Thread.Sleep(120);
Check("延迟 100ms：提前松开不输出", engine.Evaluate(true).Buttons == GamepadButton.None);

engine.OnKey(0x42, true);
Thread.Sleep(130);
Check("延迟 100ms：到点输出 B", engine.Evaluate(true).Buttons == GamepadButton.B);
engine.OnKey(0x42, false);

// 3) 逐项累加延迟：A(100) + B(0) + Y(200) → A、B 在 100ms，Y 在 300ms
engine.SetRules(new[]
{
    new MappingRule
    {
        Source = MappingSource.Keyboard, KeyCode = 0x43,
        Outputs = { Out(GamepadTarget.A, 100, 100), Out(GamepadTarget.B), Out(GamepadTarget.Y, 100, 200) },
    },
});
engine.OnKey(0x43, true);
Check("累加延迟：90ms 内无输出（等待 100ms）", engine.Evaluate(true).Buttons == GamepadButton.None);
Thread.Sleep(130);
Check("累加延迟：130ms 时 A+B 同一步", engine.Evaluate(true).Buttons == (GamepadButton.A | GamepadButton.B));
Thread.Sleep(200);
Check("累加延迟：330ms 时加上 Y", engine.Evaluate(true).Buttons == (GamepadButton.A | GamepadButton.B | GamepadButton.Y));
engine.OnKey(0x43, false);
Check("累加延迟：松开全释放", engine.Evaluate(true).Buttons == GamepadButton.None);

// 3b) A(100) + Y(200) + B(0) → A 在 100ms，Y 与 B 同在 300ms
engine.SetRules(new[]
{
    new MappingRule
    {
        Source = MappingSource.Keyboard, KeyCode = 0x46,
        Outputs = { Out(GamepadTarget.A, 100, 100), Out(GamepadTarget.Y, 100, 200), Out(GamepadTarget.B) },
    },
});
engine.OnKey(0x46, true);
Thread.Sleep(130);
Check("累加延迟（换序）：130ms 只有 A", engine.Evaluate(true).Buttons == GamepadButton.A);
Thread.Sleep(200);
Check("累加延迟（换序）：330ms 时 Y+B 同一步",
    engine.Evaluate(true).Buttons == (GamepadButton.A | GamepadButton.Y | GamepadButton.B));
engine.OnKey(0x46, false);

// 3c) 扳机百分比随步进
engine.SetRules(new[]
{
    new MappingRule
    {
        Source = MappingSource.Keyboard, KeyCode = 0x47,
        Outputs = { Out(GamepadTarget.A), Out(GamepadTarget.RightTrigger, 90, 100) },
    },
});
engine.OnKey(0x47, true);
Check("步进扳机：0ms 只有 A", engine.Evaluate(true) is { Buttons: GamepadButton.A, RightTrigger: 0f });
Thread.Sleep(130);
var rt = engine.Evaluate(true);
Check("步进扳机：130ms 加上 RT 0.9",
    rt.Buttons == GamepadButton.A && Math.Abs(rt.RightTrigger - 0.9f) < 0.001f);
engine.OnKey(0x47, false);

// 4) 连按：延迟到点后整组脉冲
engine.SetRules(new[]
{
    new MappingRule
    {
        Source = MappingSource.Keyboard, KeyCode = 0x45, DelayMs = 400,
        Outputs = { Out(GamepadTarget.A), Out(GamepadTarget.B) },
        RepeatEnabled = true, RepeatIntervalMs = 100,
    },
});
engine.OnKey(0x45, true);
bool earlyOff = true;
var sw = System.Diagnostics.Stopwatch.StartNew();
while (sw.ElapsedMilliseconds < 250) // 延迟 400ms 内不得输出
{
    if (engine.Evaluate(true).Buttons != GamepadButton.None) earlyOff = false;
    Thread.Sleep(5);
}
Check("连按：延迟未到无输出", earlyOff);

int on = 0, off = 0;
for (int i = 0; i < 45; i++)
{
    if (engine.Evaluate(true).Buttons == (GamepadButton.A | GamepadButton.B)) on++;
    else off++;
    Thread.Sleep(10);
}
engine.OnKey(0x45, false);
Check($"连按：整组同时脉冲（按下 {on} / 松开 {off}）", on > 0 && off > 0);

// 5) 鼠标移动：水平 → 左摇杆X，垂直 → 左摇杆Y（各自灵敏度）
var axisRule = new MappingRule
{
    Source = MappingSource.MouseAxis,
    AxisX = (int)StickAxis.LeftX,
    AxisY = (int)StickAxis.LeftY,
    MoveAmountX = 1f, DisplacementX = 0.1f,
    MoveAmountY = 1f, DisplacementY = 0.2f,
};
engine.SetRules(new[] { axisRule });
engine.Evaluate(true);
Thread.Sleep(5);
engine.OnMouseMove(5, 0);
float first = engine.Evaluate(true).LeftX;
Check($"输入平滑：单拍只吃掉一部分位移（LeftX={first:0.000}，满值应为 0.5）", first > 0.01f && first < 0.35f);

// 连续输入应能推到接近满偏
float peak = 0f;
for (int i = 0; i < 30; i++)
{
    engine.OnMouseMove(10, 0);
    peak = Math.Max(peak, engine.Evaluate(true).LeftX);
    Thread.Sleep(2);
}
Check($"持续输入可接近满偏（峰值 {peak:0.00}）", peak > 0.5f);

// 垂直方向：鼠标下移取负，且灵敏度翻倍
engine.OnMouseMove(0, 5);
float up = engine.Evaluate(true).LeftY;
Check($"垂直取负且增益 0.2（LeftY={up:0.00} 应为负）", up < -0.02f);

// 回中
for (int i = 0; i < 60; i++) { engine.Evaluate(true); Thread.Sleep(10); }
var settled = engine.Evaluate(true);
Check("停止约 600ms 后回中", Math.Abs(settled.LeftX) < 0.01f && Math.Abs(settled.LeftY) < 0.01f);

// 6) 只用垂直轴
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.MouseAxis, AxisX = -1, AxisY = (int)StickAxis.RightY, DisplacementY = 0.1f },
});
engine.Evaluate(true);
engine.OnMouseMove(50, 0);
Check("未启用的水平轴无输出", engine.Evaluate(true) is { RightX: 0f, LeftX: 0f, LeftY: 0f, RightY: 0f });

// 6b) 匀速画圆：输出应是同样形状的圆（幅度稳定、不被积分打满）
engine.SetRules(new[]
{
    new MappingRule
    {
        Source = MappingSource.MouseAxis,
        AxisX = (int)StickAxis.RightX, AxisY = (int)StickAxis.RightY,
        MoveAmountX = 1f, DisplacementX = 0.1f,
        MoveAmountY = 1f, DisplacementY = 0.1f,
    },
});
var ring = new List<(float X, float Y)>();
const double circleRate = 0.4;   // 单位/ms（约 400 单位/秒）
var clock = System.Diagnostics.Stopwatch.StartNew();
long lastMs = clock.ElapsedMilliseconds;
engine.Evaluate(true);

for (int i = 0; i < 48; i++)
{
    Thread.Sleep(5);
    long nowMs = clock.ElapsedMilliseconds;
    double dt = Math.Max(1, nowMs - lastMs);
    lastMs = nowMs;

    double theta = i * Math.PI / 24; // 两圈
    engine.OnMouseMove(
        (int)Math.Round(circleRate * Math.Cos(theta) * dt),
        (int)Math.Round(circleRate * Math.Sin(theta) * dt));

    var s = engine.Evaluate(true);
    ring.Add((s.RightX, s.RightY));
}

float minR = float.MaxValue, maxR = 0f;
for (int i = 16; i < ring.Count; i++)   // 跳过前 1/3 圈的起始瞬态
{
    var (x, y) = ring[i];
    float r = MathF.Sqrt(x * x + y * y);
    minR = Math.Min(minR, r);
    maxR = Math.Max(maxR, r);
}
Check($"画圆：稳态半径稳定（{minR:0.00}~{maxR:0.00}，不饱和、形状不变）",
    maxR < 0.9f && minR > 0.15f && maxR - minR < 0.15f);
Check("画圆：四个方向都出现", ring.Any(p => p.X > 0.15f) && ring.Any(p => p.X < -0.15f) &&
                              ring.Any(p => p.Y > 0.15f) && ring.Any(p => p.Y < -0.15f));
// 7) 鼠标按键
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.MouseButton, KeyCode = 1, Outputs = { Out(GamepadTarget.X) } },
});
engine.OnMouseButton(1, true);
Check("鼠标左键 → X", engine.Evaluate(true).Buttons == GamepadButton.X);
engine.OnMouseButton(1, false);
Check("松开鼠标左键释放 X", engine.Evaluate(true).Buttons == GamepadButton.None);

// 8) 滚轮：持续滚动视为按住，停滚超时松开
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.MouseWheel, KeyCode = 0, Outputs = { Out(GamepadTarget.Y) } },
});
engine.OnWheel(120);
Check("滚轮上 → Y 按下", engine.Evaluate(true).Buttons == GamepadButton.Y);
Thread.Sleep(150);
Check("停滚 150ms → 松开", engine.Evaluate(true).Buttons == GamepadButton.None);

engine.OnWheel(120);
Thread.Sleep(60);
engine.OnWheel(120);
Check("持续滚动期间保持按住", engine.Evaluate(true).Buttons == GamepadButton.Y);

// 9) 捕获关闭时输出恒空
engine.SetRules(new[]
{
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x41, Outputs = { Out(GamepadTarget.A) } },
});
engine.OnKey(0x41, true);
Check("enabled=false 时无输出", engine.Evaluate(false).Buttons == GamepadButton.None);
Check("enabled=true 时恢复输出", engine.Evaluate(true).Buttons == GamepadButton.A);

// 10) 模型默认值
var defaults = new MappingRule();
Check("默认 位移量 X/Y = 0.02", Math.Abs(defaults.DisplacementX - 0.02f) < 1e-6f && Math.Abs(defaults.DisplacementY - 0.02f) < 1e-6f);
Check("默认 两个轴都不使用（非轴向规则不占用轴）", defaults.AxisX == -1 && defaults.AxisY == -1);

// —— M2b ——

// 11) 摇杆方向输出：W/A/S/D → 左摇杆 ± 满偏；同轴相抵、对角合成
var dir = new MappingEngine();
dir.SetRules(new[]
{
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x57, Outputs = { Out(GamepadTarget.LeftYPlus) } },   // W
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x53, Outputs = { Out(GamepadTarget.LeftYMinus) } },  // S
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x41, Outputs = { Out(GamepadTarget.LeftXMinus) } },  // A
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x44, Outputs = { Out(GamepadTarget.LeftXPlus) } },   // D
});

dir.OnKey(0x57, true);
var w = dir.Evaluate(true);
Check("方向：W → 左Y=+1、左X=0", w.LeftY == 1f && w.LeftX == 0f);

dir.OnKey(0x44, true);
var wd = dir.Evaluate(true);
Check($"方向：W+D → 对角 (1,1)", wd.LeftX == 1f && wd.LeftY == 1f);

dir.OnKey(0x41, true);
var wda = dir.Evaluate(true);
Check("方向：W+D+A → 左X 相抵为 0、左Y 仍 1", wda.LeftX == 0f && wda.LeftY == 1f);
dir.OnKey(0x41, false);

dir.OnKey(0x53, true);
Check("方向：W+S 同轴相抵 → 左Y 0", dir.Evaluate(true).LeftY == 0f);

dir.OnKey(0x57, false); dir.OnKey(0x53, false); dir.OnKey(0x44, false);
Check("方向：全部松开回中", dir.Evaluate(true) is { LeftX: 0f, LeftY: 0f });

// 12) 物理+合成合并：逐轴取 |大|，再按开关裁剪（圆形归一化 / 方形）
var merge = StickMath.ComposeStick(0.5f, 0.2f, 0.1f, 0.9f, StickShape.Square);
Check($"合并：逐轴取 |大| (0.5,0.2)+(0.1,0.9) → (0.5,0.9)",
    Math.Abs(merge.X - 0.5f) < 1e-6f && Math.Abs(merge.Y - 0.9f) < 1e-6f);

var independent = StickMath.ComposeStick(0.9f, 0.1f, 0.1f, 0.9f, StickShape.Square);
Check($"合并：两轴独立取 |大| → (0.9,0.9)（不处理混合角）",
    Math.Abs(independent.X - 0.9f) < 1e-6f && Math.Abs(independent.Y - 0.9f) < 1e-6f);

var circle = StickMath.ComposeStick(0f, 0f, 1f, 1f, StickShape.Circular);
Check($"归一化开：对角 (1,1) → (0.707,0.707)",
    Math.Abs(circle.X - 0.70710678f) < 1e-4f && Math.Abs(circle.Y - 0.70710678f) < 1e-4f);

var square = StickMath.ComposeStick(0f, 0f, 1f, 1f, StickShape.Square);
Check("归一化关：对角保持 (1,1)", square.X == 1f && square.Y == 1f);

var physicalOnly = StickMath.ComposeStick(0.6f, -0.4f, 0f, 0f, StickShape.Circular);
Check("无映射时物理值原样保留", Math.Abs(physicalOnly.X - 0.6f) < 1e-6f && Math.Abs(physicalOnly.Y + 0.4f) < 1e-6f);

// 13) 扳机多源取大：两键绑不同百分比 + 同时按下 → 只取最大，不叠加
var trig = new MappingEngine();
trig.SetRules(new[]
{
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x31, Outputs = { Out(GamepadTarget.RightTrigger, 20) } },
    new MappingRule { Source = MappingSource.Keyboard, KeyCode = 0x32, Outputs = { Out(GamepadTarget.RightTrigger, 90) } },
});
trig.OnKey(0x31, true);
Check("扳机：只按 20% 键 → 0.2", Math.Abs(trig.Evaluate(true).RightTrigger - 0.2f) < 1e-6f);
trig.OnKey(0x32, true);
Check("扳机：两键同时 → 取 90%（不叠加）", Math.Abs(trig.Evaluate(true).RightTrigger - 0.9f) < 1e-6f);
trig.OnKey(0x32, false);
Check("扳机：松开 90% 键 → 回落到 0.2", Math.Abs(trig.Evaluate(true).RightTrigger - 0.2f) < 1e-6f);
trig.OnKey(0x31, false);
Check("扳机：全部松开 → 0", trig.Evaluate(true).RightTrigger == 0f);

// 14) 键盘黑名单：修饰键 / 系统键禁止参与映射
Check("黑名单：Tab/Esc/Caps/左右 Shift·Ctrl·Alt/Win/Menu",
    new[] { 0x09, 0x1B, 0x14, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0x5D }.All(KeyBlacklist.Contains));
Check("黑名单：NumLock/ScrollLock/Pause/PrintScreen/音量/计算器",
    new[] { 0x90, 0x91, 0x13, 0x2C, 0xAD, 0xAE, 0xAF, 0xB7 }.All(KeyBlacklist.Contains));
Check("黑名单：方向键上方九键（Ins/Del/Home/End/PgUp/PgDn 同区）",
    new[] { 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22 }.All(KeyBlacklist.Contains));
Check("黑名单：方向键本身仍可映射", new[] { 0x25, 0x26, 0x27, 0x28 }.All(vk => !KeyBlacklist.Contains(vk)));
Check("黑名单：普通键不受影响（W/A/F1/Space/NumPad0）",
    new[] { 0x57, 0x41, 0x70, 0x20, 0x60 }.All(vk => !KeyBlacklist.Contains(vk)));

// 15) 物理摇杆径向死区（参数来自配置，默认 R_ON 0.12 / R_OFF 0.07 / 3 / 3 / 0.02 / 0.04）
var dzCfg = StickDeadzoneConfig.Default;
var dz = new StickDeadzoneState();

var dzZero = StickDeadzone.Apply(dzCfg, ref dz, 0f, 0f);
Check("死区：静止 (0,0) → 归零", dzZero.X == 0f && dzZero.Y == 0f);

dz = default;
var dzEdge = StickDeadzone.Apply(dzCfg, ref dz, 0.12f, 0f);
Check("死区：r=0.12 边界不触发（须 > R_ON）", dzEdge.X == 0f && dzEdge.Y == 0f);

dz = default;
var dzOut = StickDeadzone.Apply(dzCfg, ref dz, 0.13f, 0f);
Check("死区：r>0.12 立即输出原值", Math.Abs(dzOut.X - 0.13f) < 1e-6f && dzOut.Y == 0f);

// Live 后回中需连续 3 包 r ≤ R_OFF
dz = default;
StickDeadzone.Apply(dzCfg, ref dz, 0.13f, 0f);
var dzBack1 = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
var dzBack2 = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
var dzBack3 = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
Check("死区：回中需连续 3 包（前 2 包仍输出、第 3 包归零）",
    Math.Abs(dzBack1.X - 0.05f) < 1e-6f && Math.Abs(dzBack2.X - 0.05f) < 1e-6f && dzBack3.X == 0f);

// 死区内同向慢推 3 包（每包 0.02，累计 0.06）→ 放行
dz = default;
StickDeadzone.Apply(dzCfg, ref dz, 0f, 0f);
var dzSlow1 = StickDeadzone.Apply(dzCfg, ref dz, 0f, 0.02f);
var dzSlow2 = StickDeadzone.Apply(dzCfg, ref dz, 0f, 0.04f);
var dzSlow3 = StickDeadzone.Apply(dzCfg, ref dz, 0f, 0.06f);
Check("死区：死区内同向慢推 3 包 → 第 3 包放行（输出 0.06）",
    dzSlow1.Y == 0f && dzSlow2.Y == 0f && Math.Abs(dzSlow3.Y - 0.06f) < 1e-6f);

// 原地来回抖动不触发
dz = default;
StickDeadzone.Apply(dzCfg, ref dz, 0.03f, 0f);
var dzJit1 = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
var dzJit2 = StickDeadzone.Apply(dzCfg, ref dz, 0.03f, 0f);
var dzJit3 = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
var dzJit4 = StickDeadzone.Apply(dzCfg, ref dz, 0.03f, 0f);
Check("死区：原地来回抖动 → 不放行",
    dzJit1.X == 0f && dzJit2.X == 0f && dzJit3.X == 0f && dzJit4.X == 0f);

// 单包尖刺不触发
dz = default;
StickDeadzone.Apply(dzCfg, ref dz, 0.01f, 0f);
var dzSpike = StickDeadzone.Apply(dzCfg, ref dz, 0.06f, 0f);
var dzSpikeAfter = StickDeadzone.Apply(dzCfg, ref dz, 0.01f, 0f);
Check("死区：单包尖刺 → 不放行", dzSpike.X == 0f && dzSpikeAfter.X == 0f);

// Reset 后首包不做同向判定
dz = default;
var dzFirst = StickDeadzone.Apply(dzCfg, ref dz, 0.05f, 0f);
Check("死区：Reset 后首包按死区处理（输出 0）", dzFirst.X == 0f);

// 配置生效：更小的 R_ON 立即放行
var tightCfg = new StickDeadzoneConfig { RadiusOn = 0.05f, RadiusOff = 0.03f };
tightCfg.Sanitize();
dz = default;
var dzTight = StickDeadzone.Apply(tightCfg, ref dz, 0.06f, 0f);
Check("死区：配置生效（R_ON=0.05 时 r=0.06 立即输出）", Math.Abs(dzTight.X - 0.06f) < 1e-6f);

// 越界配置被 Sanitize 夹取
var badCfg = new StickDeadzoneConfig
{
    RadiusOn = -1f, RadiusOff = 0.5f, OutConfirmPackets = 0,
    MoveStreakPackets = 0, MinStep = 9f, MinAccum = 9f,
};
badCfg.Sanitize();
Check("死区配置：越界值被夹取（R_ON≥0、R_OFF≤R_ON、包数≥1、位移≤1）",
    badCfg.RadiusOn == 0f && badCfg.RadiusOff == 0f && badCfg.OutConfirmPackets == 1 &&
    badCfg.MoveStreakPackets == 1 && badCfg.MinStep == 1f && badCfg.MinAccum == 1f);

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails == 0 ? 0 : 1;
