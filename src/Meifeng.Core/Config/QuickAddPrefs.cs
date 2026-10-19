using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meifeng.Core.Config;

// Win32 RegisterHotKey 的修饰键位
[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8
}

// 全局造词快捷键与用户词当前分组，存 %APPDATA%\MeifengWubi\quickadd.json
public static class QuickAddPrefs
{
    // 默认 Ctrl+Alt+N（VK_N = 0x4E）
    public const uint DefaultKeyCode = 0x4E;
    public const HotKeyModifiers DefaultModifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt;

    private static readonly object Lock = new();

    // 快捷键变化后触发，重新注册全局热键
    public static event Action? Changed;

    private sealed class Store
    {
        [JsonPropertyName("keyCode")] public uint KeyCode { get; set; } = DefaultKeyCode;
        [JsonPropertyName("modifiers")] public uint Modifiers { get; set; } = (uint)DefaultModifiers;
        [JsonPropertyName("display")] public string Display { get; set; } = "Ctrl+Alt+N";
        [JsonPropertyName("selectedGroupIndex")] public int SelectedGroupIndex { get; set; }
    }

    public static uint KeyCode => Load().KeyCode;

    public static HotKeyModifiers Modifiers => (HotKeyModifiers)Load().Modifiers;

    public static string Display => Load().Display;

    public static int SelectedGroupIndex
    {
        get => Load().SelectedGroupIndex;
        set => Update(s => s.SelectedGroupIndex = Math.Max(0, value));
    }

    // 至少带一个修饰键，避免把单字母注册成全局热键
    public static void SaveShortcut(uint keyCode, HotKeyModifiers modifiers, string display)
    {
        if (modifiers == HotKeyModifiers.None) return;
        Update(s =>
        {
            s.KeyCode = keyCode;
            s.Modifiers = (uint)modifiers;
            s.Display = display;
        });
        Changed?.Invoke();
    }

    // 生成「Ctrl+Alt+N」形式的显示文字
    public static string DisplayString(HotKeyModifiers modifiers, string keyName)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");
        parts.Add(keyName.ToUpperInvariant());
        return string.Join("+", parts);
    }

    private static string FilePath => Path.Combine(AppPaths.ContainerDir, "quickadd.json");

    private static void Update(Action<Store> body)
    {
        lock (Lock)
        {
            var s = ReadUnlocked();
            body(s);
            try
            {
                Directory.CreateDirectory(AppPaths.ContainerDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
            }
            catch
            {
                // 写不进去时沿用内存值
            }
        }
    }

    private static Store Load()
    {
        lock (Lock) return ReadUnlocked();
    }

    private static Store ReadUnlocked()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
        }
        catch
        {
            // 损坏时回到默认
        }
        return new Store();
    }
}
