using Meifeng.Core.Engine;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Config;

// 部署结果：成功为 null，失败为原因
public sealed record DeployResult(string? Error)
{
    public bool Success => Error == null;

    // 部署成功但引擎没能重新拉起
    public bool EngineRestartFailed { get; init; }

    public static DeployResult Ok => new((string?)null);

    public static DeployResult Fail(string message) => new(message);
}

// 按当前配置目录部署 Rime。服务进程与设置在同一进程，部署前先停掉会话，结束后重建
public static class RimeConfigDeploy
{
    // 全量部署（菜单「部署」、设置页「部署当前配置」）
    public static DeployResult DeployNow()
    {
        var result = RimeEngine.Shared.PerformExternalDeploy(DeployUnlocked, out var restarted);
        return result.Success && !restarted ? result with { EngineRestartFailed = true } : result;
    }

    // 实际部署流程；调用前会话必须已停止
    private static DeployResult DeployUnlocked()
    {
        var user = RimeResourceDeployer.ResolveUserDataDir();
        // 拷整个文件夹时常多一层目录，先尝试提升到根
        RimeFileLayout.FlattenNestedSchemeIfNeeded(user);
        // 缺方案时补内置文件；已有主码表不覆盖
        RimeResourceDeployer.SyncBundledResourcesIfNeeded();
        if (RimeFileLayout.ValidateSchemeRoot(user) is { } problem) return DeployResult.Fail(problem);
        // 缺的 import_tables 先补空表，否则主词库整本编不出来
        RimeResourceDeployer.EnsureMissingImportedDicts(user);
        try
        {
            // 共享目录用内置预设（default.yaml 等）；方案和词库仍在用户目录
            var shared = RimeResourceDeployer.ResolveSharedDataDir() ?? user;
            RimeEngine.MaintenanceDeploy(shared, user);
            var build = Path.Combine(user, "build");
            var compiled = Directory.Exists(build) ? Directory.GetFiles(build).Select(Path.GetFileName).ToList() : [];
            if (!compiled.Any(n => n!.EndsWith(".schema.yaml", StringComparison.Ordinal)))
            {
                return DeployResult.Fail("部署完成但没有编译出方案。方案依赖预设 default.yaml，请确认安装目录里带有 Resources\\SharedSupport。");
            }
            var hasDictSource = Directory.EnumerateFiles(user, "*.dict.yaml").Any();
            var hasTable = compiled.Any(n => n!.EndsWith(".table.bin", StringComparison.Ordinal));
            if (hasDictSource && !hasTable)
            {
                return DeployResult.Fail("方案配置已编译，但词库没有生成。请检查 .dict.yaml 的 import_tables 是否都能找到对应文件。");
            }
            DictLexiconIndex.Shared.Invalidate();
            WubiDictEncoder.ClearCache();
            return DeployResult.Ok;
        }
        catch (Exception e)
        {
            return DeployResult.Fail(e.Message);
        }
    }

