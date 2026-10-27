namespace LolStatsCheck.Core.Services;

public sealed class AppSettings
{
    public string? ClientPath { get; set; }
    public int RecentGames { get; set; } = 20;
    public bool CurrentModeFirst { get; set; } = true;
    public double Opacity { get; set; } = 0.96;
    public bool AlwaysOnTop { get; set; } = true;
    public bool AutoShow { get; set; } = true;
    public int PostGameSeconds { get; set; } = 30;
    public bool ShowRank { get; set; } = true;
    public bool ShowDiagnostics { get; set; }
    public string? FallbackPath { get; set; }
    public bool FallbackAutoLaunch { get; set; }
    public string? FallbackArguments { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool DisclaimerAccepted { get; set; }

    public void Normalize()
    {
        if (RecentGames is not (10 or 20 or 30))
        {
            RecentGames = 20;
        }

        if (PostGameSeconds is not (-1 or 0 or 15 or 30 or 60))
        {
            PostGameSeconds = 30;
        }

        Opacity = Math.Clamp(Opacity, 0.7, 1.0);
        FallbackArguments ??= string.Empty;
        FallbackPath = string.IsNullOrWhiteSpace(FallbackPath) ? null : FallbackPath.Trim();
        ClientPath = string.IsNullOrWhiteSpace(ClientPath) ? null : ClientPath.Trim();
    }

    public AppSettings Clone() => new()
    {
        ClientPath = ClientPath,
        RecentGames = RecentGames,
        CurrentModeFirst = CurrentModeFirst,
        Opacity = Opacity,
        AlwaysOnTop = AlwaysOnTop,
        AutoShow = AutoShow,
        PostGameSeconds = PostGameSeconds,
        ShowRank = ShowRank,
        ShowDiagnostics = ShowDiagnostics,
        FallbackPath = FallbackPath,
        FallbackAutoLaunch = FallbackAutoLaunch,
        FallbackArguments = FallbackArguments,
        WindowLeft = WindowLeft,
        WindowTop = WindowTop,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        DisclaimerAccepted = DisclaimerAccepted
    };
}
