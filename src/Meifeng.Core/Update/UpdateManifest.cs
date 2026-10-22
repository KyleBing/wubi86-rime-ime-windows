using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meifeng.Core.Update;

// 更新清单 update.json：发布工作流在打标签时生成，作为 Release 附件上传。
// 客户端只读这一个文件，不调用 GitHub API（未登录时 60 次/小时/IP，且国内访问不稳）。
public sealed class UpdateManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("channel")] public string Channel { get; set; } = "stable";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    // macOS 用构建号区分同一营销版本；Windows 留 0
    [JsonPropertyName("build")] public int Build { get; set; }
    [JsonPropertyName("published_at")] public string? PublishedAt { get; set; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("fallback_url")] public string? FallbackUrl { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("min_system")] public string? MinSystem { get; set; }
    // 低于这个版本时提示必须更新，普通的新版本仍由用户决定
    [JsonPropertyName("minimum_version")] public string? MinimumVersion { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("notes_url")] public string? NotesUrl { get; set; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // 解析失败或没有版本号时返回 null，调用方按「检查失败」处理
    public static UpdateManifest? Parse(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Options);
            return string.IsNullOrWhiteSpace(manifest?.Version) ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 允许 "v1.0.2"、"1.0.2-beta1" 这类写法；统一补成四段，避免 1.0.2 与 1.0.2.0 比较出错
    public static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().TrimStart('v', 'V');
        // 只取开头的数字与点，忽略 -beta、+build 之类后缀
        var end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
        text = text[..end].TrimEnd('.');
        if (text.Length == 0) return null;

        var parts = text.Split('.');
        if (parts.Length > 4) return null;
        var nums = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return null;
        }
        return new Version(nums[0], nums[1], nums[2], nums[3]);
    }

    // 清单版本是否比当前版本新；任一版本号非法时返回 false
    public static bool IsNewer(string? current, string? latest)
    {
        var a = ParseVersion(current);
        var b = ParseVersion(latest);
        return a != null && b != null && b > a;
    }

    // 当前版本低于清单里的最低版本时，界面应提示必须更新
    public bool IsMandatoryFor(string? current)
    {
        var minimum = ParseVersion(MinimumVersion);
        var now = ParseVersion(current);
        return minimum != null && now != null && now < minimum;
    }
}
