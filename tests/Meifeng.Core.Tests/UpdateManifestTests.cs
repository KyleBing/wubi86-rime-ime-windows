using Meifeng.Core.Update;

namespace Meifeng.Core.Tests;

// 更新清单解析与版本比较：不联网，纯逻辑
public class UpdateManifestTests
{
    private const string Sample = """
        {
          "schema": 1,
          "platform": "windows",
          "channel": "stable",
          "version": "1.0.3",
          "published_at": "2026-10-30",
          "download_url": "https://example.com/setup.exe",
          "fallback_url": "https://mirror.example.com/setup.exe",
          "sha256": "ABCDEF",
          "size": 123,
          "minimum_version": "1.0.0",
          "notes": "修了一些问题"
        }
        """;

    [Fact]
    public void Parse_ReadsSnakeCaseFields()
    {
        var manifest = UpdateManifest.Parse(Sample);

        Assert.NotNull(manifest);
        Assert.Equal(1, manifest!.Schema);
        Assert.Equal("windows", manifest.Platform);
        Assert.Equal("1.0.3", manifest.Version);
        Assert.Equal("https://example.com/setup.exe", manifest.DownloadUrl);
        Assert.Equal("https://mirror.example.com/setup.exe", manifest.FallbackUrl);
        Assert.Equal("ABCDEF", manifest.Sha256);
        Assert.Equal(123, manifest.Size);
        Assert.Equal("1.0.0", manifest.MinimumVersion);
        Assert.Equal("修了一些问题", manifest.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"\"}")]
    public void Parse_RejectsInvalidManifest(string json)
    {
        Assert.Null(UpdateManifest.Parse(json));
    }

    [Theory]
    [InlineData("1.0.2", "1.0.2.0", false)]
    [InlineData("v1.0.2", "1.0.3", true)]
    [InlineData("1.0.2", "1.0.2", false)]
    [InlineData("1.2.0", "1.1.9", false)]
    [InlineData("1.0.2-beta1", "1.0.2", false)]
    [InlineData("1.10", "1.9.9", false)]
    public void IsNewer_ComparesNumericParts(string current, string latest, bool expected)
    {
        Assert.Equal(expected, UpdateManifest.IsNewer(current, latest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.0.2.3.4")]
    public void IsNewer_ReturnsFalseForBadVersion(string? current)
    {
        Assert.False(UpdateManifest.IsNewer(current, "1.0.3"));
    }

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("0.9.9", true)]
    [InlineData("1.0.3", false)]
    public void IsMandatoryFor_UsesMinimumVersion(string current, bool expected)
    {
        var manifest = UpdateManifest.Parse(Sample);
        Assert.Equal(expected, manifest!.IsMandatoryFor(current));
    }
}
