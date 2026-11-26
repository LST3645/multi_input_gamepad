# 多输入整合手柄（multi_input_gamepad）

把**物理 Xbox 手柄 + 键盘 + 鼠标**整合成一个**虚拟 Xbox Series 手柄**：键盘鼠标可以映射成手柄的任意按键/方向/扳机，物理手柄的输入实时转发，游戏只看到一台手柄。

## 功能

- **物理手柄转发**：Xbox Elite 2 等 XInput 手柄的按键、摇杆、扳机实时转发到虚拟手柄
- **捕获模式**：`SHIFT+ALT+\` 一键开关；开启时键盘鼠标全部拦截，只用于驱动虚拟手柄
- **映射编辑**：可视化里单击任意按键直接新建/编辑；每个输入最多 3 个输出槽，逐槽延迟（≤3 级）与连按
- **手柄映射**：把输入手柄的按键、LT/RT 四档、摇杆四个方向映射成任意手柄输出，**保存后立即生效（不依赖捕获开关）**；参与映射的输入不再透传——按键按位停、LT/RT 任一档有规则即整根停、**摇杆任一方向有规则即整根停**。摇杆按扇区判定（独立区 70° + 两侧共同区各 10°，位移阈值 0.25，两个参数都可调）：共同区里相邻两个方向同时成立（`左X+` 与 `左Y+` 都触发）
- **摇杆方向绑定**：8 个方向目标（左/右摇杆 X/Y 正负），支持「归一化摇杆」（圆形/方形）开关
- **物理摇杆死区**：径向死区 + 死区内同向推进确认（参数在配置文件里可改）
- **震动转发**：游戏发给虚拟手柄的震动按包内时长转发到物理手柄主电机
- **多配置**：`profiles\<名称>.json`，支持导入/导出/改名/删除；关闭时按导入文件名备份到 `cache\`
- **输入可视化**：手柄、108 键键盘、鼠标三面板实时高亮；另有输出端面板反映实际提交给虚拟手柄的状态
- **物理手柄隐藏**：集成 [HidHide](https://github.com/nefarius/HidHide)，程序运行期间把物理手柄从其它程序隐藏，避免游戏把一次按键算成两次

## 系统要求

| 项 | 要求 |
|---|---|
| 系统 | Windows 10 / 11 x64 |
| 权限 | **管理员**（创建虚拟手柄必需，已写进程序清单） |
| 运行时 | 发布版为自包含单文件，无需安装 .NET |
| 可选 | HidHide（隐藏物理手柄用；未安装时功能降级为不隐藏） |

## 安装与启动

1. 运行 `dist\multi_input_gamepad.exe`（首次会弹 UAC，这是必须的）；
2. 首次启动会安装 HIDMaestro 的虚拟手柄驱动（本地自签名，Windows 可能提示确认）；
3. 主界面出现后，状态栏显示「虚拟手柄：已启动」即就绪；
4. 按 `SHIFT+ALT+\` 或点击顶部状态胶囊开启/关闭捕获模式。

关闭主窗口即退出（退出前会保存当前配置）。

## 使用

- **新建映射**：点「＋新建」，或在可视化面板里单击某个按键/鼠标键/滚轮 → 编辑器自动预选该输入；
- **手柄映射**：编辑器来源选「手柄」，先点输入手柄上的按键/扳机档位/摇杆方向，再点输出手柄上的目标；例：`50%LT → A` 与 `75%LT → B` 同时存在时，LT 按过 75% 就同时按 A 和 B；`左摇杆右 → A` 则在摇杆偏向右方（含右上/右下共同区）且位移超过阈值时按 A，而**左摇杆只要有一个方向参与映射，整根就不再透传**（游戏只看到映射出的按键；右摇杆不受影响）；
- **可视化里直接改手柄映射**：主窗口左侧输入手柄面板上，已映射的按键有金色描边，单击即可新建/编辑该条（LT/RT 与左右摇杆不在可视化里点选，请在编辑器「手柄」页操作）；
- **摇杆参数**：主窗口输入手柄卡片底部可改「位移 >」（默认 0.25）与「独立扇区」（默认 70°），随配置保存；
- **编辑映射**：点列表行，或再次单击可视化里已映射的输入；
- **映射列表格式**：每行 `来源, 输入 → 输出`（输出端只有手柄，不再重复写来源），如 `键盘, K → A`、`手柄, LS上 → RS上`、`鼠标, 左键 → A`；`LS` = 左摇杆、`RS` = 右摇杆；输出槽的延迟直接写成 `100ms A`（不写「延迟」）；行内不再有「立即/末项」摘要，开启连按时该行下方显示 `连按, 每 N ms`；
- **配置管理**：卡片标题行 `[配置▾][导入][导出] … [＋新建]`；下拉列表项右侧 `✎` 改名、`×` 删除（`default` 不可删，改名后会静默补建新的 `default`）；
- **退出导入的配置**：关闭程序时按导入时的原始文件名写入 `cache\` 作为应急备份。

## 文件位置

| 内容 | 路径 |
|---|---|
| 配置 | `%LOCALAPPDATA%\multi_input_gamepad\profiles\<名称>.json` |
| 当前激活配置名 | `%LOCALAPPDATA%\multi_input_gamepad\settings.json` |
| 导入备份 | `%LOCALAPPDATA%\multi_input_gamepad\cache\` |
| HidHide 自愈标记 | `%LOCALAPPDATA%\multi_input_gamepad\hidhide.state` |

一套配置 = 映射规则 + 归一化开关 + 死区参数 + 摇杆映射参数，导入导出就是这一个文件。

## 已知限制

1. **虚拟手柄固定在 XInput 1 号槽**：被隐藏的物理手柄仍占 0 号槽（HidHide 只拦访问、不移除枚举），只读 0 号槽的游戏会认为没有手柄；
2. **扳机震动不支持**：XInput 只暴露左右主电机，Elite 2 的 impulse trigger 不走 XInput；
3. **隐藏只对新句柄生效**：必须先启动本程序，**再启动游戏/Steam 等会枚举手柄的程序**；程序启动前就已经在运行的进程（例如一直开着的 Steam）仍持有物理手柄的旧句柄，看不到隐藏效果，需要重启该程序；
4. **未装 HidHide 时**：物理与虚拟手柄会同时出现，游戏可能把一次按键算成两次（界面会提示）；
5. 虚拟手柄是 `xbox-series-xs-bt`（蓝牙形态呈现），极少数只认 GIP/USB 有线 Series 的游戏可能需要换成 `xbox-360-wired` 配置档；
6. **Steam 的控制器列表可能仍显示物理手柄**：那是枚举级（名称来自 PnP 属性）的历史条目，实测 Steam 重启后 XInput / HID / Windows.Gaming.Input 三条路径都读不到物理手柄，不影响游戏内输入。
7. **手柄映射不受捕获开关限制**：规则保存后立即生效（捕获只管键盘鼠标是否被拦截，与手柄管线无关）；摇杆判定用**死区之后**的值，与游戏看到的一致。

## 构建

```powershell
dotnet build multi_input_gamepad.slnx
dotnet run tools/engine-check/engine-check.cs          # 映射/死区断言
dotnet publish src/multi_input_gamepad.App/multi_input_gamepad.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

