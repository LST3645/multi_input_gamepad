using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MultiInputGamepad.App.ViewModels;
using MultiInputGamepad.Core;

namespace MultiInputGamepad.App;

/// <summary>
/// 映射编辑器：来源 = 键盘 / 鼠标（按键+滚轮）/ 鼠标移动 / 手柄（按键 / 扳机 4 档 / 摇杆四方向）；
/// 输出用模拟手柄布局点选（LT/RT 为 10 级可拖拽进度条），可组合最多 3 个目标；
/// 「立即/延迟 + ms」作用于当前槽，表示相对上一项再等 N ms（逐项累加）。
/// 「输入键」通过低层钩子的按键探针拾取（由 MainWindow 转发）；手柄页则直接在输入手柄布局上点选。
/// </summary>
public partial class MappingEditorWindow : Window
{
    private const int VkEscape = 0x1B;
    private const double TriggerTrackHeight = 100;
    private const double InputTriggerTrackHeight = 72;

    /// <summary>「立即/延迟」里延迟框的默认值（添加新槽后回到默认状态）。</summary>
    private const int DefaultDelayMs = 100;

    private static readonly Brush PickBrush = Frozen(0xE8, 0xE8, 0xE8);
    private static readonly Brush MutedBrush = Frozen(0x8A, 0x8A, 0x92);
    private static readonly Brush FillBrush = Frozen(0x2E, 0x5F, 0xBF);
    private static readonly Brush NeutralFillBrush = Frozen(0x2A, 0x2A, 0x2E);
    private static readonly Brush FillUsedBrush = Frozen(0x2E, 0x6A, 0x46);
    private static readonly Brush TrackBrush = Frozen(0x16, 0x16, 0x18);
    private static readonly Brush TrackBorderBrush = Frozen(0x3A, 0x3A, 0x3E);
    private static readonly Brush SelectedBrush = Frozen(0x4C, 0x9A, 0xFF);
    private static readonly Brush TriggerBrush = Frozen(0xE0, 0xA0, 0x30);
    private static readonly Brush ChipBrush = Frozen(0x33, 0x33, 0x3A);
    private static readonly Brush ChipActiveBrush = Frozen(0x2E, 0x5F, 0xBF);
    private static readonly Brush ChipEmptyBrush = Frozen(0x2A, 0x2A, 0x2E);
    private static readonly Brush PlusBrush = Frozen(0x6A, 0x6A, 0x72);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>一个输出槽：目标 + （扳机的）百分比 + 该槽自己的延迟（相对上一槽，0 = 同时按下）。</summary>
    private sealed class Slot
    {
        public GamepadTarget? Target;
        public int Percent = 100;
        public int DelayMs;
    }

    private readonly MappingRule? _existing;
    private readonly IReadOnlyList<MappingRule> _allRules;
    private readonly List<Slot> _slots = new();
    private readonly Dictionary<GamepadTarget, RadioButton> _picks = new();
    private readonly Dictionary<RadioButton, GamepadInput> _padPicks = new();
    private readonly Dictionary<RadioButton, (MappingSource Source, int Code)> _mousePicks = new();
    private readonly Dictionary<RadioButton, int> _axisX = new();
    private readonly Dictionary<RadioButton, int> _axisY = new();

    private Border _ltFill = null!;
    private Border _rtFill = null!;
    private Border _inLtFill = null!;
    private Border _inRtFill = null!;

    private int _ltLevel = MappingRule.TriggerSteps;
    private int _rtLevel = MappingRule.TriggerSteps;
    private int _inLtLevel = GamepadInputs.TriggerLevels;
    private int _inRtLevel = GamepadInputs.TriggerLevels;
    private GamepadInput? _padInput;
    private int _activeIndex;
    private int _vk = -1;
    private bool _probing;
    private bool _syncing;

    private MappingSource _mouseSource = MappingSource.MouseButton;
    private int _mouseCode;
    private bool _mousePicked;   // 鼠标页默认不选中任何按键，必须由用户点选后才算有效

    /// <summary>确定后的规则（取消时为 null）。</summary>
    public MappingRule? Result { get; private set; }

