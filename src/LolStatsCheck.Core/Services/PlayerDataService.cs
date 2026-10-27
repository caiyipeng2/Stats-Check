using System.Collections.Concurrent;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed record HistoryFetchResult(
    IReadOnlyList<MatchRecord> Matches,
    int TotalAvailable,
    LcuDataStatus Status,
    string? Message,
    bool IsStale)
{
    public bool HasMatches => Matches.Count > 0;
}

public sealed class PlayerDataService
{
    private const int FetchCount = 50;
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EmptyHistoryTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RankTtl = TimeSpan.FromMinutes(10);

    private readonly Func<LcuClient?> _clientProvider;
    private readonly LogService _log;
    private readonly SemaphoreSlim _gate = new(3, 3);
    private readonly ConcurrentDictionary<string, HistoryCacheEntry> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProfileCacheEntry> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RankCacheEntry> _ranks = new(StringComparer.OrdinalIgnoreCase);

    public PlayerDataService(Func<LcuClient?> clientProvider, LogService log)
    {
        _clientProvider = clientProvider;
        _log = log;
    }

    public async Task<HistoryFetchResult> GetHistoryAsync(
        string puuid, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!ChampSelectParser.IsValidPuuid(puuid))
        {
            return new HistoryFetchResult(Array.Empty<MatchRecord>(), 0,
                LcuDataStatus.NotFound, "无效的玩家标识", false);
        }

        _history.TryGetValue(puuid, out var cached);
        if (!forceRefresh && cached is not null && cached.ExpiresAt > DateTimeOffset.Now)
        {
            return new HistoryFetchResult(cached.Matches, cached.TotalAvailable,
                cached.Matches.Count > 0 ? LcuDataStatus.Ok : LcuDataStatus.NotFound,
                cached.Matches.Count > 0 ? null : "暂无战绩", false);
        }

        var client = _clientProvider();
        if (client is null)
        {
            return FromError(cached, LcuDataStatus.Error, "客户端未连接");
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var path = $"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches" +
                           $"?begIndex=0&endIndex={FetchCount - 1}";
                var json = await client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);
                var parsed = MatchHistoryParser.Parse(json, puuid);
                var entry = new HistoryCacheEntry
                {
                    Matches = parsed.Matches,
                    TotalAvailable = parsed.TotalAvailable,
                    ExpiresAt = DateTimeOffset.Now.Add(
                        parsed.Matches.Count > 0 ? HistoryTtl : EmptyHistoryTtl)
                };
                _history[puuid] = entry;

                return new HistoryFetchResult(parsed.Matches, parsed.TotalAvailable,
                    parsed.Matches.Count > 0 ? LcuDataStatus.Ok : LcuDataStatus.NotFound,
                    parsed.Matches.Count > 0 ? null : "暂无战绩", false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (LcuHttpException ex) when (ex.StatusCode is 403 or 404)
        {
            _log.Debug($"战绩接口 {(ex.StatusCode == 403 ? "不可见" : "无数据")}：{Short(puuid)}");
            return new HistoryFetchResult(Array.Empty<MatchRecord>(), 0,
                ex.StatusCode == 403 ? LcuDataStatus.Forbidden : LcuDataStatus.NotFound,
                ex.StatusCode == 403 ? "战绩不可见" : "暂无战绩", false);
        }
        catch (LcuTimeoutException)
        {
            _log.Warn($"查询战绩超时：{Short(puuid)}");
            return FromError(cached, LcuDataStatus.Unavailable, "战绩服务暂不可用");
        }
        catch (LcuConnectionException ex)
        {
            _log.Warn("查询战绩失败（连接）：" + ex.Message);
            return FromError(cached, LcuDataStatus.Unavailable, "战绩服务暂不可用");
        }
        catch (LcuException ex)
        {
            _log.Warn("查询战绩失败：" + ex.Message);
            return FromError(cached, LcuDataStatus.Error, "战绩查询失败");
        }
    }

