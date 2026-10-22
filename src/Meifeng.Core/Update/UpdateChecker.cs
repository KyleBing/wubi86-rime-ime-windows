using System.Globalization;
using System.Net.Http.Headers;
using Meifeng.Core.Config;

namespace Meifeng.Core.Update;

public enum UpdateCheckStatus { NewVersion, UpToDate, Failed }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateManifest? Manifest = null, string? Error = null);

// 查更新：只读 Release 附件里的 update.json，缓存 24 小时。
// 主地址是本仓库的固定附件名，备用地址留给国内镜像。
public static class UpdateChecker
{
    public const string WindowsSource =
        "https://github.com/KyleBing/wubi86-rime-ime-windows/releases/latest/download/update.json";
    public const string WindowsMirror = "https://kylebing.cn/wubi/update/windows.json";

    // 自动检查的间隔；手动点「检查更新」不受限制
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private const string KeyLastCheckAt = "update.lastCheckAt";
    private const string KeySkippedVersion = "update.skippedVersion";
    private const string KeyAutoCheck = "update.autoCheck";
    private const string KeyLastNotified = "update.lastNotifiedVersion";

    // 清单是静态文件，超时收紧，避免设置窗口一直转圈
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // 启动时自动检查，默认开启
    public static bool AutoCheck
    {
        get => PrefsStore.Shared.GetBool(KeyAutoCheck) ?? true;
        set => PrefsStore.Shared.Set(KeyAutoCheck, value);
    }

    // 上次成功取到清单的时间；没查过时为 null
    public static DateTimeOffset? LastCheckAt
    {
        get
        {
            var raw = PrefsStore.Shared.GetString(KeyLastCheckAt);
            return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
        }
    }

    // 用户选择「以后再说」的版本，自动提示时跳过；手动检查仍会显示
    public static string? SkippedVersion
    {
        get => PrefsStore.Shared.GetString(KeySkippedVersion);
        set => PrefsStore.Shared.Set(KeySkippedVersion, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    // 已经用托盘气泡提醒过的版本，避免每次启动都弹
    public static string? LastNotifiedVersion
    {
        get => PrefsStore.Shared.GetString(KeyLastNotified);
        set => PrefsStore.Shared.Set(KeyLastNotified, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public static bool IsAutoCheckDue => LastCheckAt is not { } at || DateTimeOffset.UtcNow - at >= AutoCheckInterval;

    // current 用程序集版本传入，方便单测；网络失败不抛异常，交给界面决定怎么提示
    public static async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        string? lastError = null;
        foreach (var url in new[] { WindowsSource, WindowsMirror })
        {
            try
            {
                var json = await FetchAsync(url, current, ct).ConfigureAwait(false);
                var manifest = UpdateManifest.Parse(json);
                if (manifest == null)
                {
                    lastError = "更新清单格式不正确";
                    continue;
                }
                // 两个平台的清单分开托管，防止拿错包
                if (manifest.Platform.Length > 0 && !manifest.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = $"清单平台是 {manifest.Platform}";
                    continue;
                }

                PrefsStore.Shared.Set(KeyLastCheckAt, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                // 清单里的版本不比当前新时同样算检查成功
                return UpdateManifest.IsNewer(Format(current), manifest.Version)
                    ? new UpdateCheckResult(UpdateCheckStatus.NewVersion, manifest)
                    : new UpdateCheckResult(UpdateCheckStatus.UpToDate, manifest);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                lastError = e.Message;
            }
        }

        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, lastError ?? "无法连接更新服务器");
    }

    // 程序集版本可能带第四段 0，显示与比较都按三段处理
    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    private static async Task<string> FetchAsync(string url, Version current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MeifengWubi", Format(current)));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        // 404 通常表示仓库还没有发布过版本，给一句能看懂的提示
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("更新服务器上还没有发布版本");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
