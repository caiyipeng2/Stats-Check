using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed partial class SessionCoordinator
{
    private sealed record LiveResolveWork(
        PlayerRuntime Runtime,
        int Version,
        string GameName,
        string TagLine,
        string SummonerName);

    private sealed class LiveApplyResult
    {
        public List<(PlayerRuntime Runtime, int Version)> Starts { get; } = new();
        public List<LiveResolveWork> Resolves { get; } = new();
        public string Status { get; set; } = string.Empty;
    }

    private LiveApplyResult ApplyLiveSnapshotLocked(LiveGameSnapshot live)
    {
        var result = new LiveApplyResult();
        var myLiveTeam = ResolveMyLiveTeamLocked(live);
        if (myLiveTeam is null)
        {
            result.Status = "暂时无法确定本方阵营，等待更多对局数据…";
            return result;
        }

        var existing = _snapshot;
        var myTeam = existing?.MyTeam.ToList() ?? new List<ChampSelectPlayer>();
        var theirTeam = existing?.TheirTeam.ToList() ?? new List<ChampSelectPlayer>();
        EnsureFiveSlotsLocked(myTeam, TeamSide.MyTeam);
        EnsureFiveSlotsLocked(theirTeam, TeamSide.TheirTeam);

        var enemyLiveTeam = myLiveTeam.Equals("ORDER", StringComparison.OrdinalIgnoreCase)
            ? "CHAOS"
            : "ORDER";
        var assignments = new Dictionary<string, LivePlayer>(StringComparer.Ordinal);

        MapLiveSideLocked(myTeam, TeamSide.MyTeam, live, myLiveTeam, assignments);
        MapLiveSideLocked(theirTeam, TeamSide.TheirTeam, live, enemyLiveTeam, assignments);

        var localCellId = myTeam.FirstOrDefault(p => p.IsLocalPlayer)?.CellId ??
                          existing?.LocalPlayerCellId ?? -1;
        var joined = new ChampSelectSnapshot
        {
            GameId = _sessionGameId,
            QueueId = _queueId,
            LocalPlayerCellId = localCellId,
            Phase = "InProgress",
            MyTeam = myTeam,
            TheirTeam = theirTeam,
            SessionKey = existing?.SessionKey ?? _sessionKey
        };
        _snapshot = joined;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in joined.AllPlayers)
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

            if (ApplyPlayerLocked(runtime, player, forceIdentity: isNew))
            {
                result.Starts.Add((runtime, runtime.Version));
            }

            if (!assignments.TryGetValue(key, out var livePlayer))
            {
                continue;
            }

            runtime.ChampionNameOverride = string.IsNullOrWhiteSpace(livePlayer.ChampionAlias)
                ? null
                : livePlayer.ChampionAlias;
            runtime.IsVisible = true;
            var liveName = DisplayNameOf(livePlayer);
            if (string.IsNullOrWhiteSpace(runtime.DisplayName))
            {
                runtime.DisplayName = liveName;
            }

            if (livePlayer.IsBot)
            {
                runtime.Status = PlayerCardStatus.Unavailable;
                runtime.Message = "电脑玩家（无人机战绩）";
                runtime.CanRetry = false;
                runtime.Stats = null;
                runtime.UpdatedAt = DateTimeOffset.Now;
                continue;
            }

            if (ChampSelectParser.IsValidPuuid(runtime.Puuid))
            {
                continue;
            }

            if (runtime.ResolveInProgress || runtime.ResolveAttempts >= 2)
            {
                continue;
            }

            runtime.Status = PlayerCardStatus.Loading;
            runtime.Message = "正在解析玩家身份…";
            runtime.CanRetry = false;
            runtime.UpdatedAt = DateTimeOffset.Now;
            runtime.ResolveInProgress = true;
            runtime.ResolveAttempts++;
            result.Resolves.Add(new LiveResolveWork(
                runtime,
                runtime.Version,
                livePlayer.GameName,
                livePlayer.TagLine,
                livePlayer.SummonerName));
        }

        foreach (var stale in _runtimes.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _runtimes.Remove(stale);
        }

        var mapped = assignments.Count;
        var queryable = assignments.Keys.Count(key =>
            _runtimes.TryGetValue(key, out var runtime) &&
            runtime.IsVisible &&
            ChampSelectParser.IsValidPuuid(runtime.Puuid));
        var bots = assignments.Values.Count(player => player.IsBot);
        var pending = result.Resolves.Count;
        if (mapped == 0)
        {
            result.Status = "已读取对局数据，正在匹配双方玩家…";
        }
        else
        {
            var suffix = new List<string>();
            if (bots > 0)
            {
                suffix.Add($"{bots} 名电脑玩家");
            }

            if (pending > 0)
            {
                suffix.Add($"正在解析 {pending} 个身份");
            }

            result.Status = $"本地对局已读取：{mapped} 名玩家，{queryable} 名可查询" +
                            (suffix.Count > 0 ? "，" + string.Join("，", suffix) : string.Empty);
        }

        return result;
    }

    private string? ResolveMyLiveTeamLocked(LiveGameSnapshot live)
    {
        if (!string.IsNullOrWhiteSpace(live.ActiveTeam))
        {
            return live.ActiveTeam.ToUpperInvariant();
        }

        var active = live.Players.FirstOrDefault(player => IsActivePlayer(live, player));
        if (active is not null && !string.IsNullOrWhiteSpace(active.Team))
        {
            return active.Team.ToUpperInvariant();
        }

        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return null;
        }

        var myChampions = snapshot.MyTeam
            .Select(player => player.DisplayChampionId)
            .Where(id => id > 0)
            .ToHashSet();
        if (myChampions.Count == 0)
        {
            return null;
        }

        var orderScore = live.Players.Count(player =>
            TeamEquals(player.Team, "ORDER") && myChampions.Contains(FindChampionIdForLive(player)));
        var chaosScore = live.Players.Count(player =>
            TeamEquals(player.Team, "CHAOS") && myChampions.Contains(FindChampionIdForLive(player)));

        if (orderScore == 0 && chaosScore == 0)
        {
            return null;
        }

        return orderScore >= chaosScore ? "ORDER" : "CHAOS";
    }

    private void MapLiveSideLocked(
        List<ChampSelectPlayer> slots,
        TeamSide side,
        LiveGameSnapshot live,
        string liveTeam,
        Dictionary<string, LivePlayer> assignments)
    {
        var used = new HashSet<int>();
        var candidates = live.Players
            .Where(player => TeamEquals(player.Team, liveTeam))
            .ToList();

        foreach (var livePlayer in candidates)
        {
            var championId = FindChampionIdForLive(livePlayer);
            var index = FindSlotForLiveLocked(slots, used, livePlayer, championId);
            if (index < 0)
            {
                continue;
            }

            used.Add(index);
            var original = slots[index];
            var key = BuildKey(original.Team, original.CellId, original.SlotIndex);
            _runtimes.TryGetValue(key, out var runtime);

            var puuid = runtime is { IsVisible: true } &&
                        ChampSelectParser.IsValidPuuid(runtime.Puuid)
                ? runtime.Puuid
                : string.Empty;
            var summonerId = runtime?.SummonerId ?? 0;
            var displayName = !string.IsNullOrWhiteSpace(runtime?.DisplayName)
                ? runtime.DisplayName
                : DisplayNameOf(livePlayer);
            var isLocal = (side == TeamSide.MyTeam && IsActivePlayer(live, livePlayer)) ||
                          original.IsLocalPlayer;

            slots[index] = new ChampSelectPlayer
            {
                CellId = original.CellId,
                Team = original.Team,
                SlotIndex = original.SlotIndex,
                ChampionId = championId > 0 ? championId : original.ChampionId,
                PickIntentChampionId = 0,
                AssignedPosition = original.AssignedPosition,
                SummonerId = summonerId,
                Puuid = puuid,
                DisplayName = displayName,
                IsVisible = true,
                IsAutofilled = original.IsAutofilled,
                IsLocalPlayer = isLocal
            };
            assignments[key] = livePlayer;
        }
    }

    private int FindSlotForLiveLocked(
        List<ChampSelectPlayer> slots, HashSet<int> used, LivePlayer livePlayer, int championId)
    {
        var liveName = DisplayNameOf(livePlayer);
        for (var index = 0; index < slots.Count; index++)
        {
            if (used.Contains(index))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(liveName) &&
                string.Equals(slots[index].DisplayName, liveName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        if (championId > 0)
        {
            for (var index = 0; index < slots.Count; index++)
            {
                if (!used.Contains(index) && slots[index].DisplayChampionId == championId)
                {
                    return index;
                }
            }
        }

        for (var index = 0; index < slots.Count; index++)
        {
            if (used.Contains(index))
            {
                continue;
            }

            if (!slots[index].IsVisible ||
                !ChampSelectParser.IsValidPuuid(slots[index].Puuid))
            {
                return index;
            }
        }

        for (var index = 0; index < slots.Count; index++)
        {
            if (!used.Contains(index))
            {
                return index;
            }
        }

        return -1;
    }

    private void EnsureFiveSlotsLocked(List<ChampSelectPlayer> slots, TeamSide team)
    {
        var existingSlots = slots.Select(player => player.SlotIndex).ToHashSet();
        var nextCell = slots.Count == 0
            ? (team == TeamSide.MyTeam ? 0 : 5)
            : slots.Max(player => player.CellId) + 1;

        for (var slot = 0; slot < 5; slot++)
        {
            if (existingSlots.Contains(slot))
            {
                continue;
            }

            slots.Add(new ChampSelectPlayer
            {
                CellId = nextCell++,
                Team = team,
                SlotIndex = slot,
                IsVisible = false
            });
        }
    }

    private int FindChampionIdForLive(LivePlayer player)
    {
        if (string.IsNullOrWhiteSpace(player.ChampionAlias))
        {
            return 0;
        }

        return _champions.FindChampionId(player.ChampionAlias);
    }

    private static bool TeamEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsActivePlayer(LiveGameSnapshot live, LivePlayer player)
    {
        if (!string.IsNullOrWhiteSpace(live.ActiveRiotId))
        {
            if (player.RiotId.Equals(live.ActiveRiotId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(player.GameName) &&
                !string.IsNullOrWhiteSpace(player.TagLine))
            {
                var riotId = $"{player.GameName}#{player.TagLine}";
                if (riotId.Equals(live.ActiveRiotId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(live.ActiveSummonerName) &&
               player.SummonerName.Equals(live.ActiveSummonerName, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayNameOf(LivePlayer player)
    {
        if (!string.IsNullOrWhiteSpace(player.GameName))
        {
            return player.GameName;
        }

        if (!string.IsNullOrWhiteSpace(player.SummonerName))
        {
            return player.SummonerName;
        }

        return string.IsNullOrWhiteSpace(player.RiotId) ? "玩家" : player.RiotId;
    }

    private void StartLiveResolve(LiveResolveWork work, CancellationToken cancellationToken)
    {
        _ = ResolveLiveIdentityAsync(work, cancellationToken);
    }

    private async Task ResolveLiveIdentityAsync(
        LiveResolveWork work, CancellationToken cancellationToken)
    {
        PlayerProfile? profile = null;
        try
        {
            await _liveResolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                profile = await _resolver.ResolveAsync(
                    work.GameName, work.TagLine, work.SummonerName, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _liveResolveGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is LcuException or JsonException)
        {
            _log.Debug("解析对局玩家身份失败：" + ex.Message);
        }

        (PlayerRuntime Runtime, int Version)? toLoad = null;
        lock (_stateLock)
        {
            if (!_runtimes.TryGetValue(work.Runtime.Key, out var current) ||
                !ReferenceEquals(current, work.Runtime))
            {
                return;
            }

            if (current.Version != work.Version)
            {
                current.ResolveInProgress = false;
                return;
            }

            current.ResolveInProgress = false;
            current.UpdatedAt = DateTimeOffset.Now;

            if (profile is not null && ChampSelectParser.IsValidPuuid(profile.Puuid))
            {
                current.Puuid = profile.Puuid;
                current.SummonerId = profile.SummonerId;
                current.Profile = profile;
                current.IsVisible = true;
                current.ResolveAttempts = 0;
                if (!string.IsNullOrWhiteSpace(profile.ShortName) &&
                    !profile.ShortName.Equals("未知玩家", StringComparison.Ordinal))
                {
                    current.DisplayName = profile.ShortName;
                }

                current.Status = PlayerCardStatus.Loading;
                current.Message = null;
                current.CanRetry = false;
                current.Version++;
                toLoad = (current, current.Version);
                _log.Debug($"已解析对局玩家身份：{ShortPuuid(profile.Puuid)}");
            }
            else
            {
                current.Status = PlayerCardStatus.Unavailable;
                current.Message = current.ResolveAttempts >= 2
                    ? "未能查询战绩（身份解析失败），可重试"
                    : "身份解析失败，稍后自动重试…";
                current.CanRetry = true;
            }
        }

        if (toLoad is { } load)
        {
            StartPlayerLoad(load.Runtime, load.Version, force: false, cancellationToken);
        }

        EmitCurrent();
    }
}
