using System.Windows;
using System.Windows.Threading;
using Meifeng.Core;
using Meifeng.Core.Config;
using Meifeng.Core.Engine;
using Meifeng.Core.Input;
using Meifeng.Core.Ipc;
using Meifeng.Core.Lexicon;
using Meifeng.Core.Sync;
using MeifengWubi.Dialogs;
using MeifengWubi.Settings;
using MeifengWubi.Ui;
using Microsoft.Win32;

namespace MeifengWubi;

// 输入法服务进程：librime、管道服务、候选窗、托盘、全局造词键和设置窗口都在这里
internal sealed class App : Application
{
    private const string MutexName = "Local\\MeifengWubi.Server";
    private const string ShowSettingsEventName = "Local\\MeifengWubi.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettings;
    private ImeUi? _ui;
    private InputController? _controller;
    private PipeServer? _pipe;
    private TrayIcon? _tray;
    private GlobalHotKey? _hotKey;
    private RankHotKey? _rankHotKey;

    [STAThread]
    public static int Main(string[] args)
    {
        var wantSettings = args.Contains("--settings", StringComparer.OrdinalIgnoreCase);
        var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            // 已在运行：带 --settings（或直接双击）时让已有进程打开设置
            if (!args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowSettingsEventName);
                    signal.Set();
                }
                catch
                {
                    // 旧进程还没建好事件，忽略
                }
            }
            mutex.Dispose();
            return 0;
        }
        var app = new App { _mutex = mutex };
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += (_, _) => app.Boot(openSettings: wantSettings || !args.Contains("--background", StringComparer.OrdinalIgnoreCase));
        return app.Run();
    }

    // openSettings：手动启动（非开机/文本服务拉起）时顺便打开设置
    private void Boot(bool openSettings)
    {
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"ui exception: {e.Exception}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"fatal: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write($"task exception: {e.Exception}");
            e.SetObserved();
        };

        AppPaths.EnsureDirectories();
        _ui = new ImeUi(Dispatcher);
        _controller = new InputController(RimeEngine.Shared, _ui);
        _ui.Controller = _controller;

        _tray = new TrayIcon(_controller.AsciiMode);
        _tray.ToggleRequested += () => Task.Run(() => _controller.ToggleAscii());
        _tray.SettingsRequested += () => SettingsWindow.Open();
        _tray.QuickAddRequested += QuickAddWindow.Present;
        _tray.DeployRequested += DeployFromMenu;
        _tray.ExitRequested += Quit;
        _controller.AsciiModeChanged += ascii => Dispatcher.BeginInvoke(() => _tray?.SetMode(ascii));

        _pipe = new PipeServer(_controller, _ui);
        _pipe.OpenRequested += what => Dispatcher.BeginInvoke(() => HandleOpen(what));
        try
        {
            _pipe.Start();
        }
        catch (Exception e)
        {
            Log.Write($"pipe start failed: {e.Message}");
        }

        _hotKey = new GlobalHotKey(QuickAddWindow.Present);
        // 候选条显示时才占住 Ctrl+Alt+0，按下后在界面线程直接打开排序窗
        _rankHotKey = new RankHotKey(() => _controller.OpenRankFromHotKey());
        _ui.CandidatePopupChanged += visible => _rankHotKey.SetArmed(visible);

        // 后台预热引擎：首次安装要整本编译码表，可能要几秒
        Task.Run(() =>
        {
            RimeResourceDeployer.SyncBundledResourcesIfNeeded();
            lock (_controller.SyncRoot) RimeEngine.Shared.EnsureStarted();
            UserDictAutoSync.Install();
        });

        // 行为设置（页大小、翻页键）写盘后，空闲时重载使之生效
        BehaviorPrefs.Changed += () => Task.Run(() =>
        {
            lock (_controller.SyncRoot) RimeEngine.Shared.FlushReloadIfIdle();
        });
        // 自动同步把云端改动合并进本地后，空闲时重载用户词
        UserDictAutoSync.UserDictDidChange += () => Task.Run(() =>
        {
            UserDictWriter.MarkNeedsDeploy();
            lock (_controller.SyncRoot) RimeEngine.Shared.FlushReloadIfIdle();
        });
        AppearancePrefs.Changed += () => _ui?.RefreshAppearance();
        // 系统深浅色切换后重画候选窗
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) _ui?.RefreshAppearance();
        };

        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var waiter = new Thread(() =>
        {
            while (_showSettings.WaitOne())
            {
                Dispatcher.BeginInvoke(() => SettingsWindow.Open());
            }
        }) { IsBackground = true, Name = "settings-signal" };
        waiter.Start();

        if (openSettings) SettingsWindow.Open();
    }

    // 文本服务语言栏菜单的请求
    private void HandleOpen(string what)
    {
        switch (what)
        {
            case "deploy": DeployFromMenu(); break;
            case "quickadd": QuickAddWindow.Present(); break;
            default: SettingsWindow.Open(); break;
        }
    }

    // 菜单「部署」：全量部署后提示结果（对应 macOS deployFromMenu）
    private async void DeployFromMenu()
    {
        var (ok, message) = await LexiconActions.DeployAsync();
        if (ok) UiKit.Alert(null, "部署完成", "当前词库配置已重新部署。");
        else if (message.Contains("重载失败")) UiKit.Alert(null, "部署完成，但输入法重载失败", "请重新切换输入法；若仍无法输入，请从托盘退出玫枫五笔后再打开。");
        else UiKit.Alert(null, "部署失败", message);
    }

    private void Quit()
    {
        try
        {
            _hotKey?.Dispose();
            _rankHotKey?.Dispose();
            _pipe?.Dispose();
            _tray?.Dispose();
            _ui?.Dispose();
            RimeEngine.Shared.Shutdown();
        }
        catch (Exception e)
        {
            Log.Write($"quit: {e.Message}");
        }
        _mutex?.ReleaseMutex();
        Shutdown();
    }
}