    public MappingEditorWindow(MappingRule? existing, IReadOnlyList<MappingRule>? allRules = null)
    {
        InitializeComponent();
        _existing = existing;
        _allRules = allRules ?? Array.Empty<MappingRule>();

        BuildTargetMap();
        BuildPadMap();
        BuildMouseMap();
        BuildAxisMaps();
        BuildTriggerBar(GamepadTarget.LeftTrigger, LtBarHost, out _ltFill);
        BuildTriggerBar(GamepadTarget.RightTrigger, RtBarHost, out _rtFill);
        _inLtFill = BuildLevelBar(InLtBarHost, GamepadInputs.TriggerLevels, level => SelectPadTrigger(true, level));
        _inRtFill = BuildLevelBar(InRtBarHost, GamepadInputs.TriggerLevels, level => SelectPadTrigger(false, level));

        // 事件在 InitializeComponent 之后统一挂接，避免解析期访问未创建控件
        SourceKeyboard.Checked += (_, _) => UpdateSourcePanels();
        SourceMouse.Checked += (_, _) => UpdateSourcePanels();
        SourceMouseAxis.Checked += (_, _) => UpdateSourcePanels();
        SourceGamepad.Checked += (_, _) => UpdateSourcePanels();
        KeyBox.MouseLeftButtonDown += OnKeyBoxClick;

        foreach (var (target, pick) in _picks)
            pick.Checked += (_, _) => OnTargetPicked(target);

        foreach (var (pick, input) in _padPicks)
            pick.Checked += (_, _) => OnPadPicked(input);

        foreach (var pick in _mousePicks.Keys)
            pick.Checked += (_, _) => OnMouseSourcePicked(pick);

        foreach (var pick in _axisX.Keys)
            pick.Checked += (_, _) => OnAxisChanged(pick, _axisX);
        foreach (var pick in _axisY.Keys)
            pick.Checked += (_, _) => OnAxisChanged(pick, _axisY);

        foreach (var box in new[] { MoveAmountXBox, DisplacementXBox, MoveAmountYBox, DisplacementYBox })
            box.TextChanged += (_, _) => UpdatePreview();

        AddSlotButton.Click += OnAddSlot;
        RemoveLastButton.Click += OnRemoveLast;
        ModeInstant.Checked += (_, _) => OnModePicked(ModeInstant);
        ModeDelay.Checked += (_, _) => OnModePicked(ModeDelay);
        RepeatCheck.Checked += (_, _) => UpdateTriggerUi();
        RepeatCheck.Unchecked += (_, _) => UpdateTriggerUi();
        DelayBox.TextChanged += OnDelayChanged;
        OkButton.Click += OnOk;
        Closed += (_, _) => EndProbe();

        _slots.Add(new Slot());
        _activeIndex = 0;
        if (existing is not null) Prefill(existing);

        UpdateSourcePanels();
        UpdateKeyBox();
        LoadDelayUi();
        UpdatePreview();
        UpdateInputTriggers();
        RefreshOutputs();
    }

    // —— 构建映射表 ——

    private void BuildTargetMap()
    {
        _picks[GamepadTarget.LeftBumper] = PickLB;
        _picks[GamepadTarget.RightBumper] = PickRB;
        _picks[GamepadTarget.DPadUp] = PickDUp;
        _picks[GamepadTarget.DPadDown] = PickDDown;
        _picks[GamepadTarget.DPadLeft] = PickDLeft;
        _picks[GamepadTarget.DPadRight] = PickDRight;
        _picks[GamepadTarget.Y] = PickY;
        _picks[GamepadTarget.X] = PickX;
        _picks[GamepadTarget.B] = PickB;
        _picks[GamepadTarget.A] = PickA;
        _picks[GamepadTarget.Back] = PickView;
        _picks[GamepadTarget.Start] = PickMenu;
        _picks[GamepadTarget.LeftStick] = PickL3;
        _picks[GamepadTarget.RightStick] = PickR3;

        // 摇杆方向（每根 X/Y 轴 ± 满偏）
        _picks[GamepadTarget.LeftXMinus] = PickLXMinus;
        _picks[GamepadTarget.LeftXPlus] = PickLXPlus;
        _picks[GamepadTarget.LeftYMinus] = PickLYMinus;
        _picks[GamepadTarget.LeftYPlus] = PickLYPlus;
        _picks[GamepadTarget.RightXMinus] = PickRXMinus;
        _picks[GamepadTarget.RightXPlus] = PickRXPlus;
        _picks[GamepadTarget.RightYMinus] = PickRYMinus;
        _picks[GamepadTarget.RightYPlus] = PickRYPlus;
    }

    private void BuildPadMap()
    {
        _padPicks[InPickA] = GamepadInput.A;
        _padPicks[InPickB] = GamepadInput.B;
        _padPicks[InPickX] = GamepadInput.X;
        _padPicks[InPickY] = GamepadInput.Y;
        _padPicks[InPickLB] = GamepadInput.LeftBumper;
        _padPicks[InPickRB] = GamepadInput.RightBumper;
        _padPicks[InPickL3] = GamepadInput.LeftStick;
        _padPicks[InPickR3] = GamepadInput.RightStick;
        _padPicks[InPickView] = GamepadInput.Back;
        _padPicks[InPickMenu] = GamepadInput.Start;
        _padPicks[InPickDUp] = GamepadInput.DPadUp;
        _padPicks[InPickDDown] = GamepadInput.DPadDown;
        _padPicks[InPickDLeft] = GamepadInput.DPadLeft;
        _padPicks[InPickDRight] = GamepadInput.DPadRight;

        // 摇杆四方向（每根摇杆 4 个独立输入，判定含两侧共同区）
        _padPicks[InPickLYPlus] = GamepadInput.LeftStickUp;
        _padPicks[InPickLYMinus] = GamepadInput.LeftStickDown;
        _padPicks[InPickLXMinus] = GamepadInput.LeftStickLeft;
        _padPicks[InPickLXPlus] = GamepadInput.LeftStickRight;
        _padPicks[InPickRYPlus] = GamepadInput.RightStickUp;
        _padPicks[InPickRYMinus] = GamepadInput.RightStickDown;
        _padPicks[InPickRXMinus] = GamepadInput.RightStickLeft;
        _padPicks[InPickRXPlus] = GamepadInput.RightStickRight;
    }

    private void BuildMouseMap()
    {
        _mousePicks[MouseX2] = (MappingSource.MouseButton, 5);
        _mousePicks[MouseX1] = (MappingSource.MouseButton, 4);
        _mousePicks[MouseLeft] = (MappingSource.MouseButton, 1);
        _mousePicks[MouseMiddle] = (MappingSource.MouseButton, 3);
        _mousePicks[MouseRight] = (MappingSource.MouseButton, 2);
        _mousePicks[WheelUp] = (MappingSource.MouseWheel, 0);
        _mousePicks[WheelDown] = (MappingSource.MouseWheel, 1);
    }

    private void BuildAxisMaps()
    {
        Add(true, AxisXNone, -1); Add(true, AxisXLeftX, (int)StickAxis.LeftX);
        Add(true, AxisXLeftY, (int)StickAxis.LeftY); Add(true, AxisXRightX, (int)StickAxis.RightX);
        Add(true, AxisXRightY, (int)StickAxis.RightY);
        Add(false, AxisYNone, -1); Add(false, AxisYLeftX, (int)StickAxis.LeftX);
        Add(false, AxisYLeftY, (int)StickAxis.LeftY); Add(false, AxisYRightX, (int)StickAxis.RightX);
        Add(false, AxisYRightY, (int)StickAxis.RightY);

        void Add(bool horizontal, RadioButton pick, int axis)
        {
            if (horizontal) _axisX[pick] = axis;
            else _axisY[pick] = axis;
        }
    }

    /// <summary>LT/RT 可拖拽进度条：点按或上下拖，自动吸附到最近的 1/10 档。</summary>
    private void BuildTriggerBar(GamepadTarget target, Grid host, out Border fill)
    {
        host.Height = TriggerTrackHeight;
        fill = BuildLevelBar(host, MappingRule.TriggerSteps, level => SetTriggerLevel(target, level),
            () => IsUsedByOtherSlot(target));
    }

    /// <summary>
    /// 可拖拽档位条：点按或上下拖，吸附到最近的 1/<paramref name="steps"/> 档。
    /// <paramref name="locked"/> 返回 true 时忽略点击（该档位已被其他输出槽占用）。
    /// </summary>
    private Border BuildLevelBar(Grid host, int steps, Action<int> onPick, Func<bool>? locked = null)
    {
        host.ClipToBounds = true;
        host.Cursor = Cursors.Hand;

        var track = new Border
        {
            Background = TrackBrush,
            BorderBrush = TrackBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };

        var fill = new Border
        {
            Background = FillBrush,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Top, // 上方为填充部分（按下程度从上往下增长）
            Margin = new Thickness(2, 2, 2, 2),
            Height = 0,
            IsHitTestVisible = false,
        };

        host.Children.Add(track);
        host.Children.Add(fill);

        int LevelFromY(double y)
        {
            double height = host.ActualHeight > 0 ? host.ActualHeight : host.Height;

            // 填充自上而下：越往下拖 = 按下程度越大（与进度条填充方向一致）
            double fraction = Math.Clamp(y / height, 0, 1);
            return Math.Clamp((int)Math.Round(fraction * steps, MidpointRounding.AwayFromZero), 1, steps);
        }

        host.MouseLeftButtonDown += (_, e) =>
        {
            if (locked?.Invoke() == true) return;
            host.CaptureMouse();
            onPick(LevelFromY(e.GetPosition(host).Y));
            e.Handled = true;
        };
        host.MouseMove += (_, e) =>
        {
            if (host.IsMouseCaptured)
                onPick(LevelFromY(e.GetPosition(host).Y));
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            if (host.IsMouseCaptured) host.ReleaseMouseCapture();
            e.Handled = true;
        };

        return fill;
    }

    /// <summary>档位填充高度（上下各留 2px 边距）。</summary>
    private static double FillHeight(double trackHeight, int level, int steps) => (trackHeight - 4) * level / steps;

    // —— 来源面板 ——

