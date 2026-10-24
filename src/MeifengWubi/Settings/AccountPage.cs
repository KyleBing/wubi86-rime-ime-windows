using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Meifeng.Core.Sync;
using MeifengWubi.Ui;

namespace MeifengWubi.Settings;

// 账户页：只负责登录。同步和扩展词库在用户词页
internal static class AccountPage
{
    public static FrameworkElement Build() =>
        UiKit.Page("账户", "登录门户同步账号。", UiKit.Card(null, null, new AccountForm()));
}

internal sealed class AccountForm : StackPanel
{
    private readonly TextBox _base = UiKit.Field(placeholder: WubiCloudConfig.DefaultBaseUrl, width: 320);
    private readonly TextBox _email = UiKit.Field(placeholder: "邮箱", width: 320);
    private readonly PasswordBox _password = new() { Width = 320, FontSize = 13 };
    private readonly TextBlock _account = UiKit.Label("", 13, wrap: true);
    private readonly Button _login = new() { Content = "登录", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _logout = new() { Content = "退出登录", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = UiKit.Label("", 12, secondary: true, wrap: true);
    private readonly TextBlock _registerInfo = UiKit.Label(
        "付费申请同步账号，实现多端个人用户词同步。",
        12,
        secondary: true,
        wrap: true);
    private readonly Button _registerApply = new() { Content = "在浏览器中申请注册", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _loginForm;
    private bool _busy;

    public AccountForm()
    {
        _login.SetResourceReference(StyleProperty, "AccentButtonStyle");
        _login.Click += (_, _) => Login();
        _logout.Click += (_, _) => Logout();
        _password.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Login();
        };
        _registerApply.Click += (_, _) => OpenRegisterApply();
        _loginForm = UiKit.VStack(8, Labeled("请求地址", _base), Labeled("邮箱", _email), Labeled("密码", _password), _login);
        _account.MaxWidth = _status.MaxWidth = _registerInfo.MaxWidth = 460;
        _account.HorizontalAlignment = _status.HorizontalAlignment = _registerInfo.HorizontalAlignment = HorizontalAlignment.Left;
        UiKit.AddSpaced(this, 8,
        [
            UiKit.Label("登录", 13, FontWeights.SemiBold),
            _account,
            _loginForm,
            _logout,
            _status,
            UiKit.Label("申请注册", 13, FontWeights.SemiBold),
            _registerInfo,
            _registerApply
        ]);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
        Refresh();
    }

    private static StackPanel Labeled(string title, Control field)
    {
        var caption = UiKit.Label(title, 12);
        caption.Width = 64;
        return UiKit.HStack(8, caption, field);
    }

    private void Refresh()
    {
        var loggedIn = WubiCloudConfig.IsLoggedIn;
        UiKit.Set(_base, WubiCloudConfig.BaseUrl);
        if (_email.Text.Length == 0) UiKit.Set(_email, WubiCloudConfig.UserInfo?.Email ?? "");
        if (WubiCloudConfig.UserInfo is { IsLoggedIn: true } user)
        {
            _account.Text = $"已登录：{(user.Nickname.Length == 0 ? user.Email : user.Nickname)}";
        }
        else
        {
            _account.Text = "未登录。与码表助手、iOS、macOS 共用 portal 账号。";
        }
        _loginForm.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        _logout.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        _login.IsEnabled = !_busy;
    }

    private async void Login()
    {
        var mail = _email.Text.Trim();
        var password = _password.Password;
        if (mail.Length == 0 || password.Length == 0)
        {
            UiKit.SetTone(_status, "请填写邮箱和密码", Tone.Error);
            return;
        }
        if (_busy) return;
        _busy = true;
        UiKit.SetTone(_status, "登录中…", Tone.Idle);
        Refresh();
        try
        {
            var info = await WubiCloudApi.LoginAsync(mail, password, _base.Text);
            _password.Password = "";
            UiKit.SetTone(_status, $"登录成功：{(info.Nickname.Length == 0 ? info.Email : info.Nickname)}", Tone.Idle);
            if (UserDictAutoSync.IsEnabled) UserDictAutoSync.SyncSoon();
        }
        catch (Exception e)
        {
            UiKit.SetTone(_status, e.Message, Tone.Error);
        }
        _busy = false;
        Refresh();
    }

    private void Logout()
    {
        WubiCloudConfig.Logout();
        _password.Password = "";
        UiKit.SetTone(_status, "已退出登录", Tone.Idle);
        Refresh();
    }

    private void OpenRegisterApply()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WubiCloudConfig.RegisterApplyUrl) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            UiKit.SetTone(_status, e.Message, Tone.Error);
        }
    }
}
