namespace Meifeng.Core.Config;

// 所有目录集中在这里：%APPDATA%\MeifengWubi 为配置容器，Rime 目录默认在其下的 rime
public static class AppPaths
{
    // 测试时可替换容器目录
    public static string? OverrideContainerDir { get; set; }

    // 程序资源根目录（安装目录下的 Resources）
    public static string? OverrideResourcesDir { get; set; }

    public static string ContainerDir =>
        OverrideContainerDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeifengWubi");

    // 默认 Rime 目录
    public static string DefaultRimeUserDataDir => Path.Combine(ContainerDir, "rime");

    // 小狼毫等 Rime 前端的用户目录：%APPDATA%\Rime
    public static string WeaselRimeUserDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");

    // 实际使用的 Rime 目录：用户改选过则用自定义目录，否则为默认目录
    public static string RimeUserDataDir => CustomRimeUserDataDir ?? DefaultRimeUserDataDir;

    private static string CustomDirRecordPath => Path.Combine(ContainerDir, "rime_user_dir.txt");

    // 用户改选的 Rime 目录；未设置或记录无效时为 null
    public static string? CustomRimeUserDataDir
    {
        get
        {
            try
            {
                if (!File.Exists(CustomDirRecordPath)) return null;
                var path = File.ReadAllText(CustomDirRecordPath).Trim();
                return path.Length == 0 ? null : path;
            }
            catch
            {
                return null;
            }
        }
    }

    // 写入自定义目录；传 null 恢复默认目录
    public static void SetCustomRimeUserDataDir(string? dir)
    {
        Directory.CreateDirectory(ContainerDir);
        if (dir != null && !SamePath(dir, DefaultRimeUserDataDir))
        {
            File.WriteAllText(CustomDirRecordPath, Path.GetFullPath(dir));
        }
        else if (File.Exists(CustomDirRecordPath))
        {
            File.Delete(CustomDirRecordPath);
        }
    }

    public static string UserDictPath => Path.Combine(RimeUserDataDir, "wubi86_jidian_user.dict.yaml");

    // 内置资源（安装目录 Resources\Rime、Resources\SharedSupport）
    public static string ResourcesDir =>
        OverrideResourcesDir ?? Path.Combine(AppContext.BaseDirectory, "Resources");

    public static string BundledRimeDir => Path.Combine(ResourcesDir, "Rime");

    public static string BundledSharedSupportDir => Path.Combine(ResourcesDir, "SharedSupport");

    public static bool EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(RimeUserDataDir);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 比较两个目录是否为同一路径（Windows 忽略大小写）
    public static bool SamePath(string a, string b)
    {
        static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Norm(a), Norm(b), cmp);
    }
}
