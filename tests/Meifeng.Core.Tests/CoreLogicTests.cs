using Meifeng.Core.Config;
using Meifeng.Core.Input;
using Meifeng.Core.Ipc;
using Meifeng.Core.Lexicon;
using Meifeng.Core.Sync;

namespace Meifeng.Core.Tests;

// 与平台无关的核心逻辑：管道协议、候选排序权重、用户词解析、三方合并、自动编码、日期候选
public class CoreLogicTests
{
    [Fact]
    public void QuotesAlternateOpenThenClose()
    {
        var pair = new PunctuationPrefs.PairState();
        Assert.Equal("“", pair.DirectSymbol("\"", ["“", "”"]));
        Assert.Equal("”", pair.DirectSymbol("\"", ["“", "”"]));
        Assert.Equal("“", pair.DirectSymbol("\"", ["“", "”"]));
        Assert.Equal("‘", pair.DirectSymbol("'", ["‘", "’"]));
        Assert.Equal("’", pair.DirectSymbol("'", ["‘", "’"]));
        // 中间夹着别的标点，引号的前后状态还在
        Assert.Equal("，", pair.DirectSymbol(",", ["，"]));
        Assert.Equal("”", pair.DirectSymbol("\"", ["“", "”"]));
        pair.Reset();
        Assert.Equal("“", pair.DirectSymbol("\"", ["“", "”"]));
        Assert.Equal("‘", pair.DirectSymbol("'", ["‘", "’"]));
    }

    [Fact]
    public void ProtocolRoundTripsSpecialCharacters()
    {
        var body = ImeProtocol.Encode([new("commit0", "a=b\n100%\r"), new("preedit", "ggll")]);
        var map = ImeProtocol.Decode(body);
        Assert.Equal("a=b\n100%\r", map["commit0"]);
        Assert.Equal("ggll", map["preedit"]);
    }

    [Fact]
    public void ProtocolKeepsNonEscapePercent()
    {
        Assert.Equal("%zz", ImeProtocol.Unescape("%zz"));
    }

    [Theory]
    // 上移到第一位：比原第一高一个幅度
    [InlineData(new[] { 100, 90, 80 }, 2, 0, 10, 110)]
    // 下移到最后：比原最后低一个幅度
    [InlineData(new[] { 100, 90, 80 }, 0, 2, 5, 75)]
    // 上移到中间且空隙够大：取上方减幅度
    [InlineData(new[] { 100, 50, 20 }, 2, 1, 10, 90)]
    public void RankWeightPlacement(int[] weights, int from, int to, int step, int expected)
    {
        Assert.Equal(expected, CandidateRankWeight.Placed(weights, from, to, step));
    }

    [Fact]
    public void RankWeightNoMoveReturnsNull()
    {
        Assert.Null(CandidateRankWeight.Placed([10, 5], 1, 1, 5));
    }

    [Fact]
    public void ParsesGroupedUserDictionary()
    {
        const string yaml = "---\nname: wubi86_jidian_user\nversion: \"1\"\n...\n## 常用\n玫枫\tggsm\t10\n五笔\tgttk\n\n## 空组\n";
        var doc = UserDictWriter.ParseDocument(yaml);
        Assert.True(doc.IsGroupMode);
        Assert.Equal(2, doc.Groups.Count);
        Assert.Equal("常用", doc.Groups[0].Name);
        Assert.Equal(2, doc.Groups[0].Entries.Count);
        Assert.Equal(10, doc.Groups[0].Entries[0].Weight);
        Assert.Null(doc.Groups[0].Entries[1].Weight);
        Assert.Equal("空组", doc.Groups[1].Name);
    }

    [Fact]
    public void EntryLineSwapsCodeFirstColumns()
    {
        var entry = UserDictWriter.ParseEntryLine("GGSM\t玫枫");
        Assert.NotNull(entry);
        Assert.Equal("玫枫", entry!.Text);
        Assert.Equal("ggsm", entry.Code);
    }

    [Fact]
    public void ThreeWayMergeKeepsBothSidesAdditions()
    {
        var @base = UserDictWriter.ParseDocument("...\n## 默认\n甲\taaaa\n");
        var local = UserDictWriter.ParseDocument("...\n## 默认\n甲\taaaa\n乙\tbbbb\n");
        var remote = UserDictWriter.ParseDocument("...\n## 默认\n甲\taaaa\n丙\tcccc\n");
        var result = DictSyncDiff.ThreeWayMerge(@base, local, remote);
        var words = DictSyncDiff.Flatten(result.Merged).Select(w => w.Word).ToHashSet();
        Assert.Contains("乙", words);
        Assert.Contains("丙", words);
        Assert.True(result.LocalChanged);
        Assert.True(result.NeedsUpload);
    }

    [Fact]
    public void ExtractsSingleCharacterCodes()
    {
        const string dict = "---\nname: test\n...\n工\ta\n工\taaaa\n五\tgg\n五\tgghy\n吾\tgkf\n";
        var map = WubiDictEncoder.ExtractCharacterMap(dict);
        Assert.Equal("aaaa", map["工"]);
        Assert.Equal("gg", map["五"]);
        Assert.Equal("gkf", map["吾"]);
    }

    [Fact]
    public void DateCandidatesUseInjectedClock()
    {
        var previous = DateTimeCandidates.Now;
        try
        {
            DateTimeCandidates.Now = () => new DateTimeOffset(2026, 9, 23, 11, 22, 5, TimeSpan.FromHours(8));
            var list = DateTimeCandidates.CandidatesFor("date");
            Assert.NotNull(list);
            Assert.Equal("2026-09-23", list![0]);
            Assert.Contains("2026年9月23日", list);
            Assert.Null(DateTimeCandidates.CandidatesFor("dat"));
        }
        finally
        {
            DateTimeCandidates.Now = previous;
        }
    }
}
