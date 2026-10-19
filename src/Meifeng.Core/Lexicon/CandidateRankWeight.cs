namespace Meifeng.Core.Lexicon;

// 按幅度 5 或 10，算被拖动候选要写成的权重
public static class CandidateRankWeight
{
    // weights 是当前列表从上到下的权重，to 是松手时的目标行号
    // 上移：比目标位置下方那条高一个幅度；下移：比目标位置上方那条低一个幅度
    // 两边的空隙比幅度小（常见于权重相同）时，仍按这个幅度跨过邻项
    public static int? Placed(IReadOnlyList<int> weights, int from, int to, int step)
    {
        if (from < 0 || from >= weights.Count || from == to || step <= 0) return null;
        var others = weights.ToList();
        others.RemoveAt(from);
        if (others.Count == 0) return null;
        var dest = Math.Clamp(to, 0, others.Count);
        if (dest == 0) return others[0] + step;
        if (dest >= others.Count) return others[^1] - step;
        var above = others[dest - 1];
        var below = others[dest];
        if (above - step > below) return above - step;
        if (below + step < above) return below + step;
        return to < from ? below + step : above - step;
    }

    // 权重大于它的排在前面；权重相同的排在它后面
    public static int IndexFor(int weight, IEnumerable<int> others) => others.Count(w => w > weight);

    // 用户词优先于扩展、Hamster 和主词库
    public static int SourceRank(DictLexiconHit hit) => Path.GetFileName(hit.FilePath) switch
    {
        "wubi86_jidian_user.dict.yaml" => 0,
        "wubi86_jidian_extra.dict.yaml" => 1,
        "wubi86_jidian_user_hamster.dict.yaml" => 2,
        "wubi86_jidian.dict.yaml" => 3,
        _ => 4
    };

    // 同一词有多条时，改当前排在前面的那条：权重大的优先，一样大则改用户词
    public static DictLexiconHit? PreferredHit(IEnumerable<DictLexiconHit> hits) =>
        hits.OrderByDescending(h => h.Entry.Weight ?? int.MinValue).ThenBy(SourceRank).FirstOrDefault();
}
