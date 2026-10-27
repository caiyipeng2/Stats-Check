namespace LolStatsCheck.Core.Models;

public sealed class ChampSelectPlayer
{
    public int CellId { get; init; }
    public TeamSide Team { get; init; }
    public int SlotIndex { get; init; }
    public int ChampionId { get; init; }
    public int PickIntentChampionId { get; init; }
    public string AssignedPosition { get; init; } = string.Empty;
    public long SummonerId { get; init; }
    public string Puuid { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsVisible { get; init; }
    public bool IsAutofilled { get; init; }
    public bool IsLocalPlayer { get; init; }

    public int DisplayChampionId => ChampionId > 0 ? ChampionId : PickIntentChampionId;

    public ChampSelectPlayer WithChampion(int championId) => new()
    {
        CellId = CellId,
        Team = Team,
        SlotIndex = SlotIndex,
        ChampionId = championId,
        PickIntentChampionId = PickIntentChampionId,
        AssignedPosition = AssignedPosition,
        SummonerId = SummonerId,
        Puuid = Puuid,
        DisplayName = DisplayName,
        IsVisible = IsVisible,
        IsAutofilled = IsAutofilled,
        IsLocalPlayer = IsLocalPlayer
    };

    public ChampSelectPlayer WithIdentity(
        string puuid, long summonerId, string displayName, bool isVisible) => new()
    {
        CellId = CellId,
        Team = Team,
        SlotIndex = SlotIndex,
        ChampionId = ChampionId,
        PickIntentChampionId = PickIntentChampionId,
        AssignedPosition = AssignedPosition,
        SummonerId = summonerId,
        Puuid = puuid,
        DisplayName = displayName,
        IsVisible = isVisible,
        IsAutofilled = IsAutofilled,
        IsLocalPlayer = IsLocalPlayer
    };

    public ChampSelectPlayer WithInGameIdentity(
        string puuid, long summonerId, string displayName, bool isLocalPlayer) => new()
    {
        CellId = CellId,
        Team = Team,
        SlotIndex = SlotIndex,
        ChampionId = ChampionId,
        PickIntentChampionId = PickIntentChampionId,
        AssignedPosition = AssignedPosition,
        SummonerId = summonerId,
        Puuid = puuid,
        DisplayName = displayName,
        IsVisible = true,
        IsAutofilled = IsAutofilled,
        IsLocalPlayer = isLocalPlayer
    };
}

public sealed class ChampSelectSnapshot
{
    public long GameId { get; init; }
    public int QueueId { get; init; }
    public int LocalPlayerCellId { get; init; }
    public string Phase { get; init; } = "ChampSelect";
    public IReadOnlyList<ChampSelectPlayer> MyTeam { get; init; } = Array.Empty<ChampSelectPlayer>();
    public IReadOnlyList<ChampSelectPlayer> TheirTeam { get; init; } = Array.Empty<ChampSelectPlayer>();
    public string SessionKey { get; init; } = string.Empty;

    public IEnumerable<ChampSelectPlayer> AllPlayers => MyTeam.Concat(TheirTeam);
}
