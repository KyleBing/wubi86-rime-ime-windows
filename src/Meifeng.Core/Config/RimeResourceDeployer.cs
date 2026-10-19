using System.Text.RegularExpressions;

namespace Meifeng.Core.Config;

// 把内置方案同步到可写目录
public static class RimeResourceDeployer
{
    public const string SchemaId = "wubi86_jidian";
    public const string SchemaFileName = "wubi86_jidian.schema.yaml";
    private const string ResourceVersionKey = "rime.resource.version";

    // 升版本时补齐用户目录里还没有的内置文件，已有文件不覆盖
    public const string BundledResourceVersion = "6";

    // 内置共享数据（default.yaml、按键预设、OpenCC）。只读，编译结果写到用户目录的 build\
    public static string? ResolveSharedDataDir()
    {
        var dir = AppPaths.BundledSharedSupportDir;
        return File.Exists(Path.Combine(dir, "default.yaml")) ? dir : null;
    }

    // 词库 import_tables 指向的文件不在时，补一份空码表。缺文件会让整本主词库编译失败
    public static void EnsureMissingImportedDicts(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var item in Directory.EnumerateFiles(root, "*.dict.yaml"))
        {
            string text;
            try { text = File.ReadAllText(item); } catch { continue; }
            foreach (var name in ImportedTableNames(text))
            {
                var dest = Path.Combine(root, $"{name}.dict.yaml");
                if (File.Exists(dest)) continue;
                var bundled = Path.Combine(AppPaths.BundledRimeDir, $"{name}.dict.yaml");
                try
                {
                    if (File.Exists(bundled))
                    {
                        File.Copy(bundled, dest);
                        continue;
                    }
                    var stub = $"# 引用的词库文件不存在，部署时补的空表，避免主码表编译失败\n---\nname: {name}\nversion: \"0\"\nsort: original\ncolumns:\n  - text\n  - code\n  - weight\n...";
                    File.WriteAllText(dest, stub);
                }
                catch
                {
                    // 补不上就交给部署报错
                }
            }
        }
    }

    private static readonly Regex ImportHeader = new(@"^import_tables[ \t]*:");
    private static readonly Regex NextKey = new(@"^[A-Za-z0-9_]+[ \t]*:");
    private static readonly Regex ImportItem = new(@"^[ \t]+-[ \t]+([A-Za-z0-9_.-]+)");

    // 只读 import_tables 段里的词库名，遇到下一个顶层字段就结束
    public static List<string> ImportedTableNames(string text)
    {
        var names = new List<string>();
        var inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!inSection)
            {
                if (ImportHeader.IsMatch(line)) inSection = true;
                continue;
            }
            if (NextKey.IsMatch(line)) break;
            var m = ImportItem.Match(line);
            if (m.Success) names.Add(m.Groups[1].Value);
        }
        return names;
    }

    // 目录根上是否已有任意 Rime 方案。有的话就直接用，不再补内置文件
    public static bool ContainsSchemaFile(string root)
    {
        try
        {
            return Directory.Exists(root) && Directory.EnumerateFiles(root, "*.schema.yaml").Any();
        }
        catch
        {
            return false;
        }
    }

    // 可写用户目录；自定义目录不可用（如移动盘未插）时退回默认目录
    public static string ResolveUserDataDir()
    {
        if (AppPaths.EnsureDirectories()) return AppPaths.RimeUserDataDir;
        try
        {
            Directory.CreateDirectory(AppPaths.DefaultRimeUserDataDir);
            return AppPaths.DefaultRimeUserDataDir;
        }
        catch
        {
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "rime");
            try { Directory.CreateDirectory(docs); } catch { }
            return docs;
        }
    }

    // 空目录才补内置五笔。已有方案的目录原样使用，共享目录就是该目录本身
    public static (string Shared, string User)? SyncBundledResourcesIfNeeded()
    {
        var user = ResolveUserDataDir();
        if (ContainsSchemaFile(user)) return (user, user);
        CopyMissingBundledFiles(user);
        var marker = Path.Combine(user, ".resource_version");
        string? markerValue = null;
        try { if (File.Exists(marker)) markerValue = File.ReadAllText(marker); } catch { }
        markerValue ??= PrefsStore.Shared.GetString(ResourceVersionKey);
        if (markerValue == BundledResourceVersion)
        {
            // 仍校验关键文件，坏配置时强制再同步
            if (File.Exists(Path.Combine(user, SchemaFileName))
                && File.Exists(Path.Combine(user, "wubi86_jidian.dict.yaml")))
            {
                return (user, user);
            }
        }
        try
        {
            ForceSyncBundledResources(overwriteUserDict: false);
        }
        catch
        {
            return null;
        }
        return (user, user);
    }

    // 强制把内置方案拷到用户目录；overwriteUserDict 为 true 时连用户词库一并覆盖（复位用）
    public static (string Shared, string User) ForceSyncBundledResources(bool overwriteUserDict)
    {
        var bundled = AppPaths.BundledRimeDir;
        if (!Directory.Exists(bundled)) throw new InvalidOperationException("找不到内置方案资源 Resources\\Rime");
        var user = ResolveUserDataDir();
        Directory.CreateDirectory(user);
        var files = Directory.GetFiles(bundled);
        if (files.Length == 0) throw new InvalidOperationException("内置 Rime 目录为空");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            // 用户目录已有自己的方案时，不补 default.custom.yaml，避免换掉原方案列表
            if (name == "default.custom.yaml" && !overwriteUserDict && UserAlreadyHasOwnSchema(user)) continue;
            var dest = Path.Combine(user, name);
            // 常规同步只补缺失文件，不改已有内容；复位才覆盖
            if (!overwriteUserDict && File.Exists(dest)) continue;
            File.Copy(file, dest, overwrite: true);
        }
        var needsCustom = !UserAlreadyHasOwnSchema(user);
        if (!File.Exists(Path.Combine(user, SchemaFileName))
            || !File.Exists(Path.Combine(user, "wubi86_jidian.dict.yaml"))
            || (needsCustom && !File.Exists(Path.Combine(user, "default.custom.yaml"))))
        {
            throw new InvalidOperationException("内置方案拷贝不完整，缺少 schema/dict/default.custom");
        }
        File.WriteAllText(Path.Combine(user, ".resource_version"), BundledResourceVersion);
        PrefsStore.Shared.Set(ResourceVersionKey, BundledResourceVersion);
        return (user, user);
    }

    // 把内置五笔方案里用户目录还没有的文件补进去，已有文件不动
    public static void CopyMissingBundledFiles(string destRoot)
    {
        if (!Directory.Exists(AppPaths.BundledRimeDir)) return;
        CopyMissing(AppPaths.BundledRimeDir, destRoot);
    }

    private static void CopyMissing(string source, string dest)
    {
        try { Directory.CreateDirectory(dest); } catch { return; }
        foreach (var item in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(item);
            if (name.StartsWith('.')) continue;
            var target = Path.Combine(dest, name);
            var isDir = Directory.Exists(item);
            if (name == "default.custom.yaml" && UserAlreadyHasOwnSchema(dest)) continue;
            if (File.Exists(target) || Directory.Exists(target))
            {
                // 已有文件保持原样，只往还没有的子目录里补文件
                if (isDir) CopyMissing(item, target);
                continue;
            }
            try
            {
                if (isDir) CopyDirectory(item, target);
                else File.Copy(item, target);
            }
            catch
            {
                // 单个文件失败不影响其它文件
            }
        }
    }

    // 递归复制目录
    public static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(source)) CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    // 方案文件已在，且内容和内置副本不同，说明这是用户自己的 Rime 目录
    private static bool UserAlreadyHasOwnSchema(string root)
    {
        var dest = Path.Combine(root, SchemaFileName);
        var bundled = Path.Combine(AppPaths.BundledRimeDir, SchemaFileName);
        try
        {
            if (!File.Exists(dest) || !File.Exists(bundled)) return false;
            static string Norm(string s) => s.Replace("\r\n", "\n").Trim();
            return Norm(File.ReadAllText(dest)) != Norm(File.ReadAllText(bundled));
        }
        catch
        {
            return false;
        }
    }
}

