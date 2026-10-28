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
    private readonly TextBlock _registerInfo = UiKit.Label(RegistrationApplyCopy.Hint, 12, secondary: true, wrap: true);
    private readonly Button _registerApply = new() { Content = "在浏览器中申请注册", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _loginForm;
    private readonly StackPanel _registerSection;
    // 账号数据区：删除线上同步数据 / 注销账号
    private readonly TextBlock _accountDataInfo = UiKit.Label(
        "删除线上同步数据：清空云端备份的个人词库，同步次数归零；本机词库和账号资料不受影响。\n" +
        "注销账号：永久删除账号及全部线上数据（个人词库备份、本人提交的词条、邀请码、邮箱验证记录、本人提交的注册申请），无法登录也无法恢复；本机词库仍保留在本地。\n" +
        "两项操作都需要输入完整账号邮箱后才能执行。",
        12, secondary: true, wrap: true);
    private readonly Button _clearSync = new() { Content = "删除线上同步数据", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _destroyAccount = new() { Content = "注销账号", Padding = new Thickness(14, 4, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _accountDataSection;
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
        _clearSync.Click += (_, _) => ClearSyncData();
        _destroyAccount.Click += (_, _) => DestroyAccount();
        _loginForm = UiKit.VStack(8, Labeled("请求地址", _base), Labeled("邮箱", _email), Labeled("密码", _password), _login);
        _account.MaxWidth = _status.MaxWidth = _registerInfo.MaxWidth = _accountDataInfo.MaxWidth = 460;
        _account.HorizontalAlignment = _status.HorizontalAlignment = _registerInfo.HorizontalAlignment = _accountDataInfo.HorizontalAlignment = HorizontalAlignment.Left;
        _registerSection = UiKit.VStack(8,
            UiKit.Label("申请注册", 13, FontWeights.SemiBold),
            _registerInfo,
            _registerApply);
        _accountDataSection = UiKit.VStack(8,
            UiKit.Label("账号数据", 13, FontWeights.SemiBold),
            _accountDataInfo,
            UiKit.HStack(8, _clearSync, _destroyAccount));
        UiKit.AddSpaced(this, 8,
        [
            UiKit.Label("登录", 13, FontWeights.SemiBold),
            _account,
            _loginForm,
            _logout,
            _status,
            _registerSection,
            _accountDataSection
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
        // 已登录不再展示申请注册
        _registerSection.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        // 账号数据只在已登录时展示
        _accountDataSection.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
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

    // 删除线上同步数据：只清空云端个人词库，账号保持登录；需输入完整账号邮箱确认
    private async void ClearSyncData()
    {
        if (_busy) return;
        var mail = ConfirmByEmail("删除线上同步数据",
            "云端备份的个人词库会被删除，同步次数归零。本机词库和账号资料不受影响，下次自动同步时会重新上传本机词库。",
            "删除");
        if (mail == null) return;
        _busy = true;
        UiKit.SetTone(_status, "正在删除线上同步数据…", Tone.Idle);
        Refresh();
        try
        {
            var message = await WubiCloudApi.ClearSyncDataAsync();
            UiKit.SetTone(_status, message, Tone.Success);
        }
        catch (Exception e)
        {
            UiKit.SetTone(_status, e.Message, Tone.Error);
        }
        _busy = false;
        Refresh();
    }

    // 注销账号：先输入完整邮箱，再在服务端删除全部用户数据并清本地登录态
    private async void DestroyAccount()
    {
        if (_busy) return;
        if (ConfirmByEmail("注销账号",
                "注销会永久删除账号资料、云端个人词库、本人提交的词条、邀请码、邮箱验证记录和本人提交的注册申请。注销后无法登录，也无法恢复；本机词库仍保留在本地。",
                "永久注销") == null) return;
        _busy = true;
        UiKit.SetTone(_status, "正在注销…", Tone.Idle);
        Refresh();
        try
        {
            var message = await WubiCloudApi.DestroyAccountAsync();
            WubiCloudConfig.Logout();
            _password.Password = "";
            UiKit.SetTone(_status, message, Tone.Idle);
        }
        catch (Exception e)
        {
            UiKit.SetTone(_status, e.Message, Tone.Error);
        }
        _busy = false;
        Refresh();
    }

    // 破坏性操作确认：要求完整输入当前账号邮箱，一致才返回输入的邮箱，否则返回 null
    private string? ConfirmByEmail(string title, string message, string confirmTitle)
    {
        var expected = (WubiCloudConfig.UserInfo?.Email ?? "").Trim();
        if (expected.Length == 0)
        {
            UiKit.SetTone(_status, "没有读到账号邮箱，请重新登录后再试", Tone.Error);
            return null;
        }
        var typed = UiKit.Prompt(Window.GetWindow(this), title,
            message + $"\n\n请输入账号邮箱 {expected} 以确认。", "", "输入账号邮箱", confirmTitle, destructive: true);
        if (typed == null) return null;
        typed = typed.Trim();
        if (!string.Equals(typed, expected, StringComparison.OrdinalIgnoreCase))
        {
            UiKit.SetTone(_status, "邮箱不一致，操作已取消", Tone.Error);
            return null;
        }
        return typed;
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
