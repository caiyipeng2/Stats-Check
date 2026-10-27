namespace LolStatsCheck.Core.Models;

public sealed record PlayerCardState(
    string Key,
    int CellId,
    TeamSide Team,
    int SlotIndex,
    string DisplayName,
    string PositionText,
    int ChampionId,
    int PickIntentChampionId,
    string ChampionName,
    PlayerCardStatus Status,
    string? Message,
    PlayerProfile? Profile,
    PlayerStats? Stats,
    RankInfo? Rank,
    bool IsAutofilled,
    bool IsLocalPlayer,
    bool CanRetry,
    bool CanUseFallback,
    DateTimeOffset UpdatedAt)
{
    public static PlayerCardState Placeholder(
        string key,
        int cellId,
        TeamSide team,
        int slot,
        string name,
        string position,
        bool isLocal) =>
        new(key, cellId, team, slot, name, position, 0, 0, string.Empty,
            PlayerCardStatus.Hidden, "身份未公开", null, null, null,
            false, isLocal, false, true, DateTimeOffset.MinValue);
}

public sealed record SessionUpdate(
    ConnectionStatus Connection,
    string Phase,
    int QueueId,
    string ModeName,
    bool IsChampSelect,
    bool IsInGame,
    bool OverlayVisible,
    string SessionKey,
    string StatusText,
    string StatusDetail,
    string? Banner,
    bool HasHiddenPlayers,
    bool HasQueryIssues,
    DateTimeOffset? LastUpdated,
    IReadOnlyList<PlayerCardState> MyTeam,
    IReadOnlyList<PlayerCardState> TheirTeam,
    bool IsMock,
    string TrayText)
{
    public bool HasSession => MyTeam.Count > 0 || TheirTeam.Count > 0;

    public static SessionUpdate Empty(
        ConnectionStatus connection,
        string statusText,
        string statusDetail,
        string trayText,
        bool isMock = false) =>
        new(connection, "None", 0, string.Empty, false, false, false, string.Empty,
            statusText, statusDetail, null, false, false, null,
            Array.Empty<PlayerCardState>(), Array.Empty<PlayerCardState>(),
            isMock, trayText);
}

public sealed record FallbackLaunchResult(
    FallbackLaunchStatus Status,
    string Message,
    int? ProcessId);

public enum FallbackLaunchStatus
{
    NotConfigured,
    PathMissing,
    AlreadyRunning,
    Started,
    Failed
}
