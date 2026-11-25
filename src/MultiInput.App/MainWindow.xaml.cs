using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MultiInput.App.ViewModels;
using MultiInput.Core;
using MultiInput.Input;
using MultiInput.Output;

namespace MultiInput.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly InputState _state = new();
    private readonly MappingEngine _engine = new();

    private volatile InputCapture? _capture;
    private GamepadSource? _pad;
    private HidMaestroBackend? _backend;
    private HidHideController? _hidHide;

    /// <summary>HidHide 自愈标记：存在说明上次没恢复，本次退出必须关闭 cloak。</summary>
    private static string HidHideMarkerPath => Path.Combine(ConfigStore.Root, "hidhide.state");

    /// <summary>游戏震动：左/右主电机字节（高 8 位=左，低 8 位=右）。HIDMaestro 回调线程写，转发线程每拍读。</summary>
    private long _rumbleMotorBytes;

    /// <summary>游戏震动的失效时刻（Environment.TickCount64 毫秒）。Xbox 震动包自带时长，到点必须归零。</summary>
    private long _rumbleUntilMs;
    private CancellationTokenSource? _cts;
    private Thread? _hookThread;
    private Thread? _forwardThread;
    private DispatcherTimer? _uiTimer;
    private Action<int>? _keyProbe;
    private bool _started;

    /// <summary>「归一化摇杆」开关：true = 圆形归一化（默认），false = 方形不归一化。转发线程读取。</summary>
    private volatile bool _normalizeSticks = true;

    /// <summary>当前生效的死区参数（转发线程每拍读取，切换配置后立即生效）。</summary>
    private volatile StickDeadzoneConfig _deadzoneConfig = StickDeadzoneConfig.Default;

    /// <summary>死区参数版本：变化时转发线程重置摇杆状态机，避免旧状态绕过新阈值。</summary>
    private int _deadzoneRevision;

    /// <summary>当前激活配置（映射 + 归一化 + 死区），落盘为 profiles\&lt;名称&gt;.json。</summary>
    private ProfileConfig _config = new();

    /// <summary>本次会话导入过的配置：配置名 → 导入时的原始文件名（退出时写 cache 备份）。</summary>
    private readonly Dictionary<string, string> _importedFiles = new();

    /// <summary>刷新配置下拉框时抑制 SelectionChanged。</summary>
    private bool _suppressProfileChange;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.MappingListChanged += OnMappingListChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnWindowStateChanged;
    }

    /// <summary>最小化时停止 UI 刷新（省掉属性写入与渲染失效），恢复时立即刷新一帧。</summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_uiTimer is null) return;

        if (WindowState == WindowState.Minimized)
        {
            _uiTimer.Stop();
            return;
        }

        if (!_uiTimer.IsEnabled)
        {
            _uiTimer.Start();
            OnUiTick(null, EventArgs.Empty);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        // 旧版 mappings.json + settings.json → profiles\default.json（仅首次）
        ConfigStore.MigrateLegacyIfNeeded();
        ConfigStore.EnsureDefaultProfile();   // default 是稳定兜底：缺失时静默补建

        string active = ConfigStore.LoadActiveName();
        var names = ConfigStore.ListProfiles();
        if (!names.Any(name => string.Equals(name, active, StringComparison.CurrentCultureIgnoreCase)))
            active = names.FirstOrDefault() ?? ConfigStore.DefaultProfileName;

        if (ConfigStore.Load(active, out string? loadError) is { } loaded)
        {
            _config = loaded;
            if (loadError is not null) _vm.ShowNotice($"配置「{active}」读取失败（{loadError}）。", error: true);
        }
        else
        {
            _config = new ProfileConfig { Name = active };
            if (loadError is not null) _vm.ShowNotice($"配置「{active}」读取失败（{loadError}），已按空配置运行。", error: true);
        }

        ApplyConfig(_config);
        RefreshProfileList(active);
        ConfigStore.SaveActiveName(active);

        // HidHide：程序运行期间隐藏物理手柄，游戏只枚举到虚拟手柄（消除同一按键来自两台设备的连击）。
        // 隐藏只对之后新打开的句柄生效，所以必须在启动时就做，而不是等开启捕获。
        _hidHide = HidHideController.TryCreate();
        if (_hidHide is null)
            _vm.ShowNotice("未检测到 HidHide：物理与虚拟手柄会同时出现，一次按键可能被算成两次。");
        else
            _hidHide.Apply(HidHideMarkerPath);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _hookThread = new Thread(() => HookLoop(ct)) { IsBackground = true, Name = "InputHooks" };
        _hookThread.Start();

        _forwardThread = new Thread(() => ForwardLoop(ct)) { IsBackground = true, Name = "Forward" };
        _forwardThread.Start();

        // 可视化只是中转工具，整体 30 Hz 足够（键位/鼠标/手柄均为 30 Hz）
        _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

    }

    // —— 配置（多套 + 导入/导出） ——

    /// <summary>把配置应用到运行时：规则 → 列表/引擎，开关与死区 → 运行参数与 UI。</summary>
    private void ApplyConfig(ProfileConfig config)
    {
        _normalizeSticks = config.NormalizeSticks;
        _deadzoneConfig = config.Deadzone;
        Interlocked.Increment(ref _deadzoneRevision);   // 通知转发线程重置死区状态机
        NormalizeCheck.IsChecked = _normalizeSticks;
        _vm.OutputPad.SquareSticks = !_normalizeSticks;

        _vm.ReloadMappings(config.Mappings);
        _engine.SetRules(config.Mappings);
    }

    /// <summary>落盘当前配置（映射取列表最新，开关取运行时值）。</summary>
    private void SaveActiveProfile()
    {
        _config.Mappings = _vm.Mappings.Select(row => row.Rule).ToList();
        _config.NormalizeSticks = _normalizeSticks;
        ConfigStore.Save(_config);
    }

    private void RefreshProfileList(string active)
    {
        _suppressProfileChange = true;
        try
        {
            var items = ConfigStore.ListProfiles()
                .Select(name => new ProfileItem(name, canDelete: !IsDefaultProfile(name)))
                .ToList();

            ProfileBox.ItemsSource = items;
            ProfileBox.SelectedItem = items.FirstOrDefault(item => string.Equals(item.Name, active, StringComparison.CurrentCulture))
                                     ?? items.FirstOrDefault();
        }
        finally
        {
            _suppressProfileChange = false;
        }
    }

    private static bool IsDefaultProfile(string name) =>
        string.Equals(name, ConfigStore.DefaultProfileName, StringComparison.CurrentCultureIgnoreCase);

    private void OnProfileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressProfileChange) return;
        if (ProfileBox.SelectedItem is not ProfileItem item) return;
        if (string.Equals(item.Name, _config.Name, StringComparison.CurrentCulture)) return;

        SaveActiveProfile();     // 先保存当前配置

        if (ConfigStore.Load(item.Name, out string? error) is not { } loaded)
        {
            _vm.ShowNotice($"配置「{item.Name}」读取失败（{error}）。", error: true);
            RefreshProfileList(_config.Name);
            return;
        }

        _config = loaded;
        ApplyConfig(_config);
        ConfigStore.SaveActiveName(item.Name);
        _vm.ShowNotice("");
    }

    /// <summary>下拉列表里点「×」删除配置（默认配置不可删）。</summary>
    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        if (IsDefaultProfile(name))
        {
            _vm.ShowNotice("default 配置不能删除（它是稳定兜底）。", error: true);
            return;
        }

        var confirm = MessageBox.Show(this, $"删除配置「{name}」？该操作不可撤销。", "删除配置",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        bool wasActive = string.Equals(name, _config.Name, StringComparison.CurrentCulture);
        if (!ConfigStore.Delete(name, out string? error))
        {
            _vm.ShowNotice($"删除失败：{error}", error: true);
            return;
        }

        _importedFiles.Remove(name);

        if (wasActive)
        {
            if (ConfigStore.Load(ConfigStore.DefaultProfileName, out _) is { } fallback)
            {
                _config = fallback;
                ApplyConfig(_config);
                ConfigStore.SaveActiveName(_config.Name);
            }
        }

        RefreshProfileList(_config.Name);
        _vm.ShowNotice($"已删除配置「{name}」。");
    }

    /// <summary>下拉列表里点「✎」重命名该配置。</summary>
    private void OnRenameProfileClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not string name) return;

        ProfileBox.IsDropDownOpen = false;   // 先收起列表，避免对话框被弹层遮挡
        RenameProfile(name);
    }

    private void RenameProfile(string name)
    {
        var dialog = new NameDialog(name, ConfigStore.ListProfiles()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultName is not { } newName) return;

        bool wasActive = string.Equals(name, _config.Name, StringComparison.CurrentCulture);
        if (!ConfigStore.Rename(name, newName, out string? error))
        {
            _vm.ShowNotice($"重命名失败：{error}", error: true);
            return;
        }

        // 改名的若是 default，静默补一份空 default 作为兜底
        bool recreatedDefault = false;
        if (!ConfigStore.Exists(ConfigStore.DefaultProfileName))
        {
            ConfigStore.EnsureDefaultProfile();
            recreatedDefault = true;
        }

        if (wasActive)
        {
            _config.Name = newName;
            SaveActiveProfile();
            ConfigStore.SaveActiveName(newName);
        }

        if (_importedFiles.Remove(name, out string? sourceFile)) _importedFiles[newName] = sourceFile;

        RefreshProfileList(wasActive ? newName : _config.Name);
        _vm.ShowNotice(recreatedDefault
            ? $"已重命名为「{newName}」，并新建了空的 default 配置。"
            : $"已重命名为「{newName}」。");
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ConfigStore.CacheDir);   // 导入默认从 cache 目录开始
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入配置",
            Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = ConfigStore.CacheDir,
        };
        if (dialog.ShowDialog(this) != true) return;

        if (ConfigStore.Import(dialog.FileName, out string? error) is not { } imported)
        {
            _vm.ShowNotice($"导入失败：{error}", error: true);
            return;
        }

        SaveActiveProfile();     // 先保存当前配置
        _config = imported;
        _importedFiles[imported.Name] = imported.SourceFileName ?? (imported.Name + ".json");
        ApplyConfig(_config);
        ConfigStore.SaveActiveName(imported.Name);
        RefreshProfileList(imported.Name);
        _vm.ShowNotice($"已导入为配置「{imported.Name}」。");
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出配置",
            Filter = "配置文件 (*.json)|*.json",
            FileName = _config.Name + ".json",
            DefaultExt = ".json",
            OverwritePrompt = true,      // 允许重名，覆盖前询问
            InitialDirectory = DefaultExportDirectory(),
        };
        if (dialog.ShowDialog(this) != true) return;

        SaveActiveProfile();
        _vm.ShowNotice(ConfigStore.Export(_config, dialog.FileName, out string? error)
            ? $"已导出到 {dialog.FileName}"
            : $"导出失败：{error}",
            error: !string.IsNullOrEmpty(error));
    }

    /// <summary>导出默认目录：用户的「下载」文件夹，不存在则回落「我的文档」。</summary>
    private static string DefaultExportDirectory()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        if (Directory.Exists(downloads)) return downloads;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : ConfigStore.Root;
    }

    /// <summary>退出时把导入过的配置按原始文件名备份到 cache 目录。</summary>
    private void BackupImportedConfigs()
    {
        var configs = new List<ProfileConfig>();
        foreach (var pair in _importedFiles)
        {
            if (ConfigStore.Load(pair.Key, out _) is not { } config) continue;
            config.SourceFileName = pair.Value;
            configs.Add(config);
        }

        ConfigStore.BackupImported(configs);
    }

    // —— 钩子线程：必须在装钩子的线程上泵消息，否则钩子约 1s 后被系统跳过/卸载 ——
    private void HookLoop(CancellationToken ct)
    {
        // 系统会阻塞等待钩子回调返回后才继续派发这一次键鼠输入，优先级略高可减少被 UI 线程挤占
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

        var capture = new InputCapture();
        capture.KeyStateChanged += (vk, down) =>
        {
            _state.SetKey(vk, down);
            _engine.OnKey(vk, down);
            if (down) DispatchKeyProbe(vk);
        };
        capture.MouseInput += e =>
        {
            _state.ApplyMouse(e);
            if (e.DeltaX != 0 || e.DeltaY != 0) _engine.OnMouseMove(e.DeltaX, e.DeltaY);
            if (e.Button != 0) _engine.OnMouseButton(e.Button, e.ButtonDown);
            if (e.WheelDelta != 0) _engine.OnWheel(e.WheelDelta);
        };
        capture.CapturingChanged += on => _state.SetCapturing(on);

        // 低层钩子回调要求本线程立即返回，系统才会继续派发这次输入。旧实现「PeekMessage + Sleep(1)」
        // 本机实测 2ms 才泵一次：鼠标吞吐被压到 ~500 事件/秒（3000 次注入要 6 秒，正常 0.4 秒），
        // 全系统鼠标发飘、键盘发涩。改为阻塞在 GetMessage：钩子一到就处理，空闲时不占 CPU。
        uint threadId = NativeMethods.GetCurrentThreadId();
        NativeMethods.PeekMessageW(out _, 0, 0, 0, NativeMethods.PM_NOREMOVE); // 先建队列，保证退出时能唤醒
        using var wake = ct.Register(() => NativeMethods.PostThreadMessageW(threadId, NativeMethods.WM_APP_WAKE, 0, 0));

        bool ok = capture.Install();
        _capture = capture;
        _state.SetCapturing(capture.Capturing);
        SetHookStatus(!ok ? "键鼠钩子：安装失败"
            : capture.RawMouseReady ? "键鼠钩子：已安装"
            : "键鼠钩子：已安装，RawInput 不可用（鼠标映射失效）");

        while (!ct.IsCancellationRequested && NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        capture.Dispose();
        _capture = null;
    }

    // —— 转发线程：500Hz 检测；按变化提交 + 最小 125Hz；映射结果叠加在物理输入之上 ——
    private void ForwardLoop(CancellationToken ct)
    {
        // 500Hz 需要 1ms 定时器粒度：本机实测 Sleep(1) 默认 10.5ms、timeBeginPeriod(1) 后 2ms
        NativeMethods.timeBeginPeriod(1);
        try
        {
            RunForwardLoop(ct);
        }
        finally
        {
            NativeMethods.timeEndPeriod(1); // 程序退出后必须归还，否则整机定时器精度被抬高
        }
    }

    private void RunForwardLoop(CancellationToken ct)
    {
        try { HidMaestroBackend.CleanupResidual(); }
        catch { /* 非管理员/无残留时忽略 */ }

        int idx = GamepadSource.FindConnectedIndex();
        var pad = new GamepadSource((uint)Math.Max(0, idx));
        _pad = pad;

        HidMaestroBackend? backend = null;
        try
        {
            backend = new HidMaestroBackend();
            backend.Rumble += OnRumble;   // 游戏震动 → 物理手柄主电机
            backend.Start();
            _backend = backend;
            SetPadStatus(idx < 0
                ? "虚拟手柄：已启动（物理手柄未连接）"
                : $"虚拟手柄：已启动（物理手柄槽位 {idx}）");
        }
        catch (Exception ex)
        {
            if (backend is not null) backend.Rumble -= OnRumble;
            backend?.Dispose();
            backend = null;
            SetPadStatus($"虚拟手柄：未启动（驱动或权限异常）— {ex.GetType().Name}");
        }

        var sw = Stopwatch.StartNew();
        long intervalTicks = Stopwatch.Frequency / 500;

        // 补发死线 8 ms（125 Hz）必须在一拍内可达，否则只在 8ms 之后第一个拍点触发（实测掉到 ~105/s）
        long minSubmitTicks = Stopwatch.Frequency / 125 - intervalTicks;
        if (minSubmitTicks <= 0) minSubmitTicks = intervalTicks;

        long nextTick = sw.ElapsedTicks + intervalTicks;
        bool hasPacket = false;
        uint lastPacket = 0;
        long lastSubmitTick = 0;
        bool lastCapturing = false;

        // 震动下发：只在目标值变化时调用 XInputSetState；捕获确认震动的截止时刻优先于游戏震动
        long confirmUntilMs = 0;
        float sentRumbleLeft = 0f, sentRumbleRight = 0f;

        // 物理摇杆死区：状态机只在「新 XInput 包」推进，其余 tick 复用上次输出
        var leftZone = new StickDeadzoneState();
        var rightZone = new StickDeadzoneState();
        int zoneRevision = _deadzoneRevision;
        float dzLeftX = 0f, dzLeftY = 0f, dzRightX = 0f, dzRightY = 0f;

        // 高精度等待定时器：一次等待精确睡到死线（Sleep(1) 在本机 ~1.8ms，2ms 死线要睡两次 → 拍长 3.5ms）
        nint waitTimer = NativeMethods.CreateWaitableTimerExW(0, null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);

        while (!ct.IsCancellationRequested)
        {
            long now = sw.ElapsedTicks;
            long waitTicks = nextTick - now;
            if (waitTicks > 0)
            {
                if (waitTimer != 0)
                {
                    long due = -(waitTicks * 10_000_000 / Stopwatch.Frequency);   // 100ns 单位，负数=相对时间
                    if (due == 0) due = -1;
                    if (NativeMethods.SetWaitableTimer(waitTimer, ref due, 0, 0, 0, false))
                        NativeMethods.WaitForSingleObject(waitTimer, NativeMethods.INFINITE);
                    else
                        Thread.Sleep(1);
                }
                else
                {
                    Thread.Sleep(1);   // 高精度定时器不可用时的退路
                }
                continue;
            }
            nextTick = now + intervalTicks;

            var snap = pad.Poll();
            _state.SetPad(snap);

            // 死区参数每拍取一次（切换配置后立即生效）；版本变化则重置状态机并强制重算一次
            var deadzone = _deadzoneConfig;
            int revision = _deadzoneRevision;
            bool zoneDirty = revision != zoneRevision;
            if (zoneDirty)
            {
                zoneRevision = revision;
                leftZone = default;
                rightZone = default;
            }

            bool capturing = _capture?.Capturing ?? false;

            // 物理手柄 → 虚拟手柄；映射结果（仅捕获开启时）叠加其上
            var state = default(VirtualGamepadState);
            bool packetChanged = false;
            if (snap is { } s)
            {
                state = ToVirtualState(s);
                packetChanged = !hasPacket || s.PacketNumber != lastPacket;
                if (packetChanged || zoneDirty)
                {
                    lastPacket = s.PacketNumber;
                    hasPacket = true;

                    // 死区按新包推进（每包一次）；只修物理输入，映射合成值不套
                    (dzLeftX, dzLeftY) = StickDeadzone.Apply(deadzone, ref leftZone, state.LeftX, state.LeftY);
                    (dzRightX, dzRightY) = StickDeadzone.Apply(deadzone, ref rightZone, state.RightX, state.RightY);
                }

                state.LeftX = dzLeftX;
                state.LeftY = dzLeftY;
                state.RightX = dzRightX;
                state.RightY = dzRightY;
            }
            else
            {
                leftZone = default;
                rightZone = default;
                dzLeftX = dzLeftY = dzRightX = dzRightY = 0f;
            }

            ApplyMapping(ref state, _engine.Evaluate(capturing), _normalizeSticks);

            if (packetChanged || now - lastSubmitTick >= minSubmitTicks)
            {

                backend?.Submit(state);
                _state.SetOutputPad(state); // 输出端可视化：记录最近一次提交的状态
                lastSubmitTick = now;
            }

            long nowMs = Environment.TickCount64;

            if (capturing != lastCapturing)
            {
                lastCapturing = capturing;
                // 开启捕获时震 150ms 作为确认；走同一震动下发通道，顺带验证该链路
                if (capturing) confirmUntilMs = nowMs + 150;
            }

            // 游戏震动 → 物理手柄主电机；扳机电机 XInput 不暴露，丢弃（见 §9.4）
            // 只在物理手柄在位时下发：物理手柄不在时 pad.Index 会落到 0，而 0 号槽这时可能是
            // 我们自己的虚拟手柄 —— 写进去经 xinputhid 变成 HID 输出包又回到 OnRumble，自激成长震。
            var (rumbleLeft, rumbleRight) = SelectRumble(
                Interlocked.Read(ref _rumbleMotorBytes), nowMs,
                confirmUntilMs, Interlocked.Read(ref _rumbleUntilMs));
            if (pad.Connected && (rumbleLeft != sentRumbleLeft || rumbleRight != sentRumbleRight))
            {
                pad.SetVibration(rumbleLeft, rumbleRight);
                (sentRumbleLeft, sentRumbleRight) = (rumbleLeft, rumbleRight);
            }
        }

        if (waitTimer != 0) NativeMethods.CloseHandle(waitTimer);
        pad.SetVibration(0f, 0f);   // 退出时停震：游戏退出瞬间的震动不残留到物理手柄
        _state.SetPad(null);
    }

    /// <summary>
    /// HIDMaestro 输出回调线程：振动目标 + 失效时刻。
    /// 左右电机一起打包进一个 64 位值——拆成两个字段读会读到不同帧的左右配对，
    /// 而包是一次性脉冲，错配会一直保持到下次失效。
    /// </summary>
    private void OnRumble(byte left, byte right, int durationMs)
    {
        Interlocked.Exchange(ref _rumbleUntilMs, Environment.TickCount64 + Math.Clamp(durationMs, 0, 2550));
        Interlocked.Exchange(ref _rumbleMotorBytes, ((long)left << 8) | right);
    }

    /// <summary>本拍应下发的震动：捕获确认（150 ms）优先；游戏脉冲在时长内生效，到期归零。</summary>
    internal static (float Left, float Right) SelectRumble(
        long motorBytes, long nowMs, long confirmUntilMs, long rumbleUntilMs)
    {
        if (nowMs < confirmUntilMs) return (0.8f, 0.8f);
        if (nowMs >= rumbleUntilMs) return (0f, 0f);

        return (((motorBytes >> 8) & 0xFF) / 255f, (motorBytes & 0xFF) / 255f);
    }


    private static VirtualGamepadState ToVirtualState(in GamepadSnapshot s) => new()
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
        LeftX = s.LeftX,
        LeftY = s.LeftY,
        RightX = s.RightX,
        RightY = s.RightY,
        LeftTrigger = s.LeftTrigger,
        RightTrigger = s.RightTrigger,
    };

    /// <summary>
    /// 把映射结果合并进输出状态：按键取或、扳机取大；
    /// 摇杆逐轴取 |物理| 与 |合成| 中的大者，再按「归一化摇杆」开关裁剪（圆形/方形）。
    /// </summary>
    private static void ApplyMapping(ref VirtualGamepadState state, in MappingResult mapping, bool normalize)
    {
        var b = mapping.Buttons;
        if ((b & GamepadButton.A) != 0) state.A = true;
        if ((b & GamepadButton.B) != 0) state.B = true;
        if ((b & GamepadButton.X) != 0) state.X = true;
        if ((b & GamepadButton.Y) != 0) state.Y = true;
        if ((b & GamepadButton.LeftBumper) != 0) state.LeftBumper = true;
        if ((b & GamepadButton.RightBumper) != 0) state.RightBumper = true;
        if ((b & GamepadButton.LeftStick) != 0) state.LeftStick = true;
        if ((b & GamepadButton.RightStick) != 0) state.RightStick = true;
        if ((b & GamepadButton.Start) != 0) state.Start = true;
        if ((b & GamepadButton.Back) != 0) state.Back = true;
        if ((b & GamepadButton.DPadUp) != 0) state.DPadUp = true;
        if ((b & GamepadButton.DPadDown) != 0) state.DPadDown = true;
        if ((b & GamepadButton.DPadLeft) != 0) state.DPadLeft = true;
        if ((b & GamepadButton.DPadRight) != 0) state.DPadRight = true;

        state.LeftTrigger = Math.Max(state.LeftTrigger, mapping.LeftTrigger);
        state.RightTrigger = Math.Max(state.RightTrigger, mapping.RightTrigger);

        var shape = normalize ? StickShape.Circular : StickShape.Square;
        (state.LeftX, state.LeftY) = StickMath.ComposeStick(state.LeftX, state.LeftY, mapping.LeftX, mapping.LeftY, shape);
        (state.RightX, state.RightY) = StickMath.ComposeStick(state.RightX, state.RightY, mapping.RightX, mapping.RightY, shape);
    }

    // —— 映射列表 ——

    /// <summary>列表任何增删改后：同步引擎规则 + 落盘当前配置（均在 UI 线程调用）。</summary>
    private void OnMappingListChanged()
    {
        _config.Mappings = _vm.Mappings.Select(row => row.Rule).ToList();
        _engine.SetRules(_config.Mappings);
        ConfigStore.Save(_config);
    }

    private void OnAddMappingClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MappingEditorWindow(null, CurrentRules()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } rule) return;
        _vm.AddMapping(rule);
    }

    private void OnMappingRowClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MappingRowVm row) return;

        var dialog = new MappingEditorWindow(row.Rule, CurrentRules()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } rule) return;
        _vm.ReplaceMapping(row, rule);
    }

    private List<MappingRule> CurrentRules() => _vm.Mappings.Select(row => row.Rule).ToList();

    // —— 可视化中已映射的输入：单击直接打开编辑器 ——

    private void OnKeyVisualClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not KeyVm key) return;
        if (KeyBlacklist.Contains(key.Vk)) { e.Handled = true; return; }   // 系统键/修饰键不参与映射
        OpenEditorFor(MappingSource.Keyboard, key.Vk);
        e.Handled = true;
    }

    private void OnMouseVisualClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;

        if (tag == "wheelup") OpenEditorFor(MappingSource.MouseWheel, 0);
        else if (tag == "wheeldown") OpenEditorFor(MappingSource.MouseWheel, 1);
        else if (int.TryParse(tag, out int button)) OpenEditorFor(MappingSource.MouseButton, button);

        e.Handled = true;
    }

    /// <summary>
    /// 单击可视化里的输入：已映射则编辑该条；未映射则以该输入为种子新建
    /// （编辑器自动切到对应来源页并预选该键，仍可修改）。
    /// </summary>
    private void OpenEditorFor(MappingSource source, int code)
    {
        MappingRowVm? row = _vm.Mappings.FirstOrDefault(
            item => item.Rule.Source == source && item.Rule.KeyCode == code);

        if (row is null)
        {
            var seed = new MappingRule { Source = source, KeyCode = code };
            var newDialog = new MappingEditorWindow(seed, CurrentRules()) { Owner = this };
            if (newDialog.ShowDialog() == true && newDialog.Result is { } created)
                _vm.AddMapping(created);
            return;
        }

        var dialog = new MappingEditorWindow(row.Rule, CurrentRules()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edited) return;
        _vm.ReplaceMapping(row, edited);
    }

    private void OnDeleteMappingClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MappingRowVm row) _vm.RemoveMapping(row);
    }

    // —— 编辑器取键探针（低层钩子 → UI 线程）——

    public void BeginKeyProbe(Action<int> onKey) => _keyProbe = onKey;

    public void EndKeyProbe() => _keyProbe = null;

    private void DispatchKeyProbe(int vk)
    {
        if (_keyProbe is null) return;
        Dispatcher.BeginInvoke(new Action(() => _keyProbe?.Invoke(vk)));
    }

    // —— UI ——

    private void OnUiTick(object? sender, EventArgs e)
    {
        _vm.Update(_state.Read());

        // CapsLock / NumLock 的锁定状态：GetKeyState 最低位 = 开启（随消息队列刷新）
        _vm.RefreshLockStates(
            capsLockOn: (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0,
            numLockOn: (NativeMethods.GetKeyState(NativeMethods.VK_NUMLOCK) & 1) != 0);
    }

    /// <summary>点击状态胶囊开启捕获（捕获开启后鼠标被吞，无法再点击关闭，属预期）。</summary>
    private void OnCapturePillClick(object sender, RoutedEventArgs e) => _capture?.SetCapturing(true);

    /// <summary>「归一化摇杆」开关：切换输出裁剪形状并落盘到当前配置（仅用户点击触发）。</summary>
    private void OnNormalizeToggle(object sender, RoutedEventArgs e)
    {
        _normalizeSticks = NormalizeCheck.IsChecked == true;
        _vm.OutputPad.SquareSticks = !_normalizeSticks;
        SaveActiveProfile();
    }

    private void SetPadStatus(string text) =>
        Dispatcher.BeginInvoke(new Action(() => _vm.PadStatus = text));

    private void SetHookStatus(string text) =>
        Dispatcher.BeginInvoke(new Action(() => _vm.HookStatus = text));

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 退出前落盘：当前配置 + 导入配置的 cache 应急备份
        SaveActiveProfile();
        BackupImportedConfigs();

        // 先把物理手柄还给系统，避免程序退出后它仍对其它程序不可见
        _hidHide?.Restore(HidHideMarkerPath);

        _uiTimer?.Stop();
        _cts?.Cancel();
        _hookThread?.Join(2000);
        _forwardThread?.Join(3000);
        if (_backend is not null) _backend.Rumble -= OnRumble;
        _backend?.Dispose();
        _backend = null;
        _pad = null;
        _cts?.Dispose();
        _cts = null;
    }
}
