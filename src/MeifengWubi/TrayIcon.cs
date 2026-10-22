using Meifeng.Core.Config;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MeifengWubi;

// 通知区域图标：显示中/英状态，菜单提供切换、设置、造词、部署、退出
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly Forms.ToolStripMenuItem _modeItem = new();
    private readonly Drawing.Icon? _chinese = LoadIcon("ModeChinese.ico");
    private readonly Drawing.Icon? _english = LoadIcon("ModeEnglish.ico");
    private readonly Drawing.Icon? _fallback = LoadIcon("Tray.ico");

    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? QuickAddRequested;
    public event Action? DeployRequested;
    public event Action? UpdateRequested;
    public event Action? ExitRequested;

    public TrayIcon(bool asciiMode)
    {
        var menu = new Forms.ContextMenuStrip();
        _modeItem.Click += (_, _) => ToggleRequested?.Invoke();
        menu.Items.Add(_modeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("玫枫五笔设置…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("添加自造词…", null, (_, _) => QuickAddRequested?.Invoke());
        menu.Items.Add("部署", null, (_, _) => DeployRequested?.Invoke());
        menu.Items.Add("检查更新…", null, (_, _) => UpdateRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        // 左键单击切换中英，双击打开设置
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ToggleRequested?.Invoke();
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
        // 新版本气泡点开后打开帮助页的「版本与更新」
        _icon.BalloonTipClicked += (_, _) => UpdateRequested?.Invoke();
        SetMode(asciiMode);
        _icon.Visible = true;
    }

    // 中英切换后更新图标与提示
    public void SetMode(bool asciiMode)
    {
        _icon.Icon = (asciiMode ? _english : _chinese) ?? _fallback;
        _icon.Text = asciiMode ? "玫枫五笔 · 英文" : "玫枫五笔 · 中文";
        _modeItem.Text = asciiMode ? "切换到中文" : "切换到英文";
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        _icon.ShowBalloonTip(4000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private static Drawing.Icon? LoadIcon(string name)
    {
        try
        {
            var path = Path.Combine(AppPaths.ResourcesDir, "Brand", name);
            return File.Exists(path) ? new Drawing.Icon(path, Forms.SystemInformation.SmallIconSize) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _chinese?.Dispose();
        _english?.Dispose();
        _fallback?.Dispose();
    }
}
