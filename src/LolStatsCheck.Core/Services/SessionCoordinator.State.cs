using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed partial class SessionCoordinator
{
    private sealed class PlayerRuntime
    {
        public string Key { get; init; } = string.Empty;
        public int CellId { get; set; }
        public TeamSide Team { get; set; }
        public int SlotIndex { get; set; }
        public int ChampionId { get; set; }
        public int PickIntentChampionId { get; set; }
        public string Position { get; set; } = string.Empty;
        public bool IsAutofilled { get; set; }
        public bool IsLocalPlayer { get; set; }
        public bool IsVisible { get; set; }
        public string Puuid { get; set; } = string.Empty;
        public long SummonerId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? ChampionNameOverride { get; set; }
        public PlayerProfile? Profile { get; set; }
        public IReadOnlyList<MatchRecord> Matches { get; set; } = Array.Empty<MatchRecord>();
        public PlayerStats? Stats { get; set; }
        public RankInfo? Rank { get; set; }
        public PlayerCardStatus Status { get; set; } = PlayerCardStatus.Hidden;
        public string? Message { get; set; }
        public bool IsStale { get; set; }
        public bool CanRetry { get; set; }
        public bool Loading { get; set; }
        public int Version { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
        public int ResolveAttempts { get; set; }
        public bool ResolveInProgress { get; set; }
        public bool MockLoaded { get; set; }
        public DateTimeOffset MockReadyAt { get; set; }
        public int MockChampionId { get; set; }
    }

    private static string BuildKey(TeamSide team, int cellId, int slot) =>
        $"{(int)team}:{cellId}:{slot}";

    private List<(PlayerRuntime Runtime, int Version)> ApplyChampSelectPlayersLocked(
        IEnumerable<ChampSelectPlayer> players)
    {
        var starts = new List<(PlayerRuntime, int)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var player in players)
        {
            var key = BuildKey(player.Team, player.CellId, player.SlotIndex);
            seen.Add(key);

            var isNew = !_runtimes.TryGetValue(key, out var runtime);
            if (runtime is null)
            {
                runtime = new PlayerRuntime
                {
                    Key = key,
                    CellId = player.CellId,
                    Team = player.Team,
                    SlotIndex = player.SlotIndex,
                    Status = player.IsVisible ? PlayerCardStatus.Loading : PlayerCardStatus.Hidden,
                    Message = player.IsVisible ? null : "身份未公开",
                    UpdatedAt = DateTimeOffset.Now
                };
                _runtimes[key] = runtime;
            }

            runtime.ChampionNameOverride = null;
            if (ApplyPlayerLocked(runtime, player, forceIdentity: isNew))
            {
                starts.Add((runtime, runtime.Version));
            }
        }

        foreach (var stale in _runtimes.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _runtimes.Remove(stale);
        }

        return starts;
    }

    private bool ApplyPlayerLocked(
        PlayerRuntime runtime, ChampSelectPlayer player, bool forceIdentity = false)
    {
        var now = DateTimeOffset.Now;
        var newDisplayChampion = player.DisplayChampionId;
        var oldDisplayChampion = runtime.ChampionId > 0
            ? runtime.ChampionId
            : runtime.PickIntentChampionId;

        var identityChanged = forceIdentity ||
                              runtime.IsVisible != player.IsVisible ||
                              !string.Equals(runtime.Puuid, player.Puuid,
                                  StringComparison.OrdinalIgnoreCase) ||
                              runtime.SummonerId != player.SummonerId;

        runtime.CellId = player.CellId;
        runtime.Team = player.Team;
        runtime.SlotIndex = player.SlotIndex;
        runtime.Position = player.AssignedPosition;
        runtime.IsAutofilled = player.IsAutofilled;
        runtime.IsLocalPlayer = player.IsLocalPlayer;
        runtime.ChampionId = player.ChampionId;
        runtime.PickIntentChampionId = player.PickIntentChampionId;

        if (!player.IsVisible)
        {
            runtime.IsVisible = false;
            runtime.Puuid = string.Empty;
            runtime.SummonerId = 0;
            runtime.DisplayName = string.Empty;
            runtime.ChampionNameOverride = null;
            runtime.Profile = null;
            runtime.Matches = Array.Empty<MatchRecord>();
            runtime.Stats = null;
            runtime.Rank = null;
            runtime.IsStale = false;
            runtime.CanRetry = false;
            runtime.Loading = false;
            runtime.Status = PlayerCardStatus.Hidden;
            runtime.Message = "身份未公开";
            if (identityChanged)
            {
                runtime.Version++;
                runtime.UpdatedAt = now;
            }

            return false;
        }

        runtime.IsVisible = true;
        runtime.Puuid = player.Puuid;
        runtime.SummonerId = player.SummonerId;
        if (!string.IsNullOrWhiteSpace(player.DisplayName))
        {
            runtime.DisplayName = player.DisplayName;
        }
        else if (identityChanged)
        {
            runtime.DisplayName = string.Empty;
        }

        if (identityChanged)
        {
            runtime.Version++;
            runtime.Loading = false;
            runtime.Profile = null;
            runtime.Matches = Array.Empty<MatchRecord>();
            runtime.Stats = null;
            runtime.Rank = null;
            runtime.IsStale = false;
            runtime.Message = null;
            runtime.CanRetry = false;
            runtime.UpdatedAt = now;
            runtime.ResolveAttempts = 0;
            runtime.ResolveInProgress = false;

            if (ChampSelectParser.IsValidPuuid(player.Puuid))
            {
                runtime.Status = PlayerCardStatus.Loading;
                return true;
            }

            runtime.Status = PlayerCardStatus.Unavailable;
            runtime.Message = "身份信息不完整";
            runtime.CanRetry = false;
            return false;
        }

        if (oldDisplayChampion != newDisplayChampion)
        {
            runtime.UpdatedAt = now;
            RecomputeRuntimeStatsLocked(runtime);
        }

        return false;
    }

    private List<(PlayerRuntime Runtime, int Version)> BeginForceReloadLocked()
    {
        var starts = new List<(PlayerRuntime, int)>();
        var now = DateTimeOffset.Now;
        foreach (var runtime in _runtimes.Values)
        {
            if (!runtime.IsVisible || !ChampSelectParser.IsValidPuuid(runtime.Puuid))
            {
                continue;
            }

            runtime.Version++;
            runtime.Loading = false;
            runtime.Status = PlayerCardStatus.Loading;
            runtime.Message = null;
            runtime.CanRetry = false;
            runtime.UpdatedAt = now;
            starts.Add((runtime, runtime.Version));
        }

        return starts;
    }

    private List<(PlayerRuntime Runtime, int Version)> BeginHistoryRetryLocked()
    {
        var starts = new List<(PlayerRuntime, int)>();
        var now = DateTimeOffset.Now;
        if (now < _nextHistoryRetry)
        {
            return starts;
        }

        foreach (var runtime in _runtimes.Values)
        {
            if (!runtime.IsVisible || runtime.Loading ||
                !ChampSelectParser.IsValidPuuid(runtime.Puuid))
            {
                continue;
            }

            if (!runtime.CanRetry ||
                runtime.Status is not (PlayerCardStatus.Unavailable or PlayerCardStatus.Error))
            {
                continue;
            }

            runtime.Version++;
            runtime.Loading = true;
            runtime.UpdatedAt = now;
            starts.Add((runtime, runtime.Version));
        }

        if (starts.Count > 0)
        {
            _nextHistoryRetry = now.AddSeconds(25);
        }

        return starts;
    }

    private void StartPlayerLoad(
        PlayerRuntime runtime, int version, bool force, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        lock (_stateLock)
        {
            if (runtime.Version != version || !runtime.IsVisible)
            {
                return;
            }

            runtime.Loading = true;
        }

        _ = LoadPlayerAsync(runtime, version, force, cancellationToken);
    }

    private async Task LoadPlayerAsync(
        PlayerRuntime runtime, int version, bool force, CancellationToken cancellationToken)
    {
        string puuid;
        long summonerId;
        string fallbackName;
        bool showRank;
        lock (_stateLock)
        {
            if (runtime.Version != version || !runtime.IsVisible)
            {
                return;
            }

            puuid = runtime.Puuid;
            summonerId = runtime.SummonerId;
            fallbackName = runtime.DisplayName;
            showRank = _settings.Current.ShowRank;
        }

        if (!ChampSelectParser.IsValidPuuid(puuid))
        {
            return;
        }

        try
        {
            PlayerProfile? profile;
            HistoryFetchResult history;
            RankInfo? rank;

            await _playerLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var profileTask = _playerData.GetProfileAsync(
                    puuid, summonerId, fallbackName, force, cancellationToken);
                var historyTask = _playerData.GetHistoryAsync(puuid, force, cancellationToken);
                var rankTask = showRank
                    ? _playerData.GetRankAsync(puuid, force, cancellationToken)
                    : Task.FromResult<RankInfo?>(null);

                await Task.WhenAll(profileTask, historyTask, rankTask).ConfigureAwait(false);

                profile = await profileTask.ConfigureAwait(false);
                history = await historyTask.ConfigureAwait(false);
                rank = await rankTask.ConfigureAwait(false);
            }
            finally
            {
                _playerLoadGate.Release();
            }

            lock (_stateLock)
            {
                if (runtime.Version != version || !runtime.IsVisible)
                {
                    return;
                }

                runtime.Loading = false;
                runtime.Profile = profile;
                runtime.Rank = rank;
                runtime.Matches = history.Matches;
                runtime.IsStale = history.IsStale;
                runtime.UpdatedAt = DateTimeOffset.Now;

                if (profile is not null && !string.IsNullOrWhiteSpace(profile.ShortName) &&
                    !profile.ShortName.Equals("未知玩家", StringComparison.Ordinal))
                {
                    runtime.DisplayName = profile.ShortName;
                }

                RecomputeRuntimeStatsLocked(runtime);
                ApplyHistoryStatusLocked(runtime, history);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _log.Warn($"加载玩家数据失败（{ShortPuuid(puuid)}）：{ex.Message}");
            lock (_stateLock)
            {
                if (runtime.Version != version || !runtime.IsVisible)
                {
                    return;
                }

                runtime.Loading = false;
                runtime.Status = PlayerCardStatus.Error;
                runtime.Message = runtime.Matches.Count > 0
                    ? "刷新失败，已显示缓存数据"
                    : "战绩查询失败";
                runtime.CanRetry = true;
                runtime.UpdatedAt = DateTimeOffset.Now;
            }
        }

        EmitCurrent();
    }

    private void ApplyHistoryStatusLocked(PlayerRuntime runtime, HistoryFetchResult history)
    {
        switch (history.Status)
        {
            case LcuDataStatus.Ok:
                runtime.Status = runtime.Stats is { Games: 0 }
                    ? PlayerCardStatus.Unavailable
                    : PlayerCardStatus.Ready;
                runtime.Message = runtime.Stats is { Games: 0 }
                    ? "近期对局均为重开"
                    : runtime.Stats is { RemakesExcluded: > 0 } stats
                        ? $"已排除重开 {stats.RemakesExcluded} 场"
                        : null;
                runtime.CanRetry = runtime.Stats is { Games: 0 };
                break;
            case LcuDataStatus.NotFound:
                runtime.Status = PlayerCardStatus.Unavailable;
                runtime.Message = "暂无战绩";
                runtime.CanRetry = false;
                break;
            case LcuDataStatus.Forbidden:
                runtime.Status = PlayerCardStatus.Unavailable;
                runtime.Message = "战绩不可见";
                runtime.CanRetry = false;
                break;
            case LcuDataStatus.Unavailable:
                runtime.Status = PlayerCardStatus.Unavailable;
                runtime.Message = history.HasMatches
                    ? $"{history.Message ?? "战绩服务暂不可用"}（显示缓存）"
                    : history.Message ?? "战绩服务暂不可用";
                runtime.CanRetry = true;
                break;
            default:
                runtime.Status = PlayerCardStatus.Error;
                runtime.Message = history.HasMatches
                    ? "查询失败，已显示缓存数据"
                    : "战绩查询失败";
                runtime.CanRetry = true;
                break;
        }
    }

    private void RecomputeRuntimeStatsLocked(PlayerRuntime runtime)
    {
        if (runtime.Matches.Count == 0)
        {
            runtime.Stats = null;
            return;
        }

        var settings = _settings.Current;
        var championId = runtime.ChampionId > 0
            ? runtime.ChampionId
            : runtime.PickIntentChampionId;
        runtime.Stats = MatchStatsCalculator.Calculate(
            runtime.Matches,
            championId,
            _queueId,
            settings.RecentGames,
            settings.CurrentModeFirst,
            runtime.IsStale);
    }

    private static string ShortPuuid(string puuid) =>
        string.IsNullOrEmpty(puuid) ? "-" : puuid[..Math.Min(8, puuid.Length)];

    private async Task MockLoopAsync(CancellationToken cancellationToken)
    {
        const long mockGameId = 202610270001L;
        var tick = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var snapshot = MockData.CreateSnapshot(mockGameId, tick);
            lock (_stateLock)
            {
                _connection = ConnectionStatus.Connected;
                _phase = "ChampSelect";
                _sessionGameId = mockGameId;
                _queueId = MockData.MockQueueId;
                _modeName = "单双排";
                _sessionKey = "mock-session";
                _snapshot = snapshot;
                _postGameUntil = null;
                _banner = null;
                _liveStatus = string.Empty;
                EnsureMockRuntimesLocked(snapshot, now);
            }

            EmitCurrent();
            tick++;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureMockRuntimesLocked(ChampSelectSnapshot snapshot, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in snapshot.AllPlayers)
        {
            var key = BuildKey(player.Team, player.CellId, player.SlotIndex);
            seen.Add(key);

            if (!_runtimes.TryGetValue(key, out var runtime))
            {
                runtime = new PlayerRuntime
                {
                    Key = key,
                    CellId = player.CellId,
                    Team = player.Team,
                    SlotIndex = player.SlotIndex,
                    Status = player.IsVisible ? PlayerCardStatus.Loading : PlayerCardStatus.Hidden,
                    Message = player.IsVisible ? null : "身份未公开",
                    UpdatedAt = now
                };
                var definition = _mockDefinitions[(player.Team, player.SlotIndex)];
                runtime.MockReadyAt = _mockResetAt.AddMilliseconds(400 + definition.LoadDelayMs);
                _runtimes[key] = runtime;
            }

            var mock = _mockDefinitions[(player.Team, player.SlotIndex)];
            runtime.CellId = player.CellId;
            runtime.Team = player.Team;
            runtime.SlotIndex = player.SlotIndex;
            runtime.Position = player.AssignedPosition;
            runtime.IsAutofilled = player.IsAutofilled;
            runtime.IsLocalPlayer = player.IsLocalPlayer;
            runtime.ChampionNameOverride = null;

            var newDisplayChampion = player.DisplayChampionId;
            var oldDisplayChampion = runtime.ChampionId > 0
                ? runtime.ChampionId
                : runtime.PickIntentChampionId;
            runtime.ChampionId = player.ChampionId;
            runtime.PickIntentChampionId = player.PickIntentChampionId;
            runtime.IsVisible = player.IsVisible;

            if (!player.IsVisible)
            {
                runtime.DisplayName = string.Empty;
                runtime.Puuid = string.Empty;
                runtime.SummonerId = 0;
                runtime.Profile = null;
                runtime.Stats = null;
                runtime.Rank = null;
                runtime.Matches = Array.Empty<MatchRecord>();
                runtime.Status = PlayerCardStatus.Hidden;
                runtime.Message = "身份未公开";
                runtime.CanRetry = false;
                runtime.MockLoaded = false;
                runtime.UpdatedAt = now;
                continue;
            }

            runtime.DisplayName = mock.Name;
            runtime.Puuid = MockData.CreatePuuid(mock.Seed);
            runtime.SummonerId = 70_000_000_000 + mock.Seed;

            if (!runtime.MockLoaded)
            {
                if (now < runtime.MockReadyAt)
                {
                    runtime.Status = PlayerCardStatus.Loading;
                    runtime.Message = null;
                    continue;
                }

                runtime.MockLoaded = true;
                runtime.MockChampionId = runtime.ChampionId > 0 ? runtime.ChampionId : 0;
                runtime.Profile = MockData.CreateProfile(mock);
                runtime.Rank = MockData.CreateRank(mock);
                if (mock.NoHistory)
                {
                    runtime.Matches = Array.Empty<MatchRecord>();
                    runtime.Stats = null;
                    runtime.Status = PlayerCardStatus.Unavailable;
                    runtime.Message = "暂无战绩";
                    runtime.CanRetry = true;
                }
                else
                {
                    runtime.Matches = MockData.CreateMatches(mock, runtime.ChampionId, now);
                    RecomputeRuntimeStatsLocked(runtime);
                    runtime.Status = PlayerCardStatus.Ready;
                    runtime.Message = runtime.Stats is { RemakesExcluded: > 0 } stats
                        ? $"已排除重开 {stats.RemakesExcluded} 场"
                        : null;
                    runtime.CanRetry = false;
                }

                runtime.UpdatedAt = now;
                continue;
            }

            if (mock.NoHistory)
            {
                runtime.Stats = null;
                runtime.Status = PlayerCardStatus.Unavailable;
                runtime.Message = "暂无战绩";
                runtime.CanRetry = true;
                continue;
            }

            var currentChampion = runtime.ChampionId > 0 ? runtime.ChampionId : 0;
            if (oldDisplayChampion != newDisplayChampion || runtime.MockChampionId != currentChampion)
            {
                runtime.MockChampionId = currentChampion;
                runtime.Matches = MockData.CreateMatches(mock, runtime.ChampionId, now);
                RecomputeRuntimeStatsLocked(runtime);
                runtime.UpdatedAt = now;
            }
        }

        foreach (var stale in _runtimes.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _runtimes.Remove(stale);
        }
    }

    private void ResetMockLoading()
    {
        lock (_stateLock)
        {
            _mockResetAt = DateTimeOffset.Now;
            foreach (var runtime in _runtimes.Values)
            {
                if (!runtime.IsVisible)
                {
                    continue;
                }

                runtime.MockLoaded = false;
                runtime.MockReadyAt = _mockResetAt.AddMilliseconds(500);
                runtime.Status = PlayerCardStatus.Loading;
                runtime.Message = null;
                runtime.CanRetry = false;
                runtime.UpdatedAt = _mockResetAt;
            }
        }
    }

    private void RetryMockPlayer(string key)
    {
        lock (_stateLock)
        {
            if (!_runtimes.TryGetValue(key, out var runtime) || !runtime.IsVisible)
            {
                return;
            }

            runtime.MockLoaded = false;
            runtime.MockReadyAt = DateTimeOffset.Now.AddMilliseconds(700);
            runtime.Status = PlayerCardStatus.Loading;
            runtime.Message = null;
            runtime.CanRetry = false;
            runtime.UpdatedAt = DateTimeOffset.Now;
        }

        EmitCurrent();
    }
}
