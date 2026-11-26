using System.Collections.ObjectModel;
using System.ComponentModel;
using MultiInputGamepad.Core;

namespace MultiInputGamepad.App.ViewModels;

/// <summary>一个可视化的键盘按键（含绝对坐标与按下高亮状态）。</summary>
public sealed class KeyVm : INotifyPropertyChanged
{
    public string Label { get; }
    public int Vk { get; }

    /// <summary>不可映射的键（系统键/修饰键）：可视化用暗色低饱和样式，且不响应单击。</summary>
    public bool IsForbidden { get; }

    public double PixelX { get; }
    public double PixelY { get; }
    public double PixelWidth { get; }
    public double PixelHeight { get; }

    private bool _isPressed;
    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (_isPressed == value) return;
            _isPressed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPressed)));
        }
    }

    /// <summary>该键已参与某条映射规则（可视化中用高亮描边表示）。</summary>
    private bool _isMapped;
    public bool IsMapped
    {
        get => _isMapped;
        set
        {
            if (_isMapped == value) return;
            _isMapped = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMapped)));
        }
    }

    /// <summary>锁定键（CapsLock / NumLock）当前是否处于开启状态（开启时描边高亮）。</summary>
    private bool _isToggledOn;
    public bool IsToggledOn
    {
        get => _isToggledOn;
        set
        {
            if (_isToggledOn == value) return;
            _isToggledOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsToggledOn)));
        }
    }

    public KeyVm(KeyboardLayout.KeyDef def)
    {
        Label = def.Label;
        Vk = def.Vk;
        IsForbidden = KeyBlacklist.Contains(def.Vk);
        const double u = KeyboardLayout.Unit;
        const double gap = KeyboardLayout.Gap;
        PixelX = def.X * u + gap / 2;
        PixelY = def.Y * u + gap / 2;
        PixelWidth = def.W * u - gap;
        PixelHeight = def.H * u - gap;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>M1 主视图模型：输入/输出可视化 + 捕获状态。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<KeyVm> Keys { get; } = new();

    public double KeyboardPixelWidth => KeyboardLayout.PixelWidth;
    public double KeyboardPixelHeight => KeyboardLayout.PixelHeight;

    public PadVm InputPad { get; } = new("游戏手柄输入（Elite 2）") { Editable = true };
    public PadVm OutputPad { get; } = new("虚拟手柄输出（Xbox Series）");

    private readonly KeyVm? _capsLockKey;
    private readonly KeyVm? _numLockKey;
    private readonly Dictionary<int, List<KeyVm>> _keysByVk = new();   // 虚拟键码 → 可视化键（Enter 等有重码）
    private readonly HashSet<int> _pressed = new();                    // 上一帧按下的键（用于增量更新）

    // 输入端手柄：包号未变就不重复刷新（空闲时省掉整帧属性写入）
    private uint _lastPadPacket;
    private bool _hasPadPacket;

    public MainViewModel()
    {
        foreach (var def in KeyboardLayout.Keys)
        {
            var key = new KeyVm(def);
            Keys.Add(key);

            if (!_keysByVk.TryGetValue(def.Vk, out var list))
                _keysByVk[def.Vk] = list = new List<KeyVm>();
            list.Add(key);

            switch (def.Vk)
            {
                case 0x14: _capsLockKey = key; break;   // CapsLock
                case 0x90: _numLockKey = key; break;    // NumLock
            }
        }
    }

    /// <summary>刷新 CapsLock / NumLock 的锁定状态（开启时键盘面板描边高亮）。</summary>
    public void RefreshLockStates(bool capsLockOn, bool numLockOn)
    {
        if (_capsLockKey is not null) _capsLockKey.IsToggledOn = capsLockOn;
        if (_numLockKey is not null) _numLockKey.IsToggledOn = numLockOn;
    }

    // —— 捕获状态 ——
    private bool _isCapturing;
    public bool IsCapturing { get => _isCapturing; private set => Set(ref _isCapturing, value); }

    private string _captureText = "捕获：关闭";
    public string CaptureText { get => _captureText; private set => Set(ref _captureText, value); }

    // —— 状态文字 ——
    private string _padStatus = "虚拟手柄：启动中…";
    public string PadStatus { get => _padStatus; set => Set(ref _padStatus, value); }

    private string _hookStatus = "键鼠钩子：启动中…";
    public string HookStatus { get => _hookStatus; set => Set(ref _hookStatus, value); }

    // —— 鼠标状态 ——
    public MouseVm Mouse { get; } = new();

    // —— 映射列表 ——
    public ObservableCollection<MappingRowVm> Mappings { get; } = new();

    public bool IsMappingListEmpty => Mappings.Count == 0;

    private string _mappingNotice = "";
    /// <summary>映射配置相关的提示（读取失败等）；空串时该行不占位。</summary>
    public string MappingNotice { get => _mappingNotice; set => Set(ref _mappingNotice, value); }

    private bool _noticeIsError;
    /// <summary>提示是否为错误：true 用红色，否则用金色（成功/普通提示）。</summary>
    public bool NoticeIsError { get => _noticeIsError; set => Set(ref _noticeIsError, value); }

    /// <summary>显示一条提示（默认金色；出错传 error=true 显示红色）。</summary>
    public void ShowNotice(string text, bool error = false)
    {
        MappingNotice = text;
        NoticeIsError = error;
    }

    public void ReloadMappings(IEnumerable<MappingRule> rules)
    {
        Mappings.Clear();
        foreach (var rule in rules)
            Mappings.Add(new MappingRowVm(rule));
        // 仅刷新空态，不触发 MappingListChanged：启动读取不应回写文件
        ReorderMappings();
        RefreshMappedInputs();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMappingListEmpty)));
    }

    public void AddMapping(MappingRule rule)
    {
        Mappings.Add(new MappingRowVm(rule));
        NotifyMappingListChanged();
    }

    public void ReplaceMapping(MappingRowVm oldRow, MappingRule rule)
    {
        int index = Mappings.IndexOf(oldRow);
        if (index < 0) return;
        Mappings[index] = new MappingRowVm(rule);
        NotifyMappingListChanged();
    }

    public void RemoveMapping(MappingRowVm row)
    {
        if (!Mappings.Remove(row)) return;
        NotifyMappingListChanged();
    }

    private void NotifyMappingListChanged()
    {
        ReorderMappings();
        RefreshMappedInputs();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMappingListEmpty)));
        MappingListChanged?.Invoke();
    }

    /// <summary>鼠标移动（轴向）规则置顶，其余保持原有先后顺序。</summary>
    private void ReorderMappings()
    {
        if (Mappings.Count < 2) return;

        var ordered = Mappings.Where(row => row.Rule.Source == MappingSource.MouseAxis)
            .Concat(Mappings.Where(row => row.Rule.Source != MappingSource.MouseAxis))
            .ToList();

        for (int target = 0; target < ordered.Count; target++)
        {
            int current = Mappings.IndexOf(ordered[target]);
            if (current >= 0 && current != target) Mappings.Move(current, target);
        }
    }

    /// <summary>按当前规则表刷新「输入已被映射」的可视化标记。</summary>
    public void RefreshMappedInputs()
    {
        var keys = new HashSet<int>();
        var buttons = new HashSet<int>();
        var padButtons = GamepadButton.None;
        bool wheelUp = false, wheelDown = false;

        foreach (var row in Mappings)
        {
            var rule = row.Rule;
            switch (rule.Source)
            {
                case MappingSource.Keyboard: keys.Add(rule.KeyCode); break;
                case MappingSource.MouseButton: buttons.Add(rule.KeyCode); break;
                case MappingSource.MouseWheel:
                    if (rule.KeyCode == 0) wheelUp = true;
                    else wheelDown = true;
                    break;
                case MappingSource.Gamepad: padButtons |= GamepadInputs.ToButton(rule.PadInput); break;
            }
        }

        foreach (var key in Keys)
            key.IsMapped = keys.Contains(key.Vk);

        InputPad.Mapped = padButtons;
        Mouse.SetMapped(buttons, wheelUp, wheelDown);
    }

    /// <summary>列表增删改后触发（用于同步引擎与持久化）。</summary>
    public event Action? MappingListChanged;

    /// <summary>由 UI 定时器调用（已在 UI 线程）。</summary>
    public void Update(InputState.Snapshot s)
    {
        IsCapturing = s.Capturing;
        CaptureText = s.Capturing ? "捕获：开启" : "捕获：关闭";

        if (s.Pad is { } p)
        {
            if (!_hasPadPacket || p.PacketNumber != _lastPadPacket)
            {
                _lastPadPacket = p.PacketNumber;
                _hasPadPacket = true;
                InputPad.Show(p);
            }
        }
        else
        {
            _hasPadPacket = false;
            InputPad.Clear("未连接");
        }

        if (s.OutputPad is { } o) OutputPad.Show(o);
        else OutputPad.Clear("无输出");

        // UI 刷新本身就是 30 Hz，鼠标直接跟随；位移钳位避免最小化恢复时数值突跳
        Mouse.Update(s.ML, s.MR, s.MM, s.MX1, s.MX2,
            Math.Clamp(s.Dx, -200, 200), Math.Clamp(s.Dy, -200, 200), s.Wheel);

        UpdateKeys(s.Keys);
    }

    /// <summary>按键高亮：只更新状态变化的键（不再每帧遍历 108 个键）。</summary>
    private void UpdateKeys(HashSet<int> now)
    {
        foreach (int vk in now)
        {
            if (_pressed.Contains(vk)) continue;
            if (_keysByVk.TryGetValue(vk, out var list))
                foreach (var key in list) key.IsPressed = true;
        }

        foreach (int vk in _pressed)
        {
            if (now.Contains(vk)) continue;
            if (_keysByVk.TryGetValue(vk, out var list))
                foreach (var key in list) key.IsPressed = false;
        }

        _pressed.Clear();
        foreach (int vk in now) _pressed.Add(vk);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
