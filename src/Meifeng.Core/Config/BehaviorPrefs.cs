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

    // 写入 Rime key_binder 的 include 列表
    public static string[] BindingIncludes(this PagingKeys p) => p switch
    {
        PagingKeys.MinusEqual => ["key_bindings:/paging_with_minus_equal"],
        PagingKeys.CommaPeriod => ["key_bindings:/paging_with_comma_period"],
        PagingKeys.Brackets => ["key_bindings:/paging_with_brackets"],
        _ => ["key_bindings:/paging_with_minus_equal", "key_bindings:/paging_with_comma_period"]
    };

    public static string Title(this SecondThirdKeys s) => s == SecondThirdKeys.None ? "无（仅用数字键）" : "; ' 选二三候选";

    public static string[] BindingIncludes(this SecondThirdKeys s) =>
        s == SecondThirdKeys.None ? [] : ["key_bindings:/select_2nd_3rd_with_semicolon_apostrophe"];
}

// 引擎侧行为：写入 Rime default.custom.yaml，并标记需要重新部署
public static class BehaviorPrefs
{
    private const string KeyPageSize = "rime.pageSize";
    private const string KeyLegacyPageSize = "candidate.pageSize";
    private const string KeyPagingKeys = "rime.pagingKeys";
    private const string KeySecondThirdKeys = "rime.secondThirdKeys";
    public const string GeneratedMarker = "由玫枫五笔设置生成";

    // 行为设置写盘后触发，引擎据此重新部署
    public static event Action? Changed;

    private static PrefsStore Store => PrefsStore.Shared;

    // 每页候选数 → menu/page_size
    public static int PageSize
    {
        get
        {
            var v = Store.GetInt(KeyPageSize) ?? 0;
            if (v > 0) return Math.Clamp(v, 3, 9);
            var legacy = Store.GetInt(KeyLegacyPageSize) ?? 0;
            return legacy > 0 ? Math.Clamp(legacy, 3, 9) : 9;
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

    // 生成 / 覆盖 default.custom.yaml 中的 patch
    public static void WriteCustomYaml()
    {
        RimeResourceDeployer.SyncBundledResourcesIfNeeded();
        var user = RimeResourceDeployer.ResolveUserDataDir();
        Directory.CreateDirectory(user);
        var path = Path.Combine(user, "default.custom.yaml");
        // 只更新玫枫自己生成过的 custom；上游或用户原文件保持不动
        if (!MayRewriteCustomYaml(path)) return;
        var existing = File.ReadAllText(path);
        var schemaIds = RimeExistingLexicon.SchemaIds(existing);
        foreach (var id in RimeExistingLexicon.RequiredSchemaIds)
        {
            if (!schemaIds.Contains(id)) schemaIds.Add(id);
        }
        var sb = new StringBuilder();
        sb.Append("# ").Append(GeneratedMarker).Append("，修改「候选栏设置」会覆盖此文件中的 patch\n");
        sb.Append("patch:\n  schema_list:\n");
        foreach (var id in schemaIds) sb.Append("    - schema: ").Append(id).Append('\n');
        sb.Append($"  \"menu/page_size\": {PageSize}\n");
        sb.Append("  \"key_binder/bindings\":\n    __patch:\n      - key_bindings:/emacs_editing\n");
        foreach (var inc in PagingKeys.BindingIncludes()) sb.Append("      - ").Append(inc).Append('\n');
        foreach (var inc in SecondThirdKeys.BindingIncludes()) sb.Append("      - ").Append(inc).Append('\n');
        sb.Append("      - key_bindings:/numbered_mode_switch\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    // 只改写带玫枫标记的 default.custom.yaml，避免盖掉极点原配置
    private static bool MayRewriteCustomYaml(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Contains(GeneratedMarker);
        }
        catch
        {
            return false;
        }
    }
}