// 读取用户 Rime 目录里已有的方案列表，不改目录中的文件
public static class RimeExistingLexicon
{
    public static readonly string[] RequiredSchemaIds = ["wubi86_jidian", "pinyin_simp"];

    // schema_list 里的方案，编译后 build\ 下应有 .schema.yaml 或 .table.bin
    public static List<string> SchemasMissingBuild(string root)
    {
        var ids = SchemaIds(ReadOrEmpty(Path.Combine(root, "default.custom.yaml")));
        if (ids.Count == 0) ids = SchemaIds(ReadOrEmpty(Path.Combine(root, "default.yaml")));
        if (ids.Count == 0) ids = RequiredSchemaIds.ToList();
        var build = Path.Combine(root, "build");
        return ids.Where(id => !File.Exists(Path.Combine(build, $"{id}.schema.yaml"))
                               && !File.Exists(Path.Combine(build, $"{id}.table.bin"))).ToList();
    }

    private static readonly Regex SchemaIdRegex = new(@"(?m)^[ \t]*-[ \t]+schema:[ \t]*[""']?([A-Za-z0-9_.-]+)[""']?");

    // 从 yaml 里按出现顺序收集 `schema:` id
    public static List<string> SchemaIds(string text)
    {
        var ids = new List<string>();
        foreach (Match m in SchemaIdRegex.Matches(text))
        {
            var id = m.Groups[1].Value;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    private static string ReadOrEmpty(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; } catch { return ""; }
    }
}