    // 改选 Rime 目录：把当前目录里新目录没有的文件拷过去（不覆盖已有），再切换并部署。newDir 为 null 表示恢复默认目录
    public static DeployResult ChangeUserDataDirectory(string? newDir)
    {
        var oldDir = Path.GetFullPath(RimeResourceDeployer.ResolveUserDataDir());
        var target = Path.GetFullPath(newDir ?? AppPaths.DefaultRimeUserDataDir);
        if (AppPaths.SamePath(target, oldDir)) return DeployResult.Ok;
        // 选到当前目录的子目录会把自己拷进自己
        var prefix = oldDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return DeployResult.Fail("不能选择当前词库目录内的子目录");
        }
        try
        {
            Directory.CreateDirectory(target);
            if (!IsWritable(target)) return DeployResult.Fail($"目录不可写：{target}");
            // 目标目录已有方案时保持原样。空目录才从当前目录补缺少的文件
            if (!RimeResourceDeployer.ContainsSchemaFile(target) && Directory.Exists(oldDir))
            {
                foreach (var item in Directory.EnumerateFileSystemEntries(oldDir))
                {
                    var name = Path.GetFileName(item);
                    if (name == "build") continue;
                    var dest = Path.Combine(target, name);
                    if (File.Exists(dest) || Directory.Exists(dest)) continue;
                    if (Directory.Exists(item)) RimeResourceDeployer.CopyDirectory(item, dest);
                    else File.Copy(item, dest);
                }
            }
            AppPaths.SetCustomRimeUserDataDir(newDir);
        }
        catch (Exception e)
        {
            return DeployResult.Fail(e.Message);
        }
        RimeResourceDeployer.SyncBundledResourcesIfNeeded();
        DictLexiconIndex.Shared.Invalidate();
        WubiDictEncoder.ClearCache();
        return DeployNow();
    }

    // 清空目录 → 恢复内置全部文件 → 立即部署（真正复位）
    public static DeployResult ResetToFactory()
    {
        var result = RimeEngine.Shared.PerformExternalDeploy(() =>
        {
            try
            {
                ClearAll();
                RimeResourceDeployer.ForceSyncBundledResources(overwriteUserDict: true);
                // 复位后按当前行为偏好重写 custom.yaml
                if (BehaviorPrefs.Persist() is { } err) return DeployResult.Fail(err);
                return DeployUnlocked();
            }
            catch (Exception e)
            {
                return DeployResult.Fail(e.Message);
            }
        }, out var restarted);
        return result.Success && !restarted ? result with { EngineRestartFailed = true } : result;
    }

    // 清空用户 Rime 目录（复位用）
    private static int ClearAll()
    {
        var root = RimeResourceDeployer.ResolveUserDataDir();
        if (!Directory.Exists(root)) return 0;
        var count = 0;
        foreach (var item in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (Directory.Exists(item)) Directory.Delete(item, true);
            else File.Delete(item);
            count++;
        }
        return count;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".meifeng_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// 配置目录布局检查 / 修正（拷整夹时常见多一层）
public static class RimeFileLayout
{
    // 根目录缺少 schema，但唯一子目录里有 → 把该子目录内容提升到根
    public static bool FlattenNestedSchemeIfNeeded(string root)
    {
        if (File.Exists(Path.Combine(root, RimeResourceDeployer.SchemaFileName))) return false;
        if (!Directory.Exists(root)) return false;
        var candidates = Directory.GetDirectories(root)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Where(d => File.Exists(Path.Combine(d, RimeResourceDeployer.SchemaFileName)))
            .ToList();
        // 仅当恰好一个子目录含方案时自动提升，避免误伤
        if (candidates.Count != 1) return false;
        var src = candidates[0];
        foreach (var child in Directory.EnumerateFileSystemEntries(src).ToList())
        {
            var dest = Path.Combine(root, Path.GetFileName(child));
            try
            {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                else if (File.Exists(dest)) File.Delete(dest);
                if (Directory.Exists(child)) Directory.Move(child, dest);
                else File.Move(child, dest);
            }
            catch
            {
                // 单个文件移动失败时继续
            }
        }
        try { Directory.Delete(src, true); } catch { }
        return true;
    }

    // 部署前检查：根目录里要有任意一份方案
    public static string? ValidateSchemeRoot(string root)
    {
        if (RimeResourceDeployer.ContainsSchemaFile(root)) return null;
        if (Directory.Exists(root))
        {
            foreach (var d in Directory.GetDirectories(root))
            {
                if (Path.GetFileName(d).StartsWith('.')) continue;
                if (RimeResourceDeployer.ContainsSchemaFile(d)) return $"方案在子目录「{Path.GetFileName(d)}」内，请改选该目录本身。";
            }
        }
        return "目录里没有 Rime 方案（.schema.yaml），无法部署。";
    }
}
