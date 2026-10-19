using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meifeng.Core.Config;

// 键值偏好存储，对应 macOS 的 UserDefaults suite。整份存为 %APPDATA%\MeifengWubi\prefs.json
// 输入法服务与设置界面在同一进程，内存里一份即可；写入时整体落盘
public sealed class PrefsStore
{
    public static PrefsStore Shared { get; } = new();

    private readonly object _lock = new();
    private JsonObject? _root;

    // 任一键改动后触发，参数为键名
    public event Action<string>? Changed;

    private static string FilePath => Path.Combine(AppPaths.ContainerDir, "prefs.json");

    // 首次访问时从磁盘读取
    private JsonObject Root
    {
        get
        {
            if (_root != null) return _root;
            try
            {
                if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject obj)
                {
                    _root = obj;
                    return _root;
                }
            }
            catch
            {
                // 文件损坏时从空配置开始
            }
            _root = new JsonObject();
            return _root;
        }
    }

    // 重新从磁盘读取（测试或外部修改后用）
    public void Reload()
    {
        lock (_lock) { _root = null; }
    }

    public string? GetString(string key)
    {
        lock (_lock)
        {
            return Root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }
    }

    public int? GetInt(string key)
    {
        lock (_lock)
        {
            if (Root[key] is not JsonValue v) return null;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d)) return (int)d;
            return null;
        }
    }

    public bool? GetBool(string key)
    {
        lock (_lock)
        {
            return Root[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
        }
    }

    public JsonNode? GetNode(string key)
    {
        lock (_lock)
        {
            return Root[key]?.DeepClone();
        }
    }

    public bool Contains(string key)
    {
        lock (_lock) { return Root.ContainsKey(key); }
    }

    public void Set(string key, string? value) => SetNode(key, value == null ? null : JsonValue.Create(value));

    public void Set(string key, int value) => SetNode(key, JsonValue.Create(value));

    public void Set(string key, bool value) => SetNode(key, JsonValue.Create(value));

    // 写入并落盘；value 为 null 时删除该键
    public void SetNode(string key, JsonNode? value)
    {
        lock (_lock)
        {
            if (value == null) Root.Remove(key);
            else Root[key] = value.DeepClone();
            Save();
        }
        Changed?.Invoke(key);
    }

    public void Remove(string key) => SetNode(key, null);

    // 先写临时文件再替换，避免写一半断电损坏
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ContainerDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // 写盘失败时内存值仍有效
        }
    }
}
