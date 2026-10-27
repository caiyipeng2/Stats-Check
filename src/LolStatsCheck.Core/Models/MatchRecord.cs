namespace LolStatsCheck.Core.Models;

public sealed record MatchRecord(
    long GameId,
    int QueueId,
    DateTimeOffset GameCreation,
    int DurationSeconds,
    int ChampionId,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    string GameType,
    string GameMode)
{
    public bool IsRemake => DurationSeconds > 0 && DurationSeconds < 300;

    public bool IsTutorial =>
        GameType.Contains("TUTORIAL", StringComparison.OrdinalIgnoreCase);

    public double Kda => (Kills + Assists) / (double)Math.Max(1, Deaths);
}

public sealed record MatchHistoryResult(
    IReadOnlyList<MatchRecord> Matches,
    int TotalAvailable);
