using System.Collections.Concurrent;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;

namespace LolStatsCheck.Core.Services;

public sealed class QueueCatalog
{
    private static readonly Dictionary<int, string> BuiltIn = new()
    {
        [400] = "匹配模式（征召）",
        [420] = "单双排",
        [430] = "匹配模式（盲选）",
        [440] = "灵活排位",
        [450] = "极地大乱斗",
        [490] = "快速匹配",
        [720] = "大乱斗：克隆",
        [900] = "无限火力",
        [1020] = "克隆大作战",
        [1300] = "极限闪击",
        [1700] = "斗魂竞技场",
        [2400] = "大乱斗：狂欢",
        [0] = "自定义"
    };

    private readonly Func<LcuClient?> _clientProvider;
    private readonly LogService _log;
    private readonly ConcurrentDictionary<int, string> _lcuNames = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private volatile bool _loaded;

    public QueueCatalog(Func<LcuClient?> clientProvider, LogService log)
    {
        _clientProvider = clientProvider;
        _log = log;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var client = _clientProvider();
            if (client is null)
            {
                return;
            }

            var json = await client.GetStringAsync("/lol-game-queues/v1/queues", cancellationToken)
                .ConfigureAwait(false);
            using var document = JsonHelpers.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetInt32("id");
                var name = item.GetString("name");
                if (id > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    _lcuNames[id] = name;
                }
            }

            _loaded = _lcuNames.Count > 0;
        }
        catch (Exception ex) when (ex is LcuException or JsonException)
        {
            _log.Warn("加载队列名称失败：" + ex.Message);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public string GetName(int queueId)
    {
        if (_lcuNames.TryGetValue(queueId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (BuiltIn.TryGetValue(queueId, out name))
        {
            return name;
        }

        return queueId > 0 ? $"队列 {queueId}" : "未知模式";
    }
}
