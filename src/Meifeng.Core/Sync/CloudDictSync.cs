using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meifeng.Core.Config;
using Meifeng.Core.Lexicon;

namespace Meifeng.Core.Sync;

// 用户词双向同步与扩展词库拉取。部署由调用方触发
public static class CloudDictSync
{
    public sealed record CompareOutcome(DictSyncDiff.Result Diff, string? RemoteYaml, UserDictDocument? RemoteDocument)
    {
        public string Summary => Diff.Summary;
        public bool HasRemote => Diff.HasRemote;
    }

    public static async Task<CompareOutcome> CompareAsync()
    {
        var local = UserDictWriter.LoadActiveDocument();
        var pulled = await WubiCloudApi.PullDictAsync();
        if (pulled != null)
        {
            var remote = UserDictWriter.ParseDocument(pulled.Yaml);
            return new CompareOutcome(DictSyncDiff.Compare(local, remote), pulled.Yaml, remote);
        }
        return new CompareOutcome(DictSyncDiff.Compare(local, null), null, null);
    }

    public static async Task<string> UploadLocalAsync()
    {
        var yaml = UserDictWriter.ExportText();
        var count = UserDictWriter.LoadActiveDocument().EntryCount;
        var revision = await WubiCloudApi.PushDictAsync(yaml, count);
        UserDictAutoSync.NoteCloudRevision(revision, UserDictWriter.LoadActiveDocument());
        return $"上传完成：已用本地 {count} 条覆盖云端";
    }

    public static async Task<string> DownloadAsync(string? cachedYaml)
    {
        string yaml;
        if (!string.IsNullOrEmpty(cachedYaml)) yaml = cachedYaml;
        else if (await WubiCloudApi.PullDictAsync() is { } pulled) yaml = pulled.Yaml;
        else throw new WubiCloudException("云端尚无备份，无法下载");
        UserDictWriter.ImportText(yaml);
        var count = UserDictWriter.ParseDocument(yaml).EntryCount;
        try
        {
            if (await WubiCloudApi.FetchMetaAsync() is { } meta)
            {
                UserDictAutoSync.NoteCloudRevision(meta.Revision, UserDictWriter.LoadActiveDocument());
            }
        }
        catch
        {
            // 版本读取失败不影响下载结果
        }
        return $"下载完成：已用云端 {count} 条覆盖本地";
    }

    public static async Task<string> MergeAsync(UserDictDocument? remote)
    {
        UserDictDocument remoteDoc;
        if (remote != null) remoteDoc = remote;
        else if (await WubiCloudApi.PullDictAsync() is { } pulled) remoteDoc = UserDictWriter.ParseDocument(pulled.Yaml);
        else return await UploadLocalAsync();
        var local = UserDictWriter.LoadActiveDocument();
        var (merged, added) = DictSyncDiff.MergeRemoteIntoLocal(local, remoteDoc);
        UserDictWriter.SaveActiveDocument(merged);
        var yaml = UserDictWriter.ExportText();
        var revision = await WubiCloudApi.PushDictAsync(yaml, merged.EntryCount);
        UserDictAutoSync.NoteCloudRevision(revision, merged);
        if (added == 0) return $"合并完成：没有新词条，已把本地 {merged.EntryCount} 条上传云端";
        return $"合并完成：并入 {added} 条，现共 {merged.EntryCount} 条，已上传云端";
    }

    // 扩展词库只拉取覆盖，返回新增条数和覆盖后的总条数
    public static async Task<(int Added, int WordCount)> PullExtraAsync()
    {
        var items = await WubiCloudApi.PullExtraDictAsync();
        return ExtraDictUpdater.ApplyOnlineItems(items);
    }
}

// 用户词自动同步。快照记在 %APPDATA%\MeifengWubi，不进 Rime 词库目录
public static class UserDictAutoSync
{
    // 启动时拉一次；本地改词后稍等再传；运行中定期收下其他设备的修改
    private static readonly TimeSpan LaunchDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LocalEditDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public enum Tone { Success, Error, Attention }

