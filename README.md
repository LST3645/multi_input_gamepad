# 多输入整合手柄（MultiInput）

把**物理 Xbox 手柄 + 键盘 + 鼠标**整合成一个**虚拟 Xbox Series 手柄**：键盘鼠标可以映射成手柄的任意按键/方向/扳机，物理手柄的输入实时转发，游戏只看到一台手柄。

## 功能

- **物理手柄转发**：Xbox Elite 2 等 XInput 手柄的按键、摇杆、扳机实时转发到虚拟手柄
- **捕获模式**：`SHIFT+ALT+\` 一键开关；开启时键盘鼠标全部拦截，只用于驱动虚拟手柄
- **映射编辑**：可视化里单击任意按键直接新建/编辑；每个输入最多 3 个输出槽，逐槽延迟（≤3 级）与连按
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

1. 运行 `dist\MultiInput.App.exe`（首次会弹 UAC，这是必须的）；
2. 首次启动会安装 HIDMaestro 的虚拟手柄驱动（本地自签名，Windows 可能提示确认）；
3. 主界面出现后，状态栏显示「虚拟手柄：已启动」即就绪；
4. 按 `SHIFT+ALT+\` 或点击顶部状态胶囊开启/关闭捕获模式。

关闭主窗口即退出（退出前会保存当前配置）。

## 使用

- **新建映射**：点「＋新建」，或在可视化面板里单击某个按键/鼠标键/滚轮 → 编辑器自动预选该输入；
- **编辑映射**：点列表行，或再次单击可视化里已映射的输入；
- **配置管理**：卡片标题行 `[配置▾][导入][导出] … [＋新建]`；下拉列表项右侧 `✎` 改名、`×` 删除（`default` 不可删，改名后会静默补建新的 `default`）；
- **退出导入的配置**：关闭程序时按导入时的原始文件名写入 `cache\` 作为应急备份。

## 文件位置

| 内容 | 路径 |
|---|---|
| 配置 | `%LOCALAPPDATA%\MultiInput\profiles\<名称>.json` |
| 当前激活配置名 | `%LOCALAPPDATA%\MultiInput\settings.json` |
| 导入备份 | `%LOCALAPPDATA%\MultiInput\cache\` |
| HidHide 自愈标记 | `%LOCALAPPDATA%\MultiInput\hidhide.state` |

一套配置 = 映射规则 + 归一化开关 + 死区参数，导入导出就是这一个文件。

## 已知限制

1. **虚拟手柄固定在 XInput 1 号槽**：被隐藏的物理手柄仍占 0 号槽（HidHide 只拦访问、不移除枚举），只读 0 号槽的游戏会认为没有手柄；
2. **扳机震动不支持**：XInput 只暴露左右主电机，Elite 2 的 impulse trigger 不走 XInput；
3. **隐藏只对新句柄生效**：必须先启动本程序，**再启动游戏/Steam 等会枚举手柄的程序**；程序启动前就已经在运行的进程（例如一直开着的 Steam）仍持有物理手柄的旧句柄，看不到隐藏效果，需要重启该程序；
4. **未装 HidHide 时**：物理与虚拟手柄会同时出现，游戏可能把一次按键算成两次（界面会提示）；
5. 虚拟手柄是 `xbox-series-xs-bt`（蓝牙形态呈现），极少数只认 GIP/USB 有线 Series 的游戏可能需要换成 `xbox-360-wired` 配置档；
6. **Steam 的控制器列表可能仍显示物理手柄**：那是枚举级（名称来自 PnP 属性）的历史条目，实测 Steam 重启后 XInput / HID / Windows.Gaming.Input 三条路径都读不到物理手柄，不影响游戏内输入。

## 构建

```powershell
dotnet build MultiInput.slnx
dotnet run tools/engine-check/engine-check.cs          # 映射/死区断言
dotnet publish src/MultiInput.App/MultiInput.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

> 程序清单要求管理员，因此**调试时 IDE / 终端本身也要以管理员身份运行**，否则启动会直接失败（错误 740）。
> 图标可用 `python tools/make-icon.py` 重新生成。

## 目录结构

| 目录 | 内容 |
|---|---|
| `src/MultiInput.Core` | 映射引擎、摇杆数学、死区状态机（无 UI、无平台依赖） |
| `src/MultiInput.Input` | 低层键鼠钩子、RawInput、XInput、HidHide 控制 |
| `src/MultiInput.Output` | HIDMaestro 虚拟手柄后端 |
| `src/MultiInput.App` | WPF 界面、捕获状态机、转发循环 |
| `src/MultiInput.Spike` | M0 技术验证程序（保留作参考） |
| `tools/engine-check` | 引擎断言脚本 |
| `lib/` | HIDMaestro.Core.dll 等第三方二进制 |
| `架构方案.md` | 架构与关键决策文档 |

## 第三方组件

- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)（MIT）— 虚拟手柄呈现。仓库只包含运行时需要的 `lib/HIDMaestro.Core.dll`（取自 v2.3.0 发布包），完整包（含 Profile 检查/提取等辅助工具）请从上游下载，许可文本见 `lib/LICENSE` 与 `lib/THIRD-PARTY-NOTICES.txt`
- [HidHide](https://github.com/nefarius/HidHide)（MIT，可选）— 物理手柄隐藏，需用户自行安装（官方 x64 安装包），程序只通过其 CLI 控制

## 许可证

[MIT](LICENSE)

