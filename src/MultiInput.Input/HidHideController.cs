using System.Diagnostics;
using Microsoft.Win32;

namespace MultiInput.Input;

/// <summary>
/// HidHide 集成：程序运行期间把物理 Xbox 手柄从其它进程隐藏，只对本程序可见，
/// 使游戏只枚举到虚拟手柄（消除同一按键来自两台设备的「连击」）。
/// <para>
/// 通过官方 CLI 控制（免管理员）。未安装时 <see cref="TryCreate"/> 返回 null，调用方降级为不隐藏。
/// 隐藏只对之后新打开的句柄生效：程序启动即隐藏，游戏随后启动才看得到效果。
/// </para>
/// </summary>
public sealed class HidHideController
{
    /// <summary>虚拟手柄（xbox-series-xs-bt，VID_045E&amp;PID_0B13）不能隐藏，否则游戏看不到输出设备。</summary>
    private const string VirtualPadPid = "0B13";

    private readonly string _cliPath;
    private readonly HashSet<string> _addedDevices = new(StringComparer.OrdinalIgnoreCase);
    private string? _addedApp;
    private bool _restoreCloak;
    private bool _restored;

    private HidHideController(string cliPath) => _cliPath = cliPath;

    /// <summary>探测 HidHide 安装；未安装返回 null。</summary>
    public static HidHideController? TryCreate()
    {
        foreach (string candidate in CliCandidates())
            if (File.Exists(candidate))
                return new HidHideController(candidate);

        return null;
    }

    private static IEnumerable<string> CliCandidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
        yield return Path.Combine(programFiles, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe");

        // 安装目录可能被改：注册表里记录的是安装根目录
        string? root = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide");
            root = key?.GetValue("Path") as string;
        }
        catch { /* 读不到就用标准路径 */ }

        if (!string.IsNullOrWhiteSpace(root))
        {
            yield return Path.Combine(root, "x64", "HidHideCLI.exe");
            yield return Path.Combine(root, "HidHideCLI.exe");
        }
    }

    /// <summary>
    /// 登记白名单、隐藏物理 Xbox 手柄、开启 cloak。幂等；失败只影响隐藏，不影响其它功能。
    /// <paramref name="markerPath"/> 用于识别上次异常退出：存在则说明上次没恢复，本次退出一定关闭 cloak。
    /// </summary>
    public void Apply(string markerPath)
    {
        bool crashedBefore = File.Exists(markerPath);
        _restoreCloak = crashedBefore ? false : IsCloaked();

        try { File.WriteAllText(markerPath, "1"); }
        catch { /* 标记失败只影响自愈，不影响隐藏 */ }

        try
        {
            var apps = ParseList(Run("--app-list"), "--app-reg");
            string? self = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(self) && !apps.Contains(self))
            {
                Run("--app-reg", self);
                _addedApp = self;
            }

            var hidden = ParseList(Run("--dev-list"), "--dev-hide");
            foreach (string instance in EnumeratePhysicalXboxInstances())
            {
                if (hidden.Contains(instance)) continue;
                Run("--dev-hide", instance);
                _addedDevices.Add(instance);
            }

            if (!IsCloaked()) Run("--cloak-on");
        }
        catch { /* HidHide 调用失败时保持不隐藏，程序照常工作 */ }
    }

    /// <summary>恢复正常状态：按 Apply 前的 cloak 状态还原，并撤销本次添加的条目。</summary>
    public void Restore(string markerPath)
    {
        if (_restored) return;
        _restored = true;

        try
        {
            // 先解除隐藏再撤白名单：避免撤销过程中自身失去白名单而读到被隐藏设备
            if (!_restoreCloak && IsCloaked()) Run("--cloak-off");

            foreach (string instance in _addedDevices) Run("--dev-unhide", instance);
            if (_addedApp is not null) Run("--app-unreg", _addedApp);
        }
        catch { /* 恢复失败时保留标记文件，下次启动自愈 */ }
        finally
        {
            try { File.Delete(markerPath); } catch { }
        }
    }

    private bool IsCloaked() => Run("--cloak-state").Contains("--cloak-on", StringComparison.Ordinal);

    /// <summary>
    /// 枚举物理 Xbox 手柄的设备实例路径（注册表 Enum 树，USB 与 HID 两条总线）。
    /// 只排除虚拟手柄自己的 PID；同一台机器上的其它 Xbox 手柄也会被隐藏（本程序只整合一个物理手柄）。
    /// </summary>
    private static IEnumerable<string> EnumeratePhysicalXboxInstances()
    {
        const string prefix = "VID_045E&PID_";

        foreach (string bus in new[] { "USB", "HID" })
        {
            using var busKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{bus}");
            if (busKey is null) continue;

            foreach (string deviceName in busKey.GetSubKeyNames())
            {
                if (!deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string pid = deviceName[prefix.Length..];
                int amp = pid.IndexOf('&');
                if (amp > 0) pid = pid[..amp];
                if (string.Equals(pid, VirtualPadPid, StringComparison.OrdinalIgnoreCase)) continue;

                using var deviceKey = busKey.OpenSubKey(deviceName);
                if (deviceKey is null) continue;

                foreach (string instance in deviceKey.GetSubKeyNames())
                    yield return $@"{bus}\{deviceName}\{instance}";
            }
        }
    }

    private static HashSet<string> ParseList(string cliOutput, string option)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in cliOutput.Split('\n'))
        {
            int start = line.IndexOf('"');
            int end = line.LastIndexOf('"');
            if (!line.Contains(option, StringComparison.Ordinal) || start < 0 || end <= start) continue;
            set.Add(line[(start + 1)..end]);
        }
        return set;
    }

    private string Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo(_cliPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("HidHideCLI 启动失败");
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(3000))
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException("HidHideCLI 无响应");
        }
        return output;
    }
}
