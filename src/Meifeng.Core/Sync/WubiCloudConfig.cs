using System.Text.Json;
using System.Text.Json.Serialization;
using Meifeng.Core.Config;

namespace Meifeng.Core.Sync;

// 云端同步账号。与码表助手、iOS、macOS 共用 portal 账号
public sealed record WubiCloudUserInfo(
    [property: JsonPropertyName("uid")] int Uid,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("token")] string Token)
{
    [JsonIgnore]
    public bool IsLoggedIn => Uid > 0 && Token.Length > 0;
}

public static class WubiCloudConfig
{
    public const string DefaultBaseUrl = "https://kylebing.cn/wubi/portal/";
    public const string SyncDictFileName = "wubi86_jidian_user.dict.yaml";
    public const string ExtraDictFileName = "wubi86_jidian_extra.dict.yaml";
    public const int SyncMaxContentLength = 40_000;

    private static readonly object Lock = new();

    // 账号或登录状态变化时触发
    public static event Action? Changed;

    private sealed class Store
    {
        [JsonPropertyName("baseURL")] public string BaseUrl { get; set; } = DefaultBaseUrl;
        [JsonPropertyName("user")] public WubiCloudUserInfo? User { get; set; }
        [JsonPropertyName("lastSyncAt")] public double LastSyncAt { get; set; }
        [JsonPropertyName("lastExtraPullAt")] public double LastExtraPullAt { get; set; }
    }

    // 补全协议和末尾斜杠；旧的 http 地址换成 https
    public static string ResolveBaseUrl(string? raw)
    {
        var b = (raw ?? "").Trim();
        if (b.Length == 0) b = DefaultBaseUrl;
        if (b.StartsWith("http://kylebing.cn/", StringComparison.Ordinal)) b = "https://" + b["http://".Length..];
        // 旧版默认指向 portal-go，拆分后自动切到五笔独立服务
        if (b == "https://kylebing.cn/portal/" || b == "https://kylebing.cn/portal") b = DefaultBaseUrl;
        if (!b.EndsWith('/')) b += "/";
        return b;
    }

    public static string BaseUrl
    {
        get => ResolveBaseUrl(Load().BaseUrl);
        set => Update(s => s.BaseUrl = ResolveBaseUrl(value));
    }

    public static WubiCloudUserInfo? UserInfo
    {
        get
        {
            var u = Load().User;
            return u != null && u.IsLoggedIn ? u : null;
        }
        set => Update(s => s.User = value);
    }

    public static bool IsLoggedIn => UserInfo?.IsLoggedIn == true;

    public static DateTimeOffset? LastSyncAt
    {
        get
        {
            var t = Load().LastSyncAt;
            return t > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t * 1000)) : null;
        }
        set => Update(s => s.LastSyncAt = value.HasValue ? value.Value.ToUnixTimeMilliseconds() / 1000.0 : 0);
    }

    public static DateTimeOffset? LastExtraPullAt
    {
        get
        {
            var t = Load().LastExtraPullAt;
            return t > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t * 1000)) : null;
        }
        set => Update(s => s.LastExtraPullAt = value.HasValue ? value.Value.ToUnixTimeMilliseconds() / 1000.0 : 0);
    }

    // 响应头续签 JWT 写回本地
    public static void UpdateTokenIfNeeded(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        Update(s =>
        {
            if (s.User == null || s.User.Token == token) return;
            s.User = s.User with { Token = token };
        });
    }

    public static void Logout() => Update(s => s.User = null);

    private static string FilePath => Path.Combine(AppPaths.ContainerDir, "cloud.json");

    private static void Update(Action<Store> body)
    {
        lock (Lock)
        {
            var store = ReadUnlocked();
            body(store);
            try
            {
                Directory.CreateDirectory(AppPaths.ContainerDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(store));
            }
            catch
            {
                // 写不进去时保持旧状态
            }
        }
        Changed?.Invoke();
    }

    private static Store Load()
    {
        lock (Lock) return ReadUnlocked();
    }

    private static Store ReadUnlocked()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
        }
        catch
        {
            // 文件损坏时当作未登录
        }
        return new Store();
    }
}
