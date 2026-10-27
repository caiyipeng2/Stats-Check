namespace LolStatsCheck.Core.Models;

public enum TeamSide
{
    MyTeam,
    TheirTeam
}

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected
}

public enum PlayerCardStatus
{
    Hidden,
    Loading,
    Ready,
    Unavailable,
    Error
}

public enum LcuDataStatus
{
    Ok,
    NotFound,
    Forbidden,
    Unavailable,
    Error
}