    public async Task<PlayerProfile?> GetProfileAsync(
        string puuid, long summonerId, string fallbackName, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrEmpty(puuid) ? $"sid:{summonerId}" : puuid;
        ProfileCacheEntry? cached = null;
        if (!forceRefresh && _profiles.TryGetValue(key, out cached) && cached.ExpiresAt > DateTimeOffset.Now)
        {
            return cached.Profile;
        }

        var client = _clientProvider();
        if (client is null)
        {
            return cached?.Profile;
        }

        var json = (string?)null;
        try
        {
            if (summonerId > 0)
            {
                json = await client.TryGetStringAsync(
                    $"/lol-summoner/v1/summoners/{summonerId}", cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(json) && ChampSelectParser.IsValidPuuid(puuid))
            {
                json = await client.TryGetStringAsync(
                    $"/lol-summoner/v2/summoners/puuid/{Uri.EscapeDataString(puuid)}",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is LcuException)
        {
            _log.Debug("查询玩家资料失败：" + ex.Message);
            return cached?.Profile;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return cached?.Profile;
        }

        try
        {
            var profile = SummonerParser.Parse(json, puuid, summonerId, fallbackName);
            if (profile is not null)
            {
                _profiles[key] = new ProfileCacheEntry
                {
                    Profile = profile,
                    ExpiresAt = DateTimeOffset.Now.Add(ProfileTtl)
                };
            }

            return profile ?? cached?.Profile;
        }
        catch (JsonException)
        {
            return cached?.Profile;
        }
    }

    public async Task<RankInfo?> GetRankAsync(
        string puuid, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!ChampSelectParser.IsValidPuuid(puuid))
        {
            return null;
        }

        RankCacheEntry? cached = null;
        if (!forceRefresh && _ranks.TryGetValue(puuid, out cached) && cached.ExpiresAt > DateTimeOffset.Now)
        {
            return cached.Rank;
        }

        var client = _clientProvider();
        if (client is null)
        {
            return cached?.Rank;
        }

        try
        {
            var json = await client.TryGetStringAsync(
                $"/lol-ranked/v1/ranked-stats/{Uri.EscapeDataString(puuid)}",
                cancellationToken).ConfigureAwait(false);
            var rank = json is null ? null : RankedStatsParser.Parse(json);
            _ranks[puuid] = new RankCacheEntry
            {
                Rank = rank,
                ExpiresAt = DateTimeOffset.Now.Add(RankTtl)
            };
            return rank;
        }
        catch (Exception ex) when (ex is LcuException or JsonException)
        {
            _log.Debug("查询段位失败：" + ex.Message);
            return cached?.Rank;
        }
    }

    public void Invalidate(string puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid))
        {
            return;
        }

        _history.TryRemove(puuid, out _);
        _profiles.TryRemove(puuid, out _);
        _ranks.TryRemove(puuid, out _);
    }

    public void ClearAll()
    {
        _history.Clear();
        _profiles.Clear();
        _ranks.Clear();
    }

    private static HistoryFetchResult FromError(
        HistoryCacheEntry? cached, LcuDataStatus status, string message)
    {
        if (cached is not null)
        {
            return new HistoryFetchResult(cached.Matches, cached.TotalAvailable,
                status, message + "，已显示缓存数据", true);
        }

        return new HistoryFetchResult(Array.Empty<MatchRecord>(), 0, status, message, false);
    }

    private static string Short(string puuid) =>
        string.IsNullOrEmpty(puuid) ? "-" : puuid[..Math.Min(8, puuid.Length)];

    private sealed class HistoryCacheEntry
    {
        public required IReadOnlyList<MatchRecord> Matches { get; init; }
        public int TotalAvailable { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class ProfileCacheEntry
    {
        public required PlayerProfile Profile { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class RankCacheEntry
    {
        public RankInfo? Rank { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
