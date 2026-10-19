using Meifeng.Core.Config;

namespace MeifengWubi;

// 词库部署 / 复位 / 换目录。都在后台线程执行，返回 (成功, 提示文字)
internal static class LexiconActions
{
    public static Task<(bool Ok, string Message)> DeployAsync() => Task.Run(() =>
    {
        var result = RimeConfigDeploy.DeployNow();
        if (!result.Success) return (false, result.Error!);
        return result.EngineRestartFailed ? (false, "已部署，但输入法重载失败") : (true, "部署成功");
    });

    public static Task<(bool Ok, string Message)> ResetAsync() => Task.Run(() =>
    {
        var result = RimeConfigDeploy.ResetToFactory();
        if (!result.Success) return (false, result.Error!);
        return result.EngineRestartFailed ? (false, "已复位，但输入法重载失败") : (true, "已复位并部署");
    });

    // dir 为 null 表示切回默认目录
    public static Task<(bool Ok, string Message)> ChangeDirectoryAsync(string? dir) => Task.Run(() =>
    {
        var result = RimeConfigDeploy.ChangeUserDataDirectory(dir);
        if (!result.Success) return (false, result.Error!);
        var message = dir == null ? "已恢复默认词库目录并部署" : "已切换词库目录并部署";
        return result.EngineRestartFailed ? (false, message + "，但输入法重载失败") : (true, message);
    });
}
