namespace LolStatsCheck.Core.Models;

public sealed class ChampionAggregate
{
    public int ChampionId { get; init; }
    public int Games { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public DateTimeOffset LastPlayed { get; init; }

    public double WinRate => Games > 0 ? Wins * 100.0 / Games : 0;
    public double Kda => (Kills + Assists) / (double)Math.Max(1, Deaths);
}

public sealed class PlayerStats
{
    public int Games { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public double WinRate => Games > 0 ? Wins * 100.0 / Games : 0;
    public double Kda => (Kills + Assists) / (double)Math.Max(1, Deaths);
    public double AvgKills => Games > 0 ? Kills / (double)Games : 0;
    public double AvgDeaths => Games > 0 ? Deaths / (double)Games : 0;
    public double AvgAssists => Games > 0 ? Assists / (double)Games : 0;

    public IReadOnlyList<ChampionAggregate> RecentChampions { get; init; } = Array.Empty<ChampionAggregate>();
    public ChampionAggregate? CurrentChampion { get; init; }
    public int WinStreak { get; init; }
    public int LossStreak { get; init; }
    public bool FilteredByQueue { get; init; }
    public int ConsideredMatches { get; init; }
    public int RemakesExcluded { get; init; }
    public int PoolAvailable { get; init; }
    public DateTimeOffset FetchedAt { get; init; }
    public bool IsStale { get; init; }

    public bool HasGames => Games > 0;
}
