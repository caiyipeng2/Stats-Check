using System.Text.Json;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Lcu;

public static class MatchHistoryParser
{
    public static MatchHistoryResult Parse(string json, string puuid)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(puuid))
        {
            return new MatchHistoryResult(Array.Empty<MatchRecord>(), 0);
        }

        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;

        var games = FindGamesArray(root, out var totalAvailable);
        if (games is null)
        {
            return new MatchHistoryResult(Array.Empty<MatchRecord>(), 0);
        }

        var matches = new List<MatchRecord>();
        foreach (var game in games.Value.EnumerateArray())
        {
            var record = ParseGame(game, puuid);
            if (record is not null)
            {
                matches.Add(record);
            }
        }

        return new MatchHistoryResult(matches, Math.Max(totalAvailable, matches.Count));
    }

    private static JsonElement? FindGamesArray(JsonElement root, out int totalAvailable)
    {
        totalAvailable = 0;

        if (root.ValueKind == JsonValueKind.Array)
        {
            totalAvailable = root.GetArrayLength();
            return root;
        }

        if (!root.TryGetProperty("games", out var gamesNode))
        {
            return null;
        }

        if (gamesNode.ValueKind == JsonValueKind.Array)
        {
            totalAvailable = gamesNode.GetArrayLength();
            return gamesNode;
        }

        if (gamesNode.ValueKind == JsonValueKind.Object)
        {
            totalAvailable = gamesNode.GetInt32("gameCount");
            if (gamesNode.TryGetProperty("games", out var inner) && inner.ValueKind == JsonValueKind.Array)
            {
                if (totalAvailable <= 0)
                {
                    totalAvailable = inner.GetArrayLength();
                }

                return inner;
            }
        }

        return null;
    }

    private static MatchRecord? ParseGame(JsonElement game, string puuid)
    {
        var participantId = FindParticipantId(game, puuid);
        if (participantId < 0)
        {
            return null;
        }

        JsonElement participant = default;
        var foundParticipant = false;
        if (game.TryGetProperty("participants", out var participants) &&
            participants.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in participants.EnumerateArray())
            {
                if (item.GetInt32("participantId") == participantId)
                {
                    participant = item;
                    foundParticipant = true;
                    break;
                }
            }
        }

        if (!foundParticipant)
        {
            return null;
        }

        var championId = participant.GetInt32("championId");
        if (championId <= 0)
        {
            return null;
        }

        var stats = participant.TryGetProperty("stats", out var statsElement) ? statsElement : default;
        if (stats.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var creation = ParseCreation(game);
        return new MatchRecord(
            game.GetInt64("gameId"),
            game.GetInt32("queueId"),
            creation,
            game.GetInt32("gameDuration"),
            championId,
            stats.GetBool("win"),
            stats.GetInt32("kills"),
            stats.GetInt32("deaths"),
            stats.GetInt32("assists"),
            game.GetString("gameType") ?? string.Empty,
            game.GetString("gameMode") ?? string.Empty);
    }

    private static int FindParticipantId(JsonElement game, string puuid)
    {
        if (!game.TryGetProperty("participantIdentities", out var identities) ||
            identities.ValueKind != JsonValueKind.Array)
        {
            return -1;
        }

        foreach (var identity in identities.EnumerateArray())
        {
            if (!identity.TryGetProperty("player", out var player) ||
                player.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var identityPuuid = player.GetString("puuid");
            if (!string.IsNullOrEmpty(identityPuuid) &&
                identityPuuid.Equals(puuid, StringComparison.OrdinalIgnoreCase))
            {
                return identity.GetInt32("participantId", -1);
            }
        }

        return -1;
    }

    private static DateTimeOffset ParseCreation(JsonElement game)
    {
        var milliseconds = game.GetInt64("gameCreation");
        if (milliseconds > 0)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        return game.GetDate("gameCreationDate") ?? DateTimeOffset.MinValue;
    }
}
