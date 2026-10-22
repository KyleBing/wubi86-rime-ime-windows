using System.Diagnostics;
using System.Security.Cryptography;
using Meifeng.Core.Config;

namespace Meifeng.Core.Update;

// 下载并启动安装包。只负责「拿到可信文件 + 交给安装器」，
// 替换正在使用的 exe / TSF DLL 由安装器（installer/MeifengWubi.iss）处理。
public static class UpdateInstaller
{
    // 下载目录放在配置容器下，卸载程序时随 %APPDATA% 一起清掉
    public static string UpdateRoot => Path.Combine(AppPaths.ContainerDir, "update");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // 只从清单里的 download_url 下载；地址固定指向本仓库 Release 的附件
    public static async Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var url = manifest.DownloadUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("更新清单里没有下载地址");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidOperationException("更新清单里没有版本号");

        Directory.CreateDirectory(UpdateRoot);
        // 文件名带版本号，失败重试时不会和上一版混淆
        var target = Path.Combine(UpdateRoot, $"MeifengWubi-{manifest.Version}-setup.exe");

        try
        {
            await DownloadOneAsync(url, target, progress, ct).ConfigureAwait(false);
            Verify(target, manifest);
            CleanupOldVersions(manifest.Version);
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    // 启动 Inno Setup 的静默安装。/CLOSEAPPLICATIONS 会关掉占用文件的程序，
    // 安装脚本自己还会 taskkill 掉服务进程；调用后本进程应尽快退出。
    public static bool TryLaunchInstaller(string file)
    {
        if (!File.Exists(file)) return false;
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = true, // 需要走 ShellExecute 才能弹出 UAC
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS"
            };
            return Process.Start(psi) != null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadOneAsync(string url, string target, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;

        var part = target + ".part";
        TryDelete(part);
        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Clamp((double)done / total, 0, 1));
            }
        }
        File.Move(part, target, overwrite: true);
        progress?.Report(1);
    }

    // 大小和 SHA-256 都来自 HTTPS 清单；任一不符就丢弃，不让损坏的包进入安装流程
    private static void Verify(string file, UpdateManifest manifest)
    {
        var length = new FileInfo(file).Length;
        if (manifest.Size > 0 && length != manifest.Size)
            throw new InvalidDataException($"安装包大小不符（{length} / {manifest.Size}）");
        if (string.IsNullOrWhiteSpace(manifest.Sha256)) return;

        using var stream = File.OpenRead(file);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包校验失败，请稍后重试");
    }

    // 只保留刚下载的这一版安装包
    private static void CleanupOldVersions(string keepVersion)
    {
        try
        {
            if (!Directory.Exists(UpdateRoot)) return;
            foreach (var file in Directory.EnumerateFiles(UpdateRoot))
            {
                if (Path.GetFileName(file).Contains(keepVersion, StringComparison.Ordinal)) continue;
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                }
            }
        }
        catch
        {
            // 清理失败不影响更新
        }
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }
}
