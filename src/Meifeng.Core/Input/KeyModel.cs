namespace Meifeng.Core.Input;

// 文本服务送来的修饰键状态
[Flags]
public enum KeyMods
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Win = 8,
    CapsLock = 16
}

// 一次按键：Vk 为 Windows 虚拟键码，Ch 为忽略 Ctrl/Alt 后的字符（保留 Shift 与大写锁定）
public readonly record struct KeyEvent(int Vk, string Ch, KeyMods Mods, bool IsUp)
{
    public bool Has(KeyMods m) => (Mods & m) != 0;

    public char? FirstChar => string.IsNullOrEmpty(Ch) ? null : Ch[0];
}

// 常用虚拟键码
public static class Vk
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12;
    public const int Capital = 0x14, Escape = 0x1B, Space = 0x20, Prior = 0x21, Next = 0x22, End = 0x23, Home = 0x24;
    public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Delete = 0x2E;
    public const int D0 = 0x30, D1 = 0x31, D9 = 0x39;
    public const int LWin = 0x5B, RWin = 0x5C;
    public const int Numpad0 = 0x60, Numpad1 = 0x61, Numpad9 = 0x69;
    public const int LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5;

    public static bool IsShift(int vk) => vk is Shift or LShift or RShift;

    public static bool IsModifier(int vk) =>
        vk is Shift or LShift or RShift or Control or LControl or RControl or Menu or LMenu or RMenu or LWin or RWin or Capital;
}

// 一次处理的输出：先依次上屏，再把编码区设成 Preedit（空串表示结束编码）
public sealed class ImeOutput
{
    public List<(string Text, int Replace)> Commits { get; } = [];

    public string Preedit { get; set; } = "";

    // 需要文本服务回报光标位置（显示模式提示用）
    public bool WantCaret { get; set; }

    public bool IsEmpty => Commits.Count == 0;
}

// 候选窗要显示的一页
public sealed record CandidatePage(IReadOnlyList<string> Texts, IReadOnlyList<string> Codes, int Highlighted = 0);

// 输入控制器需要的界面能力。实现必须是非阻塞的（内部投递到界面线程）
public interface IImeUi
{
    // 文本服务回报的光标矩形（屏幕物理像素），候选窗据此定位
    void SetCaret(int x, int y, int width, int height);

    void ShowCandidates(CandidatePage page);
    void HideCandidates();
    void ShowModeHint(bool asciiMode);
    void HideModeHint();

    bool IsDeletePanelOpen { get; }
    bool IsRankPanelOpen { get; }

    // 删除候选确认。窗口关闭时（无论是否删除）带回原编码；第二个参数为 true 表示词库已重载
    void PresentDeletePanel(string candidate, string preedit, Action<string, bool> onClosed);
    // 只说明不能删除的原因；窗口同样会抢焦点，关闭时也要交回编码
    void PresentDeleteUnavailable(string reason, string preedit, Action<string, bool> onClosed);
    // 候选排序。关闭时带回原编码；改过权重并重载时第二个参数为 true
    void PresentRankPanel(string preedit, IReadOnlyList<string> candidates, Action<string, bool> onClosed);
    void PresentRankUnavailable(string reason, string preedit, Action<string, bool> onClosed);

    // 记下最近上屏的词，造词窗口没有选区时用它预填
    void NoteCommittedWord(string text);
}