    private void UpdateSourcePanels()
    {
        if (KeyboardPanel is null) return;

        bool keyboard = SourceKeyboard.IsChecked == true;
        bool mouse = SourceMouse.IsChecked == true;
        bool axis = SourceMouseAxis.IsChecked == true;
        bool pad = SourceGamepad.IsChecked == true;

        KeyboardPanel.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        MousePanel.Visibility = mouse ? Visibility.Visible : Visibility.Collapsed;
        AxisPanel.Visibility = axis ? Visibility.Visible : Visibility.Collapsed;
        PadPanel.Visibility = pad ? Visibility.Visible : Visibility.Collapsed;
        OutputPanel.Visibility = axis ? Visibility.Collapsed : Visibility.Visible;

        ErrorText.Visibility = Visibility.Collapsed;
    }

    // —— 键盘输入键 ——

    private void OnKeyBoxClick(object sender, MouseButtonEventArgs e)
    {
        _probing = true;
        UpdateKeyBox();
        (Owner as MainWindow)?.BeginKeyProbe(OnKeyProbed);
    }

    private void EndProbe()
    {
        if (!_probing) return;
        _probing = false;
        (Owner as MainWindow)?.EndKeyProbe();
        UpdateKeyBox();
    }

    /// <summary>低层钩子拾取到的按键；<see cref="VkEscape"/> 表示取消拾取。</summary>
    public void OnKeyProbed(int vk)
    {
        if (!_probing) return;

        if (vk == VkEscape)
        {
            EndProbe();
            return;
        }

        // 系统/修饰键不可映射：提示并保持拾取，等用户改按其他键
        if (KeyBlacklist.Contains(vk))
        {
            Fail($"{KeyNames.Of(vk)} 为系统键/修饰键，不能参与映射。");
            return;
        }

        _vk = vk;
        ErrorText.Visibility = Visibility.Collapsed;
        EndProbe();
    }

    private void UpdateKeyBox()
    {
        if (KeyBoxText is null) return;

        if (_probing)
        {
            KeyBoxText.Text = "请按下要映射的键…（Esc 取消）";
            KeyBoxText.Foreground = PickBrush;
        }
        else if (_vk >= 0)
        {
            KeyBoxText.Text = KeyNames.Of(_vk);
            KeyBoxText.Foreground = PickBrush;
        }
        else
        {
            KeyBoxText.Text = "点击此处，然后按键";
            KeyBoxText.Foreground = MutedBrush;
        }
    }

    // —— 输出槽 ——

    /// <summary>当前编辑的槽（点击下方映射串可切换）。</summary>
    private Slot Active => _slots[_activeIndex];

    private void OnTargetPicked(GamepadTarget target)
    {
        if (_syncing) return;
        if (IsUsedByOtherSlot(target)) { RefreshOutputs(); return; }

        SelectActiveTarget(target, MappingLabels.IsTrigger(target) ? TriggerLevel(target) * 10 : 100);
    }

    /// <summary>选中当前槽的目标（延迟由「延迟」输入框单独编辑）。</summary>
    private void SelectActiveTarget(GamepadTarget target, int percent)
    {
        Active.Target = target;
        Active.Percent = percent;
        RefreshOutputs();
    }

    private void SetTriggerLevel(GamepadTarget target, int level)
    {
        if (IsUsedByOtherSlot(target)) return;

        if (target == GamepadTarget.LeftTrigger) _ltLevel = level;
        else _rtLevel = level;

        // 无论是否已选中，都重新应用并刷新（此前已选中时漏了刷新 → “按下后改不动”）
        SelectActiveTarget(target, level * 10);
    }

    // —— 手柄输入（按键 / 扳机 4 档 / 摇杆四方向）——

