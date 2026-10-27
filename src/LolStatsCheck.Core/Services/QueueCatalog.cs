using System.Collections.Concurrent;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;

namespace LolStatsCheck.Core.Services;

public sealed class QueueCatalog
{
    // 国服客户端官方队列目录（2026-10）整理；运行时会再合并客户端返回的队列名称，
    // 新上线的模式即使未内置也能显示。
    private static readonly Dictionary<int, string> BuiltIn = new()
    {
        [0] = "自定义",
        [1] = "匹配模式（自选）",
        [2] = "匹配模式（自选）",
        [8] = "匹配模式（自选）",
        [9] = "灵活排位",
        [14] = "匹配模式（征召）",
        [31] = "人机（入门）",
        [32] = "人机（新手）",
        [33] = "人机（一般）",
        [34] = "人机（入门）",
        [35] = "人机（新手）",
        [36] = "人机（一般）",
        [52] = "人机（新手）",
        [62] = "极地大乱斗",
        [63] = "极地大乱斗",
        [64] = "极地大乱斗",
        [65] = "极地大乱斗",
        [90] = "大提魔节",
        [91] = "大提魔节",
        [92] = "100级铁手挑战",
        [96] = "飞升争夺战",
        [97] = "飞升争夺战",
        [100] = "匹配模式（自选）",
        [300] = "魄罗大乱斗",
        [301] = "魄罗大乱斗",
        [314] = "枢纽攻防战",
        [315] = "枢纽攻防战",
        [316] = "这货不是统治战场",
        [317] = "这货不是统治战场",
        [318] = "无限乱斗",
        [319] = "无限乱斗",
        [320] = "红月决",
        [321] = "红月决",
        [324] = "峡谷大乱斗",
        [325] = "峡谷大乱斗",
        [400] = "匹配模式（征召）",
        [401] = "匹配模式（征召）",
        [402] = "匹配模式（征召）",
        [403] = "匹配模式（征召）",
        [410] = "排位赛（征召）",
        [411] = "排位赛（征召）",
        [412] = "排位赛（征召）",
        [413] = "排位赛（征召）",
        [420] = "单双排",
        [421] = "单双排",
        [422] = "单双排",
        [430] = "匹配模式（自选）",
        [431] = "匹配模式（自选）",
        [432] = "匹配模式（自选）",
        [433] = "匹配模式（自选）",
        [440] = "灵活排位",
        [441] = "灵活排位",
        [442] = "灵活排位",
        [450] = "极地大乱斗",
        [460] = "匹配模式（自选）",
        [470] = "灵活排位",
        [480] = "快速模式",
        [490] = "快速匹配",
        [600] = "红月决",
        [601] = "红月决",
        [610] = "暗星：奇点",
        [611] = "暗星：奇点",
        [700] = "冠军杯赛",
        [701] = "冠军杯赛",
        [710] = "5人排位赛",
        [720] = "冠军杯赛（大乱斗）",
        [721] = "冠军杯赛（大乱斗）",
        [740] = "无限乱斗 冠军杯赛",
        [741] = "无限乱斗 冠军杯赛",
        [800] = "人机对战",
        [801] = "人机对战",
        [810] = "人机（入门）",
        [820] = "人机（新手）",
        [830] = "人机（入门）",
        [840] = "人机（新手）",
        [850] = "人机（一般）",
        [860] = "极地大乱斗（人机）",
        [870] = "人机（入门级）",
        [880] = "人机（新手级）",
        [890] = "人机（一般级）",
        [900] = "无限乱斗",
        [910] = "飞升争夺战",
        [920] = "魄罗大乱斗",
        [921] = "魄罗大乱斗",
        [930] = "极地大乱斗",
        [931] = "极地大乱斗",
        [940] = "枢纽攻防战",
        [941] = "枢纽攻防战",
        [950] = "100级铁手挑战",
        [951] = "100级铁手挑战",
        [960] = "大提魔节",
        [961] = "大提魔节",
        [970] = "六杀争夺战",
        [980] = "怪兽入侵（普通）",
        [981] = "怪兽入侵（普通）",
        [982] = "怪兽入侵（普通）",
        [990] = "怪兽入侵（狂袭）",
        [1000] = "超频行动",
        [1001] = "超频行动",
        [1010] = "冰雪无限火力",
        [1011] = "冰雪无限火力",
        [1012] = "冰雪无限火力",
        [1020] = "克隆大作战",
        [1200] = "极限闪击",
        [1300] = "极限闪击",
        [1400] = "终极魔典",
        [1700] = "斗魂竞技场",
        [1710] = "斗魂竞技场",
        [1740] = "勇敢斗魂竞技场",
        [1750] = "斗魂竞技场 3x6",
        [1810] = "无尽狂潮",
        [1820] = "无尽狂潮",
        [1830] = "无尽狂潮",
        [1840] = "无尽狂潮",
        [1900] = "无限火力",
        [2000] = "新手教程（第一部分）",
        [2010] = "新手教程（第二部分）",
        [2020] = "新手教程（第三部分）",
        [2300] = "神木之门",
        [2400] = "海克斯大乱斗",
        [2401] = "海克斯大乱斗",
        [2403] = "海克斯大乱斗",
        [2405] = "海克斯大乱斗",
        [2410] = "海克斯大乱斗 巅峰赛",
        [2450] = "海克斯大乱斗 经典模式版",
        [3100] = "自定义（召唤师峡谷 自选）",
        [3110] = "自定义（召唤师峡谷 征召）",
        [3120] = "自定义（召唤师峡谷 全随机）",
        [3130] = "自定义（召唤师峡谷 比赛征召）",
        [3140] = "自定义（训练模式）",
        [3200] = "自定义（嚎哭深渊 自选）",
        [3210] = "自定义（嚎哭深渊 征召）",
        [3220] = "自定义（嚎哭深渊 全随机）",
        [3230] = "自定义（嚎哭深渊 比赛征召）",
        [3250] = "嚎哭深渊 第一滴血",
        [4200] = "末日人工智能（5v5）",
        [4210] = "末日人工智能",
        [4220] = "末日人工智能（困难）",
        [4240] = "末日人工智能（维迦的诅咒）",
        [4250] = "末日人工智能（维迦的邪咒）",
        [4260] = "末日人工智能（维迦的末日厄咒）",
        [4310] = "经典模式 5v5"
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
                var name = Normalize(item.GetString("name"));
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
        if (BuiltIn.TryGetValue(queueId, out var builtIn))
        {
            return builtIn;
        }

        if (_lcuNames.TryGetValue(queueId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return queueId > 0 ? $"队列 {queueId}" : "未知模式";
    }

    public bool IsKnown(int queueId) => queueId > 0 && (BuiltIn.ContainsKey(queueId) || _lcuNames.ContainsKey(queueId));

    private static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        while (trimmed.Contains("  ", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);
        }

        return trimmed;
    }
}
