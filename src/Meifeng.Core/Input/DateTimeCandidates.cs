using System.Globalization;

namespace Meifeng.Core.Input;

// 输入 date / time / week 编码时，插入基于系统时间的候选
// 五笔最多四码，只保留四码的 date / time / week
public static class DateTimeCandidates
{
    // 测试时可固定当前时间
    public static Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    // 匹配则返回候选列表；不匹配返回 null
    public static List<string>? CandidatesFor(string preedit)
    {
        var key = preedit.Trim().ToLowerInvariant();
        if (key.Length == 0) return null;
        var now = Now();
        return key switch
        {
            // 日期在前，日期 + 时间组合排在后面翻页可见
            "date" => Unique(DateFormats(now).Concat(DateTimeFormats(now))),
            "time" => TimeFormats(now),
            "week" => WeekFormats(now),
            _ => null
        };
    }

    private static IEnumerable<string> DateFormats(DateTimeOffset t)
    {
        int y = t.Year, m = t.Month, d = t.Day;
        var mm = m.ToString("00");
        var dd = d.ToString("00");
        var weekday = WeekdayCN(t.DayOfWeek);
        return Unique([
            $"{y}-{mm}-{dd}",
            $"{y}/{mm}/{dd}",
            $"{y}.{mm}.{dd}",
            $"{y}{mm}{dd}",
            $"{y}年{m}月{d}日",
            $"{m}月{d}日",
            $"{mm}-{dd}",
            $"{mm}/{dd}",
            weekday,
            $"{y}年{m}月{d}日 {weekday}"
        ]);
    }

    // 第 3 位秒级时间戳、第 4 位「时分」，其余紧凑格式排在后面
    private static List<string> TimeFormats(DateTimeOffset t)
    {
        int h = t.Hour, mi = t.Minute, s = t.Second;
        var hh = h.ToString("00");
        var mm = mi.ToString("00");
        var ss = s.ToString("00");
        var ts = t.ToUnixTimeSeconds();
        var tsMs = t.ToUnixTimeMilliseconds();
        return Unique([
            $"{hh}:{mm}:{ss}",
            $"{hh}:{mm}",
            $"{ts}",
            $"{h}时{mi}分",
            $"{hh}{mm}{ss}",
            $"{hh}{mm}",
            $"{h}时{mi}分{s}秒",
            $"{tsMs}"
        ]);
    }

    // 星期与周数：周数按 ISO 8601（周一为一周开始）
    private static List<string> WeekFormats(DateTimeOffset t)
    {
        var full = WeekdayCN(t.DayOfWeek);
        var shortName = full.Replace("星期", "周");
        var libai = full.Replace("星期", "礼拜");
        var week = ISOWeek.GetWeekOfYear(t.DateTime);
        var weekYear = ISOWeek.GetYear(t.DateTime);
        var en = CultureInfo.InvariantCulture;
        return Unique([
            full,
            shortName,
            $"第{week}周",
            $"{weekYear}年第{week}周",
            libai,
            t.ToString("dddd", en),
            t.ToString("ddd", en),
            $"{weekYear}-W{week:00}"
        ]);
    }

    private static IEnumerable<string> DateTimeFormats(DateTimeOffset t)
    {
        int y = t.Year, mo = t.Month, d = t.Day, h = t.Hour, mi = t.Minute, s = t.Second;
        string mm = mo.ToString("00"), dd = d.ToString("00"), hh = h.ToString("00"), mi2 = mi.ToString("00"), ss = s.ToString("00");
        return Unique([
            $"{y}-{mm}-{dd} {hh}:{mi2}:{ss}",
            $"{y}/{mm}/{dd} {hh}:{mi2}:{ss}",
            $"{y}{mm}{dd}{hh}{mi2}{ss}",
            $"{y}年{mo}月{d}日 {h}时{mi}分{s}秒",
            $"{y}-{mm}-{dd} {hh}:{mi2}",
            // 与 ISO8601DateFormatter 默认输出一致：UTC 且带 Z
            t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        ]);
    }

    private static string WeekdayCN(DayOfWeek w) => w switch
    {
        DayOfWeek.Sunday => "星期日",
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        _ => "星期六"
    };

    private static List<string> Unique(IEnumerable<string> items)
    {
        var seen = new HashSet<string>();
        return items.Where(s => s.Length > 0 && seen.Add(s)).ToList();
    }
}
