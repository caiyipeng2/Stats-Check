using System.Text.Json;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.Core.Lcu;

public sealed record LivePlayer(
    string RiotId,
    string GameName,
    string TagLine,
    string SummonerName,
    string ChampionAlias,
    string Team,
    bool IsBot);

public sealed record LiveGameSnapshot(
    IReadOnlyList<LivePlayer> Players,
    string? ActiveRiotId,
    string? ActiveSummonerName,
    string? ActiveTeam);

public sealed class LiveClientService : IDisposable
{
    private readonly LogService _log;
    private HttpClient? _http;
    private bool _disposed;

    public LiveClientService(LogService log)
    {
        _log = log;
    }

    public async Task<LiveGameSnapshot?> TryGetAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        _http ??= CreateClient();
        try
        {
            var json = await _http.GetStringAsync("/liveclientdata/allgamedata", cancellationToken)
                .ConfigureAwait(false);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is { Host: "127.0.0.1", Port: 2999 }
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://127.0.0.1:2999"),
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    private static LiveGameSnapshot? Parse(string json)
    {
        using var document = JsonHelpers.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("allPlayers", out var allPlayers) ||
            allPlayers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var players = new List<LivePlayer>();
        foreach (var item in allPlayers.EnumerateArray())
        {
            var gameName = item.GetString("riotIdGameName") ?? string.Empty;
            var tagLine = item.GetString("riotIdTagLine") ?? string.Empty;
            var riotId = item.GetString("riotId") ?? string.Empty;
            var summonerName = item.GetString("summonerName") ?? string.Empty;
            var champion = item.GetString("championName") ?? string.Empty;
            var team = item.GetString("team") ?? string.Empty;

            if (string.IsNullOrEmpty(champion))
            {
                continue;
            }

            players.Add(new LivePlayer(
                riotId,
                gameName,
                tagLine,
                summonerName,
                champion,
                team,
                item.GetBool("isBot")));
        }

        if (players.Count == 0)
        {
            return null;
        }

        string? activeRiotId = null;
        string? activeSummonerName = null;
        string? activeTeam = null;
        if (root.TryGetProperty("activePlayer", out var active) && active.ValueKind == JsonValueKind.Object)
        {
            activeRiotId = active.GetString("riotId");
            activeSummonerName = active.GetString("summonerName");

            var match = players.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(activeRiotId) &&
                 (p.RiotId.Equals(activeRiotId, StringComparison.OrdinalIgnoreCase) ||
                  BuildRiotId(p).Equals(activeRiotId, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrEmpty(activeSummonerName) &&
                 p.SummonerName.Equals(activeSummonerName, StringComparison.OrdinalIgnoreCase)));

            activeTeam = match?.Team;
        }

        return new LiveGameSnapshot(players, activeRiotId, activeSummonerName, activeTeam);
    }

    private static string BuildRiotId(LivePlayer player) =>
        string.IsNullOrEmpty(player.TagLine) || string.IsNullOrEmpty(player.GameName)
            ? string.Empty
            : $"{player.GameName}#{player.TagLine}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http?.Dispose();
    }
}