    /// <summary>选中一个手柄输入（null = 清空）。按键、扳机档位、摇杆方向互相独立、互斥选中。</summary>
    private void SelectPadInput(GamepadInput? input)
    {
        _padInput = input;

        bool previous = _syncing;
        _syncing = true;
        try
        {
            foreach (var (pick, value) in _padPicks)
                pick.IsChecked = input == value;
        }
        finally
        {
            _syncing = previous;
        }

        if (input is { } picked && GamepadInputs.TryGetTriggerLevel(picked, out bool left, out int level))
        {
            if (left) _inLtLevel = level;
            else _inRtLevel = level;
        }

        UpdateInputTriggers();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnPadPicked(GamepadInput input)
    {
        if (_syncing) return;
        SelectPadInput(input);
    }

    /// <summary>点选输入扳机的某一档：4 档各自是独立输入源。</summary>
    private void SelectPadTrigger(bool left, int level)
    {
        if (level < 1 || level > GamepadInputs.TriggerLevels) return;
        SelectPadInput(GamepadInputs.TriggerLevel(left, level));
    }

    /// <summary>输入扳机条：选中时高亮并显示档位，未选中时灰显上次选的档位。</summary>
    private void UpdateInputTriggers()
    {
        UpdateInputTrigger(InLtWell, _inLtFill, InLtText, true);
        UpdateInputTrigger(InRtWell, _inRtFill, InRtText, false);
    }

    private void UpdateInputTrigger(StackPanel well, Border fill, TextBlock text, bool left)
    {
        bool selected = _padInput is { } input &&
                        GamepadInputs.TryGetTriggerLevel(input, out bool isLeft, out _) && isLeft == left;

        int level = left ? _inLtLevel : _inRtLevel;
        fill.Height = FillHeight(InputTriggerTrackHeight, level, GamepadInputs.TriggerLevels);
        fill.Background = selected ? FillBrush : NeutralFillBrush;

        text.Text = $"{level * 100 / GamepadInputs.TriggerLevels}%";
        text.Foreground = selected ? SelectedBrush : MutedBrush;
        text.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        well.Opacity = selected ? 1.0 : 0.75;
    }


    private void OnChipClicked(int index)
    {
        if (index < 0 || index >= _slots.Count) return;
        _activeIndex = index;
        LoadDelayUi();
        RefreshOutputs();
    }

    private void OnAddSlot(object sender, RoutedEventArgs e)
    {
        if (Active.Target is null)
        {
            Fail("请先为当前输出选择一个手柄按键。");
            return;
        }

        if (_slots.Count >= MappingRule.MaxOutputs)
        {
            Fail($"最多 {MappingRule.MaxOutputs} 个输出。");
            return;
        }

        _slots.Add(new Slot());
        _activeIndex = _slots.Count - 1;
        ErrorText.Visibility = Visibility.Collapsed;
        LoadDelayUi();      // 新槽：回到「立即」+ 默认延迟值
        RefreshOutputs();
    }

    private void OnRemoveLast(object sender, RoutedEventArgs e)
    {
        if (_slots.Count > 1)
        {
            _slots.RemoveAt(_activeIndex);
            _activeIndex = Math.Min(_activeIndex, _slots.Count - 1);
        }
        else
        {
            Active.Target = null;
            Active.DelayMs = 0;
        }

        LoadDelayUi();      // 删除后：回到当前槽原来的「立即/延迟 + 延迟时间」
        RefreshOutputs();
    }

    private bool IsUsedByOtherSlot(GamepadTarget target)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i != _activeIndex && _slots[i].Target == target) return true;
        }
        return false;
    }

    private int TriggerLevel(GamepadTarget target) =>
        target == GamepadTarget.LeftTrigger ? _ltLevel : _rtLevel;

    /// <summary>刷新按钮选中态、已占用态、扳机进度条与底部映射串。</summary>
    private void RefreshOutputs()
    {
        bool previous = _syncing;
        _syncing = true;
        try
        {
            GamepadTarget? active = Active.Target;

            foreach (var (target, pick) in _picks)
            {
                bool used = IsUsedByOtherSlot(target);
                pick.IsChecked = active == target;
                pick.Tag = used ? "used" : null;
                pick.IsHitTestVisible = !used;
            }

            UpdateTriggerWell(LtWell, _ltFill, LtPercent, GamepadTarget.LeftTrigger);
            UpdateTriggerWell(RtWell, _rtFill, RtPercent, GamepadTarget.RightTrigger);
        }
        finally
        {
            _syncing = previous;
        }

        RefreshChips();
    }

    private void UpdateTriggerWell(StackPanel well, Border fill, TextBlock percentText, GamepadTarget target)
    {
        bool selected = Active.Target == target;
        bool used = IsUsedByOtherSlot(target);

        // 当前槽就是该扳机时显示实际档位，否则显示上次使用的档位（作为下次选择的默认值）
        int level = selected
            ? Math.Clamp(Active.Percent / 10, 1, MappingRule.TriggerSteps)
            : TriggerLevel(target);

        fill.Height = FillHeight(TriggerTrackHeight, level, MappingRule.TriggerSteps);
        fill.Background = used ? FillUsedBrush : selected ? FillBrush : NeutralFillBrush;

        percentText.Text = $"{level * 10}%";
        percentText.Foreground = selected ? SelectedBrush : MutedBrush;
        percentText.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        well.IsHitTestVisible = !used;
        well.Opacity = used ? 0.55 : 1.0;
    }

    private void RefreshChips()
    {
        ChipsPanel.Children.Clear();

        for (int i = 0; i < _slots.Count; i++)
        {
            Slot slot = _slots[i];
            bool isActive = i == _activeIndex;
            int index = i;

            var chip = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 4),
                Background = slot.Target is null ? ChipEmptyBrush : isActive ? ChipActiveBrush : ChipBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = slot.DelayMs > 0
                    ? $"点击选中该条；按住输入键后延迟 {slot.DelayMs}ms 才按下（相对上一项累加）"
                    : "点击选中该条；与上一项同时按下",
            };
            chip.MouseLeftButtonDown += (_, e) => { OnChipClicked(index); e.Handled = true; };
            chip.Child = new TextBlock
            {
                Text = slot.Target is null ? "待选择" : ChipText(slot),
                FontSize = 12,
                Foreground = slot.Target is null ? MutedBrush : PickBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ChipsPanel.Children.Add(chip);

            if (i < _slots.Count - 1)
            {
                // ✚ = 等上一项之后才按；+ = 与上一项同时按下
                bool simultaneous = _slots[i + 1].DelayMs == 0;
                ChipsPanel.Children.Add(new TextBlock
                {
                    Text = simultaneous ? "+" : "✚",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = simultaneous ? MutedBrush : PlusBrush,
                    Margin = new Thickness(8, 0, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = simultaneous ? "下一项与它同时按下" : "下一项在它之后延迟按下",
                });
            }
        }

        AddSlotButton.IsEnabled = Active.Target is not null && _slots.Count < MappingRule.MaxOutputs;
        RemoveLastButton.IsEnabled = _slots.Count > 1 || Active.Target is not null;
    }

    /// <summary>单个槽的文字：<c>延迟100ms A</c> / <c>LT 60%</c>；延迟是该槽相对上一项的等待。</summary>
    private static string ChipText(Slot slot)
    {
        GamepadTarget target = slot.Target!.Value;
        string prefix = slot.DelayMs > 0 ? $"延迟{slot.DelayMs}ms " : "";
        return MappingLabels.IsTrigger(target)
            ? $"{prefix}{MappingLabels.Target(target)} {slot.Percent}%"
            : $"{prefix}{MappingLabels.Target(target)}";
    }

    // —— 鼠标来源 ——

    private void OnMouseSourcePicked(RadioButton picked)
    {
        if (_syncing) return;

        var info = _mousePicks[picked];
        _mouseSource = info.Source;
        _mouseCode = info.Code;
        _mousePicked = true;
        SetExclusive(_mousePicks.Keys, picked);
    }

    /// <summary>显式互斥：WPF 的 RadioButton 按父容器自动分组，跨容器时会失效（LB/RB 曾因此联动）。</summary>
    private void SetExclusive(IEnumerable<RadioButton> group, RadioButton selected)
    {
        bool previous = _syncing;
        _syncing = true;
        try
        {
            foreach (var pick in group)
                pick.IsChecked = ReferenceEquals(pick, selected);
        }
        finally
        {
            _syncing = previous;
        }
    }

    // —— 摇杆轴 ——

    private void OnAxisChanged(RadioButton? picked, Dictionary<RadioButton, int> group)
    {
        if (_syncing) return;
        if (picked is not null) SetExclusive(group.Keys, picked);
        UpdateAxisAvailability();
        UpdatePreview();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static int SelectedAxis(Dictionary<RadioButton, int> group)
    {
        foreach (var (pick, axis) in group)
        {
            if (pick.IsChecked == true) return axis;
        }
        return -1;
    }

    private void SelectAxis(Dictionary<RadioButton, int> group, int axis)
    {
        bool previous = _syncing;
        _syncing = true;
        try
        {
            foreach (var (pick, value) in group)
                pick.IsChecked = value == axis;
        }
        finally
        {
            _syncing = previous;
        }
        UpdateAxisAvailability();
    }

    /// <summary>水平/垂直不能指向同一根轴：另一行已选的轴置灰不可选。</summary>
    private void UpdateAxisAvailability()
    {
        int x = SelectedAxis(_axisX);
        int y = SelectedAxis(_axisY);

        foreach (var (pick, axis) in _axisY)
            pick.IsEnabled = !(axis >= 0 && axis == x);
        foreach (var (pick, axis) in _axisX)
            pick.IsEnabled = !(axis >= 0 && axis == y);
    }

    private void UpdatePreview()
    {
        if (PreviewText is null) return;

        var parts = new List<string>();
        if (SelectedAxis(_axisX) >= 0) parts.Add($"水平：{Readout(MoveAmountXBox, DisplacementXBox)}");
        if (SelectedAxis(_axisY) >= 0) parts.Add($"垂直：{Readout(MoveAmountYBox, DisplacementYBox)}");

        PreviewText.Text = parts.Count == 0
            ? "至少选择一个摇杆轴。"
            : string.Join("　", parts) + "（速度映射 + 50ms 输入平滑；鼠标停下即回中）";
    }

    /// <summary>速度模型下的换算读数：满偏所需的鼠标速度（单位/秒）。</summary>
    private static string Readout(TextBox move, TextBox displacement)
    {
        float gain = NumberBox.Float(move, 1f, 0.01f, 20f) * NumberBox.Float(displacement, 0.02f, 0.0005f, 0.5f);
        if (gain <= 0f) return "-";
        return $"满偏约需 {100f / gain:0} 单位/秒";
    }

    // —— 触发方式（逐槽：立即 / 延迟 + ms，延迟相对上一项累加）——

    private void OnModePicked(RadioButton picked)
    {
        if (_syncing) return;

        bool previous = _syncing;
        _syncing = true;
        try
        {
            SetExclusive(new[] { ModeInstant, ModeDelay }, picked);

            if (picked == ModeDelay)
            {
                Active.DelayMs = NumberBox.Int(DelayBox, DefaultDelayMs, 1, 5000);
                DelayBox.Text = Active.DelayMs.ToString(CultureInfo.InvariantCulture); // 输入非法时归位到生效值
            }
            else
            {
                Active.DelayMs = 0;
            }
        }
        finally
        {
            _syncing = previous;
        }

        UpdateTriggerUi();
        RefreshChips();
    }

    private void OnDelayChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (ModeDelay.IsChecked == true) Active.DelayMs = NumberBox.Int(DelayBox, DefaultDelayMs, 1, 5000);
        RefreshChips(); // 改延迟即时同步到当前槽的显示
    }

    /// <summary>把「当前槽」的延迟回填到控件：切槽、增删槽后调用（添加 → 立即+默认，删除 → 被删槽原状态）。</summary>
    private void LoadDelayUi()
    {
        if (DelayBox is null) return;

        int delay = Active.DelayMs;
        bool previous = _syncing;
        _syncing = true;
        try
        {
            ModeDelay.IsChecked = delay > 0;
            ModeInstant.IsChecked = delay <= 0;
            DelayBox.Text = (delay > 0 ? delay : DefaultDelayMs).ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = previous;
        }

        UpdateTriggerUi();
    }

    private void UpdateTriggerUi()
    {
        if (DelayBox is null) return;
        DelayBox.IsEnabled = ModeDelay.IsChecked == true;
        RepeatMsBox.IsEnabled = RepeatCheck.IsChecked == true;
    }

    // —— 预填 ——

    private void Prefill(MappingRule rule)
    {

        bool previous = _syncing;
        _syncing = true;
        try
        {
            switch (rule.Source)
            {
                case MappingSource.Keyboard:
                    SourceKeyboard.IsChecked = true;
                    _vk = rule.KeyCode;
                    break;

                case MappingSource.MouseButton:
                case MappingSource.MouseWheel:
                    SourceMouse.IsChecked = true;
                    foreach (var (pick, info) in _mousePicks)
                    {
                        if (info.Source != rule.Source || info.Code != rule.KeyCode) continue;
                        pick.IsChecked = true;
                        _mouseSource = rule.Source;
                        _mouseCode = rule.KeyCode;
                        _mousePicked = true;
                        break;
                    }
                    break;

                case MappingSource.Gamepad:
                    SourceGamepad.IsChecked = true;
                    SelectPadInput(rule.PadInput);
                    break;

                default:
                    SourceMouseAxis.IsChecked = true;
                    SelectAxis(_axisX, rule.AxisX);
                    SelectAxis(_axisY, rule.AxisY);
                    MoveAmountXBox.Text = rule.MoveAmountX.ToString("0.00", CultureInfo.InvariantCulture);
                    DisplacementXBox.Text = rule.DisplacementX.ToString("0.000", CultureInfo.InvariantCulture);
                    MoveAmountYBox.Text = rule.MoveAmountY.ToString("0.00", CultureInfo.InvariantCulture);
                    DisplacementYBox.Text = rule.DisplacementY.ToString("0.000", CultureInfo.InvariantCulture);
                    break;
            }

            if (rule.Outputs.Count > 0)
            {
                _slots.Clear();
                int lead = rule.DelayMs; // 旧格式是规则级延迟：归并到第一项，等待时间保持不变
                foreach (var output in rule.Outputs)
                {
                    _slots.Add(new Slot
                    {
                        Target = output.Target,
                        Percent = output.Percent,
                        DelayMs = output.DelayMs + lead,
                    });
                    lead = 0;

                    if (output.Target == GamepadTarget.LeftTrigger)
                        _ltLevel = Math.Clamp(output.Percent / 10, 1, MappingRule.TriggerSteps);
                    if (output.Target == GamepadTarget.RightTrigger)
                        _rtLevel = Math.Clamp(output.Percent / 10, 1, MappingRule.TriggerSteps);
                }

                _activeIndex = _slots.Count - 1;
            }

            RepeatCheck.IsChecked = rule.RepeatEnabled;
            RepeatMsBox.Text = rule.RepeatIntervalMs.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = previous;
        }

        UpdateAxisAvailability();
        UpdateTriggerUi();
    }

    // —— 确定 ——

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var rule = BuildRule(out string error);
        if (rule is null)
        {
            Fail(error);
            return;
        }

        Result = rule;
        DialogResult = true;
    }

    /// <summary>按当前界面生成规则；校验失败返回 null 并给出提示文案。</summary>
    private MappingRule? BuildRule(out string error)
    {
        error = "";
        var rule = new MappingRule { Id = _existing?.Id ?? Guid.NewGuid() };

        if (SourceMouseAxis.IsChecked == true)
        {
            int axisX = SelectedAxis(_axisX);
            int axisY = SelectedAxis(_axisY);

            if (axisX < 0 && axisY < 0)
            {
                error = "水平/垂直至少要选择一个摇杆轴。";
                return null;
            }
            if (axisX >= 0 && axisX == axisY)
            {
                error = "水平与垂直不能指向同一个摇杆轴。";
                return null;
            }

            rule.Source = MappingSource.MouseAxis;
            rule.AxisX = axisX;
            rule.AxisY = axisY;
            rule.MoveAmountX = NumberBox.Float(MoveAmountXBox, 1f, 0.01f, 20f);
            rule.DisplacementX = NumberBox.Float(DisplacementXBox, 0.02f, 0.0005f, 0.5f);
            rule.MoveAmountY = NumberBox.Float(MoveAmountYBox, 1f, 0.01f, 20f);
            rule.DisplacementY = NumberBox.Float(DisplacementYBox, 0.02f, 0.0005f, 0.5f);

            if (FindConflict(rule) is { } axisConflict)
            {
                error = axisConflict;
                return null;
            }
            return rule;
        }

        if (SourceKeyboard.IsChecked == true)
        {
            if (_vk < 0)
            {
                error = "请先选择输入键。";
                return null;
            }
            if (KeyBlacklist.Contains(_vk))
            {
                error = $"{KeyNames.Of(_vk)} 为系统键/修饰键，不能参与映射。";
                return null;
            }
            rule.Source = MappingSource.Keyboard;
            rule.KeyCode = _vk;
            rule.AxisX = -1;
            rule.AxisY = -1;
        }
        else if (SourceGamepad.IsChecked == true)
        {
            if (_padInput is not { } padInput)
            {
                error = "请先选择输入手柄按键、扳机档位或摇杆方向。";
                return null;
            }

            rule.Source = MappingSource.Gamepad;
            rule.PadInput = padInput;
            rule.KeyCode = 0;
            rule.AxisX = -1;
            rule.AxisY = -1;
        }
        else
        {
            if (!_mousePicked)
            {
                error = "请先选择鼠标按键或滚轮。";
                return null;
            }
            rule.Source = _mouseSource;
            rule.KeyCode = _mouseCode;
            rule.AxisX = -1;
            rule.AxisY = -1;
        }

        if (Active.Target is null)
        {
            error = "请至少为最后一个输出选择一个手柄按键。";
            return null;
        }

        if (FindConflict(rule) is { } conflict)
        {
            error = conflict;
            return null;
        }

        rule.DelayMs = 0; // 延迟逐槽写在 Output 上（0 = 与上一项同时按下）
        rule.Outputs = _slots
            .Where(slot => slot.Target is not null)
            .Select(slot => new MappingOutput
            {
                Target = slot.Target!.Value,
                Percent = slot.Percent,
                DelayMs = slot.DelayMs,
            })
            .ToList();

        rule.RepeatEnabled = RepeatCheck.IsChecked == true;
        rule.RepeatIntervalMs = NumberBox.Int(RepeatMsBox, 100, 20, 2000);
        return rule;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>同一个输入只能参与一条映射规则（轴向规则按轴占用判定；手柄按按键/档位/方向占用判定）。</summary>
    private string? FindConflict(MappingRule candidate)
    {
        if (candidate.Source == MappingSource.Gamepad)
        {
            foreach (var other in _allRules)
            {
                if (other.Id == candidate.Id) continue;
                if (other.Source == MappingSource.Gamepad && other.PadInput == candidate.PadInput)
                    return $"该手柄输入已参与其他映射：{MappingLabels.Input(other)}";
            }

            return null;
        }

        bool isAxis = candidate.Source == MappingSource.MouseAxis;

        foreach (var other in _allRules)
        {
            if (other.Id == candidate.Id) continue;

            if (!isAxis)
            {
                if (other.Source != MappingSource.MouseAxis &&
                    other.Source == candidate.Source &&
                    other.KeyCode == candidate.KeyCode)
                    return $"该输入已参与其他映射：{MappingLabels.Input(other)}";
                continue;
            }

            if (other.Source != MappingSource.MouseAxis) continue; // 方向键/摇杆按键不是「轴」

            foreach (int axis in new[] { candidate.AxisX, candidate.AxisY })
            {
                if (axis < 0) continue;
                if (other.AxisX == axis || other.AxisY == axis)
                    return $"该轴向已被占用：{MappingLabels.Axis((StickAxis)axis)}（{MappingLabels.Input(other)}）";
            }
        }

        return null;
    }
}
