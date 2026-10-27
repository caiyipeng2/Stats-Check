using System.Windows.Media;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.App.ViewModels;

public static class Palette
{
    private static readonly Brush[] ChampionColors = BuildChampionBrushes();
    private static readonly Brush[] ProfileColors = BuildProfileBrushes();

    public static Brush Gold { get; } = Frozen("#C9A96A");
    public static Brush GoldBright { get; } = Frozen("#E6CB92");
    public static Brush Ally { get; } = Frozen("#4FA8E0");
    public static Brush Enemy { get; } = Frozen("#E0665A");
    public static Brush Win { get; } = Frozen("#3FD0A8");
    public static Brush Loss { get; } = Frozen("#EF6B5A");
    public static Brush Neutral { get; } = Frozen("#C9A96A");
    public static Brush Muted { get; } = Frozen("#617089");
    public static Brush Warn { get; } = Frozen("#E3B45A");
    public static Brush StatusConnected { get; } = Frozen("#3FD0A8");
    public static Brush StatusChampSelect { get; } = Frozen("#C9A96A");
    public static Brush StatusInGame { get; } = Frozen("#4FA8E0");
    public static Brush StatusDisconnected { get; } = Frozen("#E0665A");

    private static readonly (string Tier, string Color)[] TierColors =
    {
        ("IRON", "#8A929E"),
        ("BRONZE", "#B07A4A"),
        ("SILVER", "#A9B6C6"),
        ("GOLD", "#D8B45C"),
        ("PLATINUM", "#5FC6B8"),
        ("EMERALD", "#4CC77A"),
        ("DIAMOND", "#63A8E8"),
        ("MASTER", "#B57BE0"),
        ("GRANDMASTER", "#E06060"),
        ("CHALLENGER", "#67D5E8")
    };

    private static readonly Dictionary<string, Brush> TierBrushes = TierColors.ToDictionary(
        item => item.Tier, item => Frozen(item.Color), StringComparer.OrdinalIgnoreCase);

    public static Brush TeamAccent(TeamSide team, bool isLocal)
    {
        if (isLocal)
        {
            return Gold;
        }

        return team == TeamSide.MyTeam ? Ally : Enemy;
    }

    public static Brush ChampionAccent(int championId) =>
        championId <= 0 ? Frozen("#1B2534") : ChampionColors[Math.Abs(championId) % ChampionColors.Length];

    public static Brush ProfileAccent(string identity) =>
        ProfileColors[Math.Abs(StableHash(identity)) % ProfileColors.Length];

    public static Brush WinRate(double winRate, bool hasGames)
    {
        if (!hasGames)
        {
            return Muted;
        }

        return winRate switch
        {
            >= 55 => Win,
            >= 48 => Neutral,
            _ => Loss
        };
    }

    public static Brush Rank(string tier) =>
        !string.IsNullOrEmpty(tier) && TierBrushes.TryGetValue(tier, out var brush) ? brush : Muted;

    public static string Initial(string? text, string fallback = "?")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var trimmed = text.Trim();
        return trimmed.Length == 1 ? trimmed : trimmed[..1];
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value)
            {
                hash = hash * 31 + ch;
            }

            return hash;
        }
    }

    private static Brush[] BuildChampionBrushes()
    {
        var colors = new[]
        {
            "#34506B", "#63465A", "#6F5438", "#3F6256", "#544874",
            "#714B45", "#405A6A", "#63543C", "#396363", "#584564",
            "#60603C", "#3D5364"
        };
        return colors.Select(Frozen).ToArray();
    }

    private static Brush[] BuildProfileBrushes()
    {
        var colors = new[]
        {
            "#2C4358", "#503A4C", "#57452F", "#345044", "#443A5E",
            "#5A3D39", "#344B58", "#504530", "#315151", "#4A3A52"
        };
        return colors.Select(Frozen).ToArray();
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
