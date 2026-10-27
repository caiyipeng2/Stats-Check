using System.Collections.Concurrent;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed record ChampionInfo(int Id, string Name, string Alias, string IconPath);

public sealed class ChampionCatalog
{
    private readonly Func<LcuClient?> _clientProvider;
    private readonly LogService _log;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ConcurrentDictionary<int, ChampionInfo> _champions = new();
    private readonly ConcurrentDictionary<int, byte[]> _championIcons = new();
    private readonly ConcurrentDictionary<int, byte[]> _profileIcons = new();
    private volatile bool _loaded;

    public ChampionCatalog(Func<LcuClient?> clientProvider, LogService log)
    {
        _clientProvider = clientProvider;
        _log = log;
    }

    public bool IsLoaded => _loaded;

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

            var json = await client.GetStringAsync(
                "/lol-game-data/assets/v1/champion-summary.json", cancellationToken).ConfigureAwait(false);
            using var document = JsonHelpers.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetInt32("id");
                if (id <= 0)
                {
                    continue;
                }

                _champions[id] = new ChampionInfo(
                    id,
                    item.GetString("name") ?? $"#{id}",
                    item.GetString("alias") ?? string.Empty,
                    item.GetString("squarePortraitPath") ?? string.Empty);
            }

            _loaded = _champions.Count > 0;
            _log.Info($"英雄数据已加载：{_champions.Count} 个");
        }
        catch (Exception ex) when (ex is LcuException or JsonException)
        {
            _log.Warn("加载英雄数据失败：" + ex.Message);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public string GetName(int championId)
    {
        if (championId <= 0)
        {
            return string.Empty;
        }

        return _champions.TryGetValue(championId, out var info) ? info.Name : $"#{championId}";
    }

    public string GetAlias(int championId) =>
        _champions.TryGetValue(championId, out var info) ? info.Alias : string.Empty;

    public int FindChampionId(string aliasOrName)
    {
        if (string.IsNullOrWhiteSpace(aliasOrName))
        {
            return 0;
        }

        foreach (var info in _champions.Values)
        {
            if (info.Alias.Equals(aliasOrName, StringComparison.OrdinalIgnoreCase) ||
                info.Name.Equals(aliasOrName, StringComparison.OrdinalIgnoreCase))
            {
                return info.Id;
            }
        }

        return 0;
    }

    public async Task<byte[]?> GetChampionIconAsync(int championId, CancellationToken cancellationToken)
    {
        if (championId <= 0)
        {
            return null;
        }

        if (_championIcons.TryGetValue(championId, out var cached))
        {
            return cached;
        }

        var client = _clientProvider();
        if (client is null)
        {
            return null;
        }

        var path = _champions.TryGetValue(championId, out var info) && !string.IsNullOrEmpty(info.IconPath)
            ? info.IconPath
            : $"/lol-game-data/assets/v1/champion-icons/{championId}.png";

        try
        {
            var bytes = await client.GetBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                _championIcons[championId] = bytes;
                return bytes;
            }
        }
        catch (Exception ex) when (ex is LcuException)
        {
            _log.Debug($"加载英雄头像失败：{championId} {ex.Message}");
        }

        return null;
    }

    public async Task<byte[]?> GetProfileIconAsync(int iconId, CancellationToken cancellationToken)
    {
        if (iconId <= 0)
        {
            return null;
        }

        if (_profileIcons.TryGetValue(iconId, out var cached))
        {
            return cached;
        }

        var client = _clientProvider();
        if (client is null)
        {
            return null;
        }

        try
        {
            var bytes = await client.GetBytesAsync(
                $"/lol-game-data/assets/v1/profile-icons/{iconId}.jpg", cancellationToken).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                _profileIcons[iconId] = bytes;
                return bytes;
            }
        }
        catch (Exception ex) when (ex is LcuException)
        {
            _log.Debug($"加载玩家头像失败：{iconId} {ex.Message}");
        }

        return null;
    }

    public static string GetMockName(int championId) => MockChampionNames.GetName(championId);
}

internal static class MockChampionNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [11] = "易大师",
        [16] = "索拉卡",
        [17] = "提莫",
        [21] = "厄运小姐",
        [22] = "艾希",
        [40] = "迦娜",
        [51] = "凯特琳",
        [53] = "布里茨",
        [54] = "墨菲特",
        [59] = "贾克斯",
        [64] = "李青",
        [81] = "伊泽瑞尔",
        [84] = "阿卡丽",
        [86] = "盖伦",
        [89] = "蕾欧娜",
        [99] = "拉克丝",
        [103] = "阿狸",
        [105] = "菲兹",
        [110] = "韦鲁斯",
        [111] = "诺提勒斯",
        [113] = "瑟庄妮",
        [121] = "卡兹克",
        [122] = "德莱厄斯",
        [147] = "萨勒芬妮",
        [157] = "亚索",
        [201] = "布隆",
        [202] = "烬",
        [222] = "金克丝",
        [236] = "卢锡安",
        [238] = "劫",
        [245] = "艾克",
        [246] = "奇亚娜",
        [254] = "蔚",
        [350] = "悠米",
        [360] = "莎弥拉",
        [412] = "锤石",
        [497] = "洛",
        [517] = "塞拉斯",
        [523] = "厄斐琉斯",
        [555] = "派克",
        [875] = "瑟提"
    };

    public static string GetName(int championId) =>
        Names.TryGetValue(championId, out var name) ? name : $"#{championId}";
}