    public sealed record LastResult(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("tone")] string ToneRaw)
    {
        [JsonIgnore]
        public Tone Tone => ToneRaw switch { "error" => Tone.Error, "attention" => Tone.Attention, _ => Tone.Success };
    }

    private sealed class Store
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("last")] public LastResult? Last { get; set; }
    }

    private static readonly object StateLock = new();
    private static bool _installed, _applying, _running, _pending, _retryQueued;
    private static Timer? _timer;
    private static Timer? _debounce;

    // 一轮同步结束（含失败）后触发，设置页刷新状态
    public static event Action? DidFinish;

    // 自动同步把云端改动写回本地后触发
    public static event Action? UserDictDidChange;

    public static bool IsEnabled
    {
        get => LoadStore().Enabled;
        set
        {
            var s = LoadStore();
            s.Enabled = value;
            SaveStore(s);
            if (value) Start(TimeSpan.FromMilliseconds(300));
        }
    }

    public static LastResult? Last => LoadStore().Last;

    public static bool IsApplying
    {
        get { lock (StateLock) return _applying; }
    }

    // 登录后如果开关已经开着，马上补一轮
    public static void SyncSoon()
    {
        Install();
        Start(TimeSpan.FromMilliseconds(300));
    }

    // 程序启动时调用。开关关闭时定时器仍在，到点会直接返回
    public static void Install()
    {
        lock (StateLock)
        {
            if (_installed) return;
            _installed = true;
        }
        _timer = new Timer(_ => Start(TimeSpan.Zero), null, Interval, Interval);
        Start(LaunchDelay);
    }

    // 本地词库刚写入。自动同步过程中的写回不从这里再排
    public static void NoteLocalEdit()
    {
        lock (StateLock)
        {
            if (_applying) return;
        }
        if (!IsEnabled || !WubiCloudConfig.IsLoggedIn) return;
        lock (StateLock)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => Start(TimeSpan.Zero), null, LocalEditDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private static void Start(TimeSpan delay)
    {
        if (!IsEnabled || !WubiCloudConfig.IsLoggedIn) return;
        _ = Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            if (!ClaimRun()) return;
            await RunOnce();
            if (FinishRun()) Start(TimeSpan.Zero);
        });
    }

    private static async Task RunOnce()
    {
        if (!IsEnabled || !WubiCloudConfig.IsLoggedIn) return;
        using var fileLock = AcquireLock();
        if (fileLock == null)
        {
            ScheduleRetry();
            return;
        }
        try
        {
            var (text, tone) = await SyncBody();
            Record(text, tone);
        }
        catch (Exception e)
        {
            Record($"自动同步失败：{e.Message}", Tone.Error);
        }
    }

    // 手动上传或下载成功后记下版本，避免下一轮自动同步再整份重拉
    public static void NoteCloudRevision(int revision, UserDictDocument snapshot)
    {
        if (revision <= 0) return;
        SaveRevision(revision);
        try { SaveBase(snapshot); } catch { }
    }

    private static async Task<(string, Tone)> SyncBody()
    {
        var meta = await WubiCloudApi.FetchMetaAsync();
        var savedRev = LoadRevision();
        var @base = LoadBase();
        var local = UserDictWriter.LoadActiveDocument();
        var localDirty = @base == null || !DictSyncDiff.SameDocument(local, @base);
        // 云端版本没变，本地也没改：不下载正文
        if (meta != null && meta.Revision == savedRev && savedRev > 0 && !localDirty)
        {
            return ("自动同步：本地与云端一致", Tone.Success);
        }
        // 版本没变但本地改过：云端正文就是上次快照，不必再拉
        if (meta != null && meta.Revision == savedRev && savedRev > 0 && @base != null && localDirty)
        {
            return await MergeAndPush(@base, savedRev, @base, true);
        }
        return await PullMergeAndPush(true);
    }

    private static async Task<(string, Tone)> PullMergeAndPush(bool allowRetry)
    {
        var pulled = await WubiCloudApi.PullDictAsync();
        var @base = LoadBase();
        if (pulled == null)
        {
            var local = UserDictWriter.LoadActiveDocument();
            var yaml = UserDictWriter.ExportText();
            var revision = await WubiCloudApi.PushDictAsync(yaml, local.EntryCount, 0);
            SaveBase(local);
            SaveRevision(revision);
            return NotingReload(($"自动同步：云端还没有备份，已上传本地 {local.EntryCount} 条", Tone.Success));
        }
        return await MergeAndPush(UserDictWriter.ParseDocument(pulled.Yaml), pulled.Revision, @base, allowRetry);
    }

    private static async Task<(string, Tone)> MergeAndPush(UserDictDocument remote, int remoteRevision, UserDictDocument? @base, bool allowRetry)
    {
        var local = UserDictWriter.LoadActiveDocument();
        var merged = DictSyncDiff.ThreeWayMerge(@base, local, remote);
        if (merged.LocalChanged)
        {
            SetApplying(true);
            try
            {
                UserDictWriter.SaveActiveDocument(merged.Merged);
            }
            finally
            {
                SetApplying(false);
            }
            UserDictDidChange?.Invoke();
        }
        var revision = remoteRevision;
        if (merged.NeedsUpload)
        {
            var fresh = UserDictWriter.LoadActiveDocument();
            // 上传前本地又被改过，这次不覆盖云端，等下一轮用新内容再合并
            if (!DictSyncDiff.SameDocument(fresh, merged.Merged))
            {
                NoteLocalEdit();
                return NotingReload(("自动同步：本地又有新改动，稍后重试", Tone.Attention));
            }
            try
            {
                revision = await WubiCloudApi.PushDictAsync(UserDictWriter.ExportText(), merged.Merged.EntryCount, remoteRevision);
            }
            catch (RevisionConflictException)
            {
                // 别的设备刚写过。快照先不动，再拉一次全文重新合并
                if (!allowRetry) throw;
                return await PullMergeAndPush(false);
            }
        }
        // 冲突词的快照保持旧内容，下一轮仍会标成冲突，不会被静默上传
        SaveBase(merged.NextBase);
        if (revision > 0) SaveRevision(revision);
        return NotingReload(Message(merged));
    }

    // 本地文件比上次成功加载新才通知引擎；只上传、文件没变时不部署
    private static (string, Tone) NotingReload((string Text, Tone Tone) result)
    {
        if (!RequestEngineReloadIfStale()) return result;
        return (result.Text + "，已重新加载用户词", result.Tone);
    }

    private static bool RequestEngineReloadIfStale()
    {
        var path = UserDictWriter.ActivePath;
        if (!File.Exists(path)) return false;
        var modified = File.GetLastWriteTimeUtc(path).Ticks;
        var stamp = EngineReloadStamp();
        if (stamp != null && stamp.Value.Path == path && modified <= stamp.Value.Ticks) return false;
        UserDictWriter.MarkNeedsDeploy();
        return true;
    }

    // 引擎增量部署成功后记下文件时间和路径，避免每 10 分钟重复加载
    public static void NoteEngineDidReload()
    {
        try
        {
            var path = UserDictWriter.ActivePath;
            if (!File.Exists(path)) return;
            Directory.CreateDirectory(AppPaths.ContainerDir);
            File.WriteAllText(EngineReloadStampPath, $"{File.GetLastWriteTimeUtc(path).Ticks}\n{path}\n");
        }
        catch
        {
            // 时间戳写不进去只会多部署一次
        }
    }

    private static string EngineReloadStampPath => Path.Combine(AppPaths.ContainerDir, "user-dict-engine-reload.txt");

    private static (long Ticks, string Path)? EngineReloadStamp()
    {
        try
        {
            var lines = File.ReadAllText(EngineReloadStampPath).Split('\n');
            if (lines.Length >= 2 && long.TryParse(lines[0], out var t)) return (t, lines[1]);
        }
        catch
        {
            // 没有时间戳视为需要加载
        }
        return null;
    }

    private static (string, Tone) Message(DictSyncDiff.ThreeWayMergeResult m)
    {
        if (m.Conflicts.Count > 0)
        {
            var names = string.Join("、", m.Conflicts.Take(3).Select(c => c.Word));
            var suffix = m.Conflicts.Count > 3 ? $" 等 {m.Conflicts.Count} 条" : "";
            return ($"自动同步：{names}{suffix} 两边都改过，已保留本地，未上传", Tone.Attention);
        }
        if (m.NeedsUpload && m.TookRemote > 0) return ($"自动同步：已收下云端 {m.TookRemote} 条，并上传本地改动", Tone.Success);
        if (m.NeedsUpload) return ("自动同步：已上传本地改动", Tone.Success);
        if (m.TookRemote > 0 || m.Dropped > 0) return ($"自动同步：已更新本地，收下 {m.TookRemote} 条，删除 {m.Dropped} 条", Tone.Success);
        return ("自动同步：本地与云端一致", Tone.Success);
    }

    private static bool ClaimRun()
    {
        lock (StateLock)
        {
            if (_running)
            {
                _pending = true;
                return false;
            }
            _running = true;
            return true;
        }
    }

    private static bool FinishRun()
    {
        lock (StateLock)
        {
            _running = false;
            var again = _pending;
            _pending = false;
            return again;
        }
    }

    private static void SetApplying(bool v)
    {
        lock (StateLock) _applying = v;
    }

    private static void ScheduleRetry()
    {
        lock (StateLock)
        {
            if (_retryQueued) return;
            _retryQueued = true;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            lock (StateLock) _retryQueued = false;
            Start(TimeSpan.Zero);
        });
    }

    private static void Record(string text, Tone tone)
    {
        var s = LoadStore();
        s.Last = new LastResult(text, tone switch { Tone.Error => "error", Tone.Attention => "attention", _ => "success" });
        SaveStore(s);
        DidFinish?.Invoke();
    }

    // 独占文件锁，防止多个实例同时同步
    private static FileStream? AcquireLock()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ContainerDir);
            return new FileStream(Path.Combine(AppPaths.ContainerDir, "user-dict-sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static UserDictDocument? LoadBase()
    {
        var p = BasePath;
        return File.Exists(p) ? UserDictWriter.LoadDocument(p) : null;
    }

    private static int LoadRevision()
    {
        try { return int.TryParse(File.ReadAllText(RevisionPath).Trim(), out var v) ? v : 0; } catch { return 0; }
    }

    private static void SaveRevision(int revision)
    {
        if (revision <= 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RevisionPath)!);
            File.WriteAllText(RevisionPath, revision.ToString());
        }
        catch
        {
            // 版本写不进去下次会整份重拉
        }
    }

    private static string RevisionPath => BasePath + ".rev";

    private static void SaveBase(UserDictDocument d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BasePath)!);
        UserDictWriter.SaveDocument(d, BasePath, markDeploy: false);
    }

    // 按词库路径分开快照，换目录后不沿用另一份词库的基线
    private static string BasePath
    {
        get
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(UserDictWriter.ActivePath)).Replace('/', '_');
            return Path.Combine(AppPaths.ContainerDir, "sync-base", encoded + ".yaml");
        }
    }

    private static string StorePath => Path.Combine(AppPaths.ContainerDir, "user-dict-auto-sync.json");

    private static Store LoadStore()
    {
        try
        {
            if (File.Exists(StorePath)) return JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath)) ?? new Store();
        }
        catch
        {
            // 损坏时视为关闭
        }
        return new Store();
    }

    private static void SaveStore(Store s)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ContainerDir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(s));
        }
        catch
        {
            // 写不进去时本轮结果只在内存
        }
    }
}
