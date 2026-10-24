using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meifeng.Core.Sync;

// 云端接口错误，Message 即给用户看的文字
public class WubiCloudException(string message) : Exception(message);

// 云端版本已被其它设备改过
public sealed class RevisionConflictException(int revision) : WubiCloudException("版本已变化")
{
    public int Revision { get; } = revision;
}

// 云端词库 API（与 iOS / macOS / 码表助手一致：Bearer JWT）
public static class WubiCloudApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly object MaxCharsCacheLock = new();
    private static (int Value, DateTimeOffset At)? MaxCharsCache;
    private static readonly TimeSpan MaxCharsCacheTtl = TimeSpan.FromMinutes(5);

    public sealed record DictMeta(int Revision, string? ContentHash, int? WordCount, string? DateUpdate);

    public sealed record PullResult(string Title, string Yaml, int Revision);

    // 扩展词库词条
    public sealed record ExtraWordItem(int? Id, string Code, string Word, int Weight, string? Comment, int CategoryId, string CategoryName);

    // 公开配置中的同步字符上限；失败回退本地默认
    public static async Task<int> FetchDictSyncMaxCharsAsync(string? baseUrl = null)
    {
        lock (MaxCharsCacheLock)
        {
            if (MaxCharsCache is { } cached && DateTimeOffset.Now - cached.At < MaxCharsCacheTtl)
            {
                return cached.Value;
            }
        }
        var fallback = WubiCloudConfig.SyncMaxContentLength;
        try
        {
            var root = baseUrl ?? WubiCloudConfig.BaseUrl;
            var data = await SendAsync(HttpMethod.Get, MakeUrl(root, "system-config"), null, null).ConfigureAwait(false);
            var max = fallback;
            if (Success(data) && data["data"] is JsonObject cfg)
            {
                var n = Int(cfg["dict_sync_max_chars"]);
                if (n > 0) max = n;
            }
            lock (MaxCharsCacheLock)
            {
                MaxCharsCache = (max, DateTimeOffset.Now);
            }
            return max;
        }
        catch
        {
            return fallback;
        }
    }

    // POST user/login，成功后写入本地账号
    public static async Task<WubiCloudUserInfo> LoginAsync(string email, string password, string? baseUrl = null)
    {
        var root = baseUrl ?? WubiCloudConfig.BaseUrl;
        var body = new JsonObject { ["email"] = email, ["password"] = password };
        var data = await SendAsync(HttpMethod.Post, MakeUrl(root, "user/login"), body, null);
        if (!Success(data) || data["data"] is not JsonObject login || Str(login["token"]) is not { Length: > 0 } token)
        {
            throw new WubiCloudException(Str(data["message"]) ?? "登录失败");
        }
        var info = new WubiCloudUserInfo(Int(login["uid"]), Str(login["email"]) ?? email, Str(login["nickname"]) ?? "", token);
        WubiCloudConfig.BaseUrl = root;
        WubiCloudConfig.UserInfo = info;
        return info;
    }

    // GET wubi/dict/meta。没有备份时返回 null
    public static async Task<DictMeta?> FetchMetaAsync(string title = WubiCloudConfig.SyncDictFileName)
    {
        var user = RequireUser();
        var url = MakeUrl(WubiCloudConfig.BaseUrl, "dict/meta") + "?title=" + Uri.EscapeDataString(title);
        var data = await SendAsync(HttpMethod.Get, url, null, user);
        if (!Success(data))
        {
            var msg = Str(data["message"]) ?? "";
            if (msg.Contains("不存在") || msg.Contains('未')) return null;
            throw new WubiCloudException(msg.Length == 0 ? "读取版本失败" : msg);
        }
        if (data["data"] is not JsonObject meta) return null;
        var revision = Int(meta["revision"]);
        if (revision <= 0) return null;
        return new DictMeta(revision, Str(meta["content_hash"]), IntOrNull(meta["word_count"]), Str(meta["date_update"]));
    }

    // GET wubi/dict/pull；云端无备份时返回 null
    public static async Task<PullResult?> PullDictAsync(string title = WubiCloudConfig.SyncDictFileName)
    {
        var user = RequireUser();
        var url = MakeUrl(WubiCloudConfig.BaseUrl, "dict/pull") + "?title=" + Uri.EscapeDataString(title);
        var data = await SendAsync(HttpMethod.Get, url, null, user);
        if (!Success(data))
        {
            var msg = Str(data["message"]) ?? "";
            if (msg.Contains("不存在") || msg.Contains('未') || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)) return null;
            throw new WubiCloudException(msg.Length == 0 ? "拉取失败" : msg);
        }
        if (data["data"] is not JsonObject payload || Str(payload["content"]) is not { Length: > 0 } yaml) return null;
        return new PullResult(Str(payload["title"]) ?? title, yaml, Int(payload["revision"]));
    }

    // PUT wubi/dict/push。baseRevision 为空时整份覆盖。返回写入后的版本
    public static async Task<int> PushDictAsync(string yaml, int wordCount, int? baseRevision = null, string title = WubiCloudConfig.SyncDictFileName)
    {
        var user = RequireUser();
        var length = new StringInfo(yaml).LengthInTextElements;
        var maxChars = await FetchDictSyncMaxCharsAsync().ConfigureAwait(false);
        if (length >= maxChars)
        {
            throw new WubiCloudException($"同步内容超过 {maxChars} 字");
        }
        var body = new JsonObject
        {
            ["title"] = title,
            ["content"] = yaml,
            ["contentSize"] = length,
            ["wordCount"] = wordCount
        };
        if (baseRevision.HasValue) body["baseRevision"] = baseRevision.Value;
        var data = await SendAsync(HttpMethod.Put, MakeUrl(WubiCloudConfig.BaseUrl, "dict/push"), body, user);
        var payload = data["data"] as JsonObject;
        if (payload?["conflict"] is JsonValue c && c.TryGetValue<bool>(out var conflict) && conflict
            || (Str(data["message"]) ?? "").Contains("版本已变化"))
        {
            throw new RevisionConflictException(Int(payload?["revision"]));
        }
        if (!Success(data)) throw new WubiCloudException(Str(data["message"]) ?? "上传失败");
        WubiCloudConfig.LastSyncAt = DateTimeOffset.Now;
        return Int(payload?["revision"]);
    }

    // 拉取线上扩展词库全部分类词条，只读不回写
    public static async Task<List<ExtraWordItem>> PullExtraDictAsync()
    {
        var user = WubiCloudConfig.UserInfo is { IsLoggedIn: true } u ? u : null;
        var data = await SendAsync(HttpMethod.Post, MakeUrl(WubiCloudConfig.BaseUrl, "word/export-extra"), new JsonObject(), user);
        if (!Success(data) || data["data"] is not JsonArray list)
        {
            throw new WubiCloudException(Str(data["message"]) ?? "拉取扩展词库失败");
        }
        var items = new List<ExtraWordItem>();
        foreach (var node in list)
        {
            if (node is not JsonObject o) continue;
            items.Add(new ExtraWordItem(
                IntOrNull(o["id"]),
                Str(o["code"]) ?? "",
                Str(o["word"]) ?? "",
                FlexibleInt(o["priority"]),
                Str(o["comment"]),
                Int(o["category_id"]),
                Str(o["category_name"]) ?? ""));
        }
        return items;
    }

    private static WubiCloudUserInfo RequireUser() =>
        WubiCloudConfig.UserInfo is { IsLoggedIn: true } u ? u : throw new WubiCloudException("未登录，请先登录");

    private static string MakeUrl(string baseUrl, string path)
    {
        var root = WubiCloudConfig.ResolveBaseUrl(baseUrl);
        var url = root + path.Trim('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new WubiCloudException("请求地址无效");
        return url;
    }

    // 发请求、校验 HTTP 状态、续签 token，返回 JSON 外层
    private static async Task<JsonObject> SendAsync(HttpMethod method, string url, JsonObject? body, WubiCloudUserInfo? user)
    {
        using var request = new HttpRequestMessage(method, url);
        if (user != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw new WubiCloudException("网络请求超时");
        }
        catch (HttpRequestException e)
        {
            throw new WubiCloudException($"网络错误：{e.Message}");
        }
        using (response)
        {
            if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            {
                throw new WubiCloudException($"网络错误（HTTP {(int)response.StatusCode}）");
            }
            if (response.Headers.TryGetValues("X-Access-Token", out var tokens))
            {
                var renewed = tokens.FirstOrDefault();
                if (!string.IsNullOrEmpty(renewed)) WubiCloudConfig.UpdateTokenIfNeeded(renewed);
            }
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) throw new WubiCloudException("服务器无返回");
            try
            {
                return JsonNode.Parse(text) as JsonObject ?? throw new WubiCloudException("响应解析失败");
            }
            catch (JsonException)
            {
                throw new WubiCloudException("响应解析失败");
            }
        }
    }

    private static bool Success(JsonObject o) => o["success"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? n) => IntOrNull(n) ?? 0;

    private static int? IntOrNull(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return null;
    }

    // 权重可能是数字或字符串
    private static int FlexibleInt(JsonNode? n)
    {
        if (IntOrNull(n) is int i) return i;
        return Str(n) is { } s && int.TryParse(s.Trim(), out var p) ? p : 0;
    }
}
