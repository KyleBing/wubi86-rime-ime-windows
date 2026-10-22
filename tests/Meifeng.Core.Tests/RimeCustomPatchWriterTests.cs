using Meifeng.Core.Config;

namespace Meifeng.Core.Tests;

// default.custom.yaml 增量改写：内置模板没有玫枫标记时也要能写入设置
public class RimeCustomPatchWriterTests
{
    private const string Template = """
# default.custom.yaml
patch:
  schema_list:
    - schema: wubi86_jidian
    - schema: pinyin_simp
  menu:
    page_size: 5                    # 候选词数量
  key_binder:
    bindings:
      - { when: has_menu, accept: semicolon, send: 2 }
      - { when: has_menu, accept: bracketleft, send: Page_Up }
      - { when: composing, accept: Control+p, send: Up }
  recognizer:
    patterns:
      uppercase: "[A-Z][-_+.'0-9A-Za-z]*$"
""";

    private static readonly string[] Bindings =
    [
        "- { when: has_menu, accept: minus, send: Page_Up }",
        "- { when: has_menu, accept: equal, send: Page_Down }"
    ];

    [Fact]
    public void ReplacesPageSizeInExistingMenu()
    {
        var text = RimeCustomPatchWriter.Updated(Template, ["wubi86_jidian"], 7, Bindings);
        Assert.Contains("    page_size: 7", text);
        // 模板里的旧值不能再留一份
        Assert.DoesNotContain("page_size: 5", text);
        Assert.Single(text.Split('\n'), l => l.Trim().StartsWith("page_size:"));
    }

    [Fact]
    public void KeepsUnmanagedPatchContent()
    {
        var text = RimeCustomPatchWriter.Updated(Template, ["wubi86_jidian"], 7, Bindings);
        // 方案列表、识别器和非翻页绑定都原样保留
        Assert.Contains("- schema: pinyin_simp", text);
        Assert.Contains("uppercase:", text);
        Assert.Contains("accept: Control+p, send: Up", text);
    }

    [Fact]
    public void AppendsBindingsAfterExistingOnes()
    {
        var text = RimeCustomPatchWriter.Updated(Template, ["wubi86_jidian"], 7, Bindings)
            .Replace("\r\n", "\n");
        // 翻页绑定由设置决定：模板里的 [] 翻页去掉，只留生成的一套
        Assert.DoesNotContain("accept: bracketleft", text);
        Assert.Contains("- { when: has_menu, accept: minus, send: Page_Up }", text);
        Assert.Contains(RimeCustomPatchWriter.BeginMarker, text);
        Assert.Contains(RimeCustomPatchWriter.EndMarker, text);
    }

    [Fact]
    public void ApplyingTwiceIsStable()
    {
        var once = RimeCustomPatchWriter.Updated(Template, ["wubi86_jidian"], 7, Bindings);
        var twice = RimeCustomPatchWriter.Updated(once, ["wubi86_jidian"], 7, Bindings);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void CreatesPatchWhenFileIsMissing()
    {
        var text = RimeCustomPatchWriter.Updated("", ["wubi86_jidian", "pinyin_simp"], 9, Bindings);
        Assert.Contains("patch:", text);
        Assert.Contains("- schema: pinyin_simp", text);
        Assert.Contains("    page_size: 9", text);
        Assert.Contains("accept: equal, send: Page_Down", text);
    }

    [Fact]
    public void ReplacesLegacyPathKeys()
    {
        const string legacy = """
# 由玫枫五笔设置生成，修改「候选栏设置」会覆盖此文件中的 patch
patch:
  schema_list:
    - schema: wubi86_jidian
  "menu/page_size": 4
  "key_binder/bindings":
    __patch:
      - key_bindings:/paging_with_minus_equal
""";
        var text = RimeCustomPatchWriter.Updated(legacy, ["wubi86_jidian"], 6, Bindings);
        Assert.DoesNotContain("\"menu/page_size\"", text);
        Assert.DoesNotContain("\"key_binder/bindings\"", text);
        Assert.Contains("    page_size: 6", text);
        Assert.Contains("accept: minus, send: Page_Up", text);
    }
}
