using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meifeng.Core.Config;

// 候选排序快捷键，默认与造词同一组修饰键加 0（Ctrl+Alt+0），存 %APPDATA%\MeifengWubi\rankshortcut.json
public static class RankPrefs
{
    // 默认 Ctrl+Alt+0（VK_0 = 0x30）
    public const uint DefaultKeyCode = 0x30;
    public const HotKeyModifiers DefaultModifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt;

    private static readonly object Lock = new();

    // 快捷键变化后触发，排序设置界面据此刷新显示
    public static event Action? Changed;

    private sealed class Store
    {
        [JsonPropertyName("keyCode")] public uint KeyCode { get; set; } = DefaultKeyCode;
        [JsonPropertyName("modifiers")] public uint Modifiers { get; set; } = (uint)DefaultModifiers;
        [JsonPropertyName("display")] public string Display { get; set; } = "Ctrl+Alt+0";
    }

    public static uint KeyCode => Load().KeyCode;

    public static HotKeyModifiers Modifiers => (HotKeyModifiers)Load().Modifiers;

    public static string Display => Load().Display;

    // 至少带一个修饰键，避免把单个数字键吃掉
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

    // 按键的虚拟键与修饰键是否就是排序快捷键；数字键盘的 0 也认
    public static bool Matches(uint vk, HotKeyModifiers modifiers)
    {
        var s = Load();
        if (modifiers == HotKeyModifiers.None || (HotKeyModifiers)s.Modifiers != modifiers) return false;
        if (vk == s.KeyCode) return true;
        return s.KeyCode == 0x30 && vk == 0x60; // VK_NUMPAD0
    }

    private static string FilePath => Path.Combine(AppPaths.ContainerDir, "rankshortcut.json");

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