> 程序清单要求管理员，因此**调试时 IDE / 终端本身也要以管理员身份运行**，否则启动会直接失败（错误 740）。
> 图标可用 `python tools/make-icon.py` 重新生成。

## 目录结构

| 目录 | 内容 |
|---|---|
| `src/multi_input_gamepad.Core` | 映射引擎、摇杆数学/扇区、死区状态机（无 UI、无平台依赖） |
| `src/multi_input_gamepad.Input` | 低层键鼠钩子、RawInput、XInput、HidHide 控制 |
| `src/multi_input_gamepad.Output` | HIDMaestro 虚拟手柄后端 |
| `src/multi_input_gamepad.App` | WPF 界面、捕获状态机、转发循环 |
| `src/multi_input_gamepad.Spike` | M0 技术验证程序（保留作参考） |
| `tools/engine-check` | 引擎断言脚本 |
| `lib/` | HIDMaestro.Core.dll 等第三方二进制 |

## 第三方组件

- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)（MIT）— 虚拟手柄呈现。仓库只包含运行时需要的 `lib/HIDMaestro.Core.dll`（取自 v2.3.0 发布包），完整包（含 Profile 检查/提取等辅助工具）请从上游下载，许可文本见 `lib/LICENSE` 与 `lib/THIRD-PARTY-NOTICES.txt`
- [HidHide](https://github.com/nefarius/HidHide)（MIT，可选）— 物理手柄隐藏，需用户自行安装（官方 x64 安装包），程序只通过其 CLI 控制

## 许可证

[MIT](LICENSE)

