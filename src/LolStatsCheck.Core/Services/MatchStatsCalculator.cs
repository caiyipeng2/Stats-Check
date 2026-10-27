using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public static class MatchStatsCalculator
{
    public static PlayerStats Calculate(
        IReadOnlyList<MatchRecord> allMatches,
        int championId,
        int queueId,
        int recentGames,
        bool currentModeFirst,
        bool isStale = false,
        DateTimeOffset? now = null)
    {
        var fetchedAt = now ?? DateTimeOffset.Now;
        recentGames = Math.Clamp(recentGames, 1, 100);

        var deduped = allMatches
            .Where(m => m.GameId != 0 || m.GameCreation != DateTimeOffset.MinValue)
            .GroupBy(m => m.GameId != 0 ? m.GameId : m.GameCreation.ToUnixTimeMilliseconds())
            .Select(group => group.OrderByDescending(m => m.GameCreation).First())
            .Where(m => !m.IsTutorial)
            .ToList();

        var remakesExcluded = deduped.Count(m => m.IsRemake);
        var pool = deduped
            .Where(m => !m.IsRemake)
            .OrderByDescending(m => m.GameCreation)
            .ToList();

        var filteredByQueue = false;
        IReadOnlyList<MatchRecord> analyzed = pool;
        if (currentModeFirst && queueId > 0)
        {
            var sameQueue = pool.Where(m => m.QueueId == queueId).ToList();
            if (sameQueue.Count >= Math.Min(5, recentGames))
            {
                analyzed = sameQueue;
                filteredByQueue = true;
            }
        }

        analyzed = analyzed.Take(recentGames).ToList();

        var games = analyzed.Count;
        var wins = analyzed.Count(m => m.Win);
        var kills = analyzed.Sum(m => m.Kills);
        var deaths = analyzed.Sum(m => m.Deaths);
        var assists = analyzed.Sum(m => m.Assists);

        var aggregates = analyzed
            .GroupBy(m => m.ChampionId)
            .Select(group =>
            {
                var items = group.ToList();
                return new ChampionAggregate
                {
                    ChampionId = group.Key,
                    Games = items.Count,
                    Wins = items.Count(m => m.Win),
                    Losses = items.Count(m => !m.Win),
                    Kills = items.Sum(m => m.Kills),
                    Deaths = items.Sum(m => m.Deaths),
                    Assists = items.Sum(m => m.Assists),
                    LastPlayed = items.Max(m => m.GameCreation)
                };
            })
            .OrderByDescending(a => a.LastPlayed)
            .ToList();

        var currentChampion = championId > 0
            ? aggregates.FirstOrDefault(a => a.ChampionId == championId)
            : null;

        var streak = 0;
        if (analyzed.Count > 0)
        {
            var firstWin = analyzed[0].Win;
            foreach (var match in analyzed)
            {
                if (match.Win != firstWin)
                {
                    break;
                }

                streak++;
            }

            if (streak < 3)
            {
                streak = 0;
            }
            else if (!firstWin)
            {
                streak = -streak;
            }
        }

        return new PlayerStats
        {
            Games = games,
            Wins = wins,
            Losses = games - wins,
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
            RecentChampions = aggregates.Take(4).ToList(),
            CurrentChampion = currentChampion,
            WinStreak = streak > 0 ? streak : 0,
            LossStreak = streak < 0 ? -streak : 0,
            FilteredByQueue = filteredByQueue,
            ConsideredMatches = games,
            RemakesExcluded = remakesExcluded,
            PoolAvailable = pool.Count,
            FetchedAt = fetchedAt,
            IsStale = isStale
        };
    }
}
