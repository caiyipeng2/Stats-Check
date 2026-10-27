using System.Text.Json;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Lcu;

public static class SummonerParser
{
    public static PlayerProfile? Parse(string json, string fallbackPuuid, long fallbackSummonerId, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
            {
                return null;
            }

            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var puuid = root.GetString("puuid") ?? fallbackPuuid;
        var summonerId = root.GetInt64("summonerId", fallbackSummonerId);
        var displayName = root.GetString("displayName") ?? string.Empty;
        var gameName = root.GetString("gameName") ?? string.Empty;
        var tagLine = root.GetString("tagLine") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = !string.IsNullOrWhiteSpace(gameName)
                ? gameName
                : fallbackName;
        }

        if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(puuid))
        {
            displayName = "玩家 " + puuid[..Math.Min(6, puuid.Length)];
        }

        return new PlayerProfile(
            puuid,
            summonerId,
            displayName,
            gameName,
            tagLine,
            root.GetInt32("profileIconId"),
            root.GetInt32("summonerLevel"));
    }
}

public static class RankedStatsParser
{
    private static readonly string[] PreferredQueues =
    {
        "RANKED_SOLO_5x5",
        "RANKED_FLEX_SR"
    };

    public static RankInfo? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("queueMap", out var queueMap) || queueMap.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var queueType in PreferredQueues)
        {
            if (!queueMap.TryGetProperty(queueType, out var queue) ||
                queue.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tier = queue.GetString("tier") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tier) ||
                tier.Equals("UNRANKED", StringComparison.OrdinalIgnoreCase) ||
                tier.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new RankInfo
            {
                QueueType = queueType,
                Tier = tier.ToUpperInvariant(),
                TierCn = RankTierNames.ToChinese(tier),
                Division = queue.GetString("division") ?? string.Empty,
                LeaguePoints = queue.GetInt32("leaguePoints"),
                Wins = queue.GetInt32("wins"),
                Losses = queue.GetInt32("losses")
            };
        }

        return null;
    }
}
