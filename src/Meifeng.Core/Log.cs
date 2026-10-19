using Meifeng.Core.Config;

namespace Meifeng.Core;

// 简单的追加日志：%APPDATA%\MeifengWubi\logs\meifeng.log，超过 1MB 时截断
public static class Log
{
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.Combine(AppPaths.ContainerDir, "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "meifeng.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
            }
        }
        catch
        {
            // 日志失败不影响输入
        }
    }
}
