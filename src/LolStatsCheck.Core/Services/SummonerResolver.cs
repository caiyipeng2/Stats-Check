using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed class SummonerResolver
{
    private readonly Func<LcuClient?> _clientProvider;
    private readonly LogService _log;

    public SummonerResolver(Func<LcuClient?> clientProvider, LogService log)
    {
        _clientProvider = clientProvider;
        _log = log;
    }

    public async Task<PlayerProfile?> ResolveAsync(
        string? gameName, string? tagLine, string? summonerName, CancellationToken cancellationToken)
    {
        var client = _clientProvider();
        if (client is null)
        {
            return null;
        }

        foreach (var path in BuildPaths(gameName, tagLine, summonerName))
        {
            try
            {
                var json = await client.TryGetStringAsync(path, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                var profile = SummonerParser.Parse(json, string.Empty, 0,
                    summonerName ?? gameName ?? string.Empty);
                if (profile is not null && ChampSelectParser.IsValidPuuid(profile.Puuid))
                {
                    return profile;
                }
            }
            catch (Exception ex) when (ex is LcuException or JsonException)
            {
                _log.Debug("解析玩家身份失败：" + ex.Message);
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildPaths(string? gameName, string? tagLine, string? summonerName)
    {
        if (!string.IsNullOrWhiteSpace(gameName) && !string.IsNullOrWhiteSpace(tagLine))
        {
            yield return "/lol-summoner/v1/summoner-aliases" +
                         $"?gameName={Uri.EscapeDataString(gameName.Trim())}" +
                         $"&tagLine={Uri.EscapeDataString(tagLine.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(summonerName))
        {
            yield return "/lol-summoner/v1/summoners?name=" +
                         Uri.EscapeDataString(summonerName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(gameName) && string.IsNullOrWhiteSpace(tagLine))
        {
            yield return "/lol-summoner/v1/summoners?name=" +
                         Uri.EscapeDataString(gameName.Trim());
        }
    }
}
