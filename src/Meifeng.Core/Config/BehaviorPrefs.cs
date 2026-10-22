using System.Text;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Config;

// 翻页快捷键方案（对应 key_bindings.yaml 里的预设）
public enum PagingKeys { MinusEqual, CommaPeriod, Brackets, MinusAndComma }

// 第 2、第 3 候选快捷键（走 Rime key_binder）
public enum SecondThirdKeys { None, SemicolonApostrophe }

public static class BehaviorTitles
{
    public static string Title(this PagingKeys p) => p switch
    {
        PagingKeys.MinusEqual => "-= 翻页",
        PagingKeys.CommaPeriod => ",. 翻页",
        PagingKeys.Brackets => "[] 翻页",
        _ => "-= 与 ,. 同时"
    };

    // 写入 Rime key_binder.bindings 的绑定行。用原始绑定而不用预设 include，
    // 这样能直接追加到用户已有的 bindings 列表后面
    public static string[] BindingLines(this PagingKeys p) => p switch
    {
        PagingKeys.MinusEqual =>
        [
            "- { when: has_menu, accept: minus, send: Page_Up }",
            "- { when: has_menu, accept: equal, send: Page_Down }"
        ],
        PagingKeys.CommaPeriod =>
        [
            "- { when: paging, accept: comma, send: Page_Up }",
            "- { when: has_menu, accept: period, send: Page_Down }"
        ],
        PagingKeys.Brackets =>
        [
            "- { when: paging, accept: bracketleft, send: Page_Up }",
            "- { when: has_menu, accept: bracketright, send: Page_Down }"
        ],
        _ => PagingKeys.MinusEqual.BindingLines().Concat(PagingKeys.CommaPeriod.BindingLines()).ToArray()
    };

    public static string Title(this SecondThirdKeys s) => s == SecondThirdKeys.None ? "无（仅用数字键）" : "; ' 选二三候选";

    public static string[] BindingLines(this SecondThirdKeys s) => s switch
    {
        SecondThirdKeys.None => [],
        _ =>
        [
            "- { when: has_menu, accept: semicolon, send: 2 }",
            "- { when: has_menu, accept: apostrophe, send: 3 }"
        ]
    };
}

// 引擎侧行为：写入 Rime default.custom.yaml，并标记需要重新部署
public static class BehaviorPrefs
{
    private const string KeyPageSize = "rime.pageSize";
    private const string KeyLegacyPageSize = "candidate.pageSize";
    private const string KeyPagingKeys = "rime.pagingKeys";
    private const string KeySecondThirdKeys = "rime.secondThirdKeys";
    private const string KeyEmptyCodeClear = "rime.emptyCodeClear";
    private const string KeyEnterClearsCode = "rime.enterClearsCode";

    // 行为设置写盘后触发，引擎据此重新部署
    public static event Action? Changed;

    private static PrefsStore Store => PrefsStore.Shared;

    // 每页候选数 → menu/page_size。未设置时默认 5
    public static int PageSize
    {
        get
        {
            var v = Store.GetInt(KeyPageSize) ?? 0;
            if (v > 0) return Math.Clamp(v, 3, 9);
            var legacy = Store.GetInt(KeyLegacyPageSize) ?? 0;
            return legacy > 0 ? Math.Clamp(legacy, 3, 9) : 5;
        }
        set
        {
            var clamped = Math.Clamp(value, 3, 9);
            Store.Set(KeyPageSize, clamped);
            Store.Set(KeyLegacyPageSize, clamped);
            Persist();
        }
    }

    public static PagingKeys PagingKeys
    {
        get => Store.GetString(KeyPagingKeys) switch
        {
            "minusEqual" => PagingKeys.MinusEqual,
            "commaPeriod" => PagingKeys.CommaPeriod,
            "brackets" => PagingKeys.Brackets,
            _ => PagingKeys.MinusAndComma
        };
        set
        {
            Store.Set(KeyPagingKeys, value switch
            {
                PagingKeys.MinusEqual => "minusEqual",
                PagingKeys.CommaPeriod => "commaPeriod",
                PagingKeys.Brackets => "brackets",
                _ => "minusAndComma"
            });
            Persist();
        }
    }

    public static SecondThirdKeys SecondThirdKeys
    {
        get => Store.GetString(KeySecondThirdKeys) == "none" ? SecondThirdKeys.None : SecondThirdKeys.SemicolonApostrophe;
        set
        {
            Store.Set(KeySecondThirdKeys, value == SecondThirdKeys.None ? "none" : "semicolonApostrophe");
            Persist();
        }
    }

    // 空码清空：打满四码仍没有候选时清空编码。默认关，与方案里的 auto_clear 注释状态一致
    public static bool EmptyCodeClear
    {
        get => Store.GetBool(KeyEmptyCodeClear) ?? false;
        set => Store.Set(KeyEmptyCodeClear, value);
    }

    // 回车清码：编码中按回车只清空编码，不把编码字母上屏。默认关，与 Rime 的默认回车行为一致
    public static bool EnterClearsCode
    {
        get => Store.GetBool(KeyEnterClearsCode) ?? false;
        set => Store.Set(KeyEnterClearsCode, value);
    }

    // 写入 yaml 并标记需要部署；返回错误文字，成功为 null
    public static string? Persist()
    {
        try
        {
            WriteCustomYaml();
        }
        catch (Exception e)
        {
            return e.Message;
        }
        UserDictWriter.MarkNeedsDeploy();
        Changed?.Invoke();
        return null;
    }

    // 写入 default.custom.yaml 中由设置管理的部分：只改菜单每页候选数和按键绑定，
    // 文件里其他 patch（方案列表、标点、识别器、用户自己的绑定）原样保留
    public static void WriteCustomYaml()
    {
        RimeResourceDeployer.SyncBundledResourcesIfNeeded();
        var user = RimeResourceDeployer.ResolveUserDataDir();
        Directory.CreateDirectory(user);
        var path = Path.Combine(user, "default.custom.yaml");
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        // 内置方案列表随文件保留；文件还不存在时补上内置方案
        var schemaIds = RimeExistingLexicon.SchemaIds(existing);
        foreach (var id in RimeExistingLexicon.RequiredSchemaIds)
        {
            if (!schemaIds.Contains(id)) schemaIds.Add(id);
        }
        if (schemaIds.Count == 0) schemaIds.AddRange(RimeExistingLexicon.RequiredSchemaIds);

        var bindings = PagingKeys.BindingLines().Concat(SecondThirdKeys.BindingLines()).Concat(NumberedModeSwitchBindings);
        var text = RimeCustomPatchWriter.Updated(existing, schemaIds, PageSize, bindings.ToList());
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    // Control+Shift+1..5 切换方案 / 中英 / 全角 / 简体 / 扩展字集，与之前生成的配置保持一致
    private static readonly string[] NumberedModeSwitchBindings =
    [
        "- { when: always, accept: Control+Shift+1, select: .next }",
        "- { when: always, accept: Control+Shift+2, toggle: ascii_mode }",
        "- { when: always, accept: Control+Shift+3, toggle: full_shape }",
        "- { when: always, accept: Control+Shift+4, toggle: simplification }",
        "- { when: always, accept: Control+Shift+5, toggle: extended_charset }"
    ];
}
