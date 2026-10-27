namespace LolStatsCheck.Core.Models;

public sealed record PlayerProfile(
    string Puuid,
    long SummonerId,
    string DisplayName,
    string GameName,
    string TagLine,
    int ProfileIconId,
    int SummonerLevel)
{
    public string ShortName =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName :
        !string.IsNullOrWhiteSpace(GameName) ? GameName :
        "未知玩家";
}

public sealed class RankInfo
{
    public string QueueType { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
    public string TierCn { get; init; } = string.Empty;
    public string Division { get; init; } = string.Empty;
    public int LeaguePoints { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }

    public int TotalGames => Wins + Losses;
    public string ColorKey => Tier.ToLowerInvariant();

    public string DisplayText
    {
        get
        {
            if (string.IsNullOrEmpty(Tier) || Tier.Equals("UNRANKED", StringComparison.OrdinalIgnoreCase))
            {
                return "未定级";
            }

            var showDivision = Tier is not ("MASTER" or "GRANDMASTER" or "CHALLENGER");
            var division = showDivision && !string.IsNullOrEmpty(Division) ? " " + Division : string.Empty;
            return $"{TierCn}{division} {LeaguePoints}LP";
        }
    }

    public string QueueLabel => QueueType switch
    {
        "RANKED_SOLO_5x5" => "单双排",
        "RANKED_FLEX_SR" => "灵活排位",
        _ => "排位"
    };
}

public static class RankTierNames
{
    public static string ToChinese(string tier) => tier.ToUpperInvariant() switch
    {
        "IRON" => "黑铁",
        "BRONZE" => "青铜",
        "SILVER" => "白银",
        "GOLD" => "黄金",
        "PLATINUM" => "铂金",
        "EMERALD" => "翡翠",
        "DIAMOND" => "钻石",
        "MASTER" => "大师",
        "GRANDMASTER" => "宗师",
        "CHALLENGER" => "王者",
        _ => "未定级"
    };
}
