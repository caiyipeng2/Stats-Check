using System.Text.Json;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Lcu;

public static class ChampSelectParser
{
    public static ChampSelectSnapshot Parse(string json, long gameId, int queueId)
    {
        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;

        var localCellId = root.GetInt32("localPlayerCellId");
        if (gameId <= 0)
        {
            gameId = root.GetInt64("gameId");
        }

        if (queueId <= 0)
        {
            queueId = root.GetInt32("queueId");
        }

        var myTeam = ParseTeam(root, "myTeam", TeamSide.MyTeam, localCellId);
        var theirTeam = ParseTeam(root, "theirTeam", TeamSide.TheirTeam, localCellId);

        return new ChampSelectSnapshot
        {
            GameId = gameId,
            QueueId = queueId,
            LocalPlayerCellId = localCellId,
            Phase = "ChampSelect",
            MyTeam = myTeam,
            TheirTeam = theirTeam,
            SessionKey = BuildSessionKey(gameId, queueId, myTeam, theirTeam)
        };
    }

    private static List<ChampSelectPlayer> ParseTeam(
        JsonElement root, string propertyName, TeamSide team, int localCellId)
    {
        var players = new List<ChampSelectPlayer>();
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return players;
        }

        var slot = 0;
        foreach (var item in array.EnumerateArray())
        {
            var cellId = item.GetInt32("cellId");
            var visibility = item.GetString("nameVisibilityType");
            var puuid = item.GetString("puuid") ?? string.Empty;
            var summonerId = item.GetInt64("summonerId");

            var isVisible = visibility is not null
                ? visibility.Equals("VISIBLE", StringComparison.OrdinalIgnoreCase)
                : IsValidPuuid(puuid) && summonerId > 0;

            if (!isVisible)
            {
                puuid = string.Empty;
                summonerId = 0;
            }

            var displayName = item.GetString("displayName") ?? string.Empty;
            if (!isVisible)
            {
                displayName = string.Empty;
            }

            players.Add(new ChampSelectPlayer
            {
                CellId = cellId,
                Team = team,
                SlotIndex = slot++,
                ChampionId = item.GetInt32("championId"),
                PickIntentChampionId = item.GetInt32("championPickIntent"),
                AssignedPosition = item.GetString("assignedPosition") ?? string.Empty,
                SummonerId = summonerId,
                Puuid = puuid,
                DisplayName = displayName,
                IsVisible = isVisible,
                IsAutofilled = item.GetBool("isAutofilled"),
                IsLocalPlayer = team == TeamSide.MyTeam && cellId == localCellId
            });
        }

        return players;
    }

    public static bool IsValidPuuid(string? puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid))
        {
            return false;
        }

        return Guid.TryParse(puuid, out var guid) && guid != Guid.Empty;
    }

    private static string BuildSessionKey(
        long gameId, int queueId, List<ChampSelectPlayer> myTeam, List<ChampSelectPlayer> theirTeam)
    {
        var players = myTeam.Concat(theirTeam)
            .Select(p => $"{p.Team}:{p.CellId}:{p.IsVisible}:{p.Puuid}");
        return $"{gameId}|{queueId}|" + string.Join("|", players);
    }
}

public static class GameflowParser
{
    public static string ParsePhase(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            return root.GetString() ?? string.Empty;
        }

        return root.GetString("phase") ?? string.Empty;
    }

    public static (long GameId, int QueueId) ParseSession(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (0, 0);
        }

        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;
        var gameData = root.TryGetProperty("gameData", out var gd) ? gd : default;
        if (gameData.ValueKind != JsonValueKind.Object)
        {
            return (0, 0);
        }

        var gameId = gameData.GetInt64("gameId");
        var queueId = 0;
        if (gameData.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.Object)
        {
            queueId = queue.GetInt32("id");
        }

        if (queueId == 0)
        {
            queueId = gameData.GetInt32("queueId");
        }

        return (gameId, queueId);
    }

    public static int ParseLobbyQueueId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonHelpers.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("gameConfig", out var config) &&
                config.ValueKind == JsonValueKind.Object)
            {
                var queueId = config.GetInt32("queueId");
                if (queueId > 0)
                {
                    return queueId;
                }

                if (config.TryGetProperty("queue", out var queue) &&
                    queue.ValueKind == JsonValueKind.Object)
                {
                    return queue.GetInt32("id");
                }
            }
        }
        catch (JsonException)
        {
        }

        return 0;
    }
}
