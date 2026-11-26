using System.Threading;
using System.Windows;
using MultiInputGamepad.Input;

namespace MultiInputGamepad.App;

public partial class App : Application
{
    /// <summary>主窗口标题（单实例激活时按标题查找已有窗口）。</summary>
    public const string WindowTitle = "多输入整合手柄";

    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, @"Local\MultiInputGamepad.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>二次启动时把已有实例的窗口唤到前台，然后退出本进程。</summary>
    private static void ActivateExistingWindow()
    {
        nint hwnd = NativeMethods.FindWindowW(null, WindowTitle);
        if (hwnd == 0) return;

        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, 9); // SW_RESTORE
        NativeMethods.SetForegroundWindow(hwnd);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(e);
    }
}