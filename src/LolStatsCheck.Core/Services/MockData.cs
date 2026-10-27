using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed record MockPlayerDefinition(
    int Slot,
    TeamSide Team,
    string Name,
    int BaseChampionId,
    string Position,
    bool Visible,
    bool Local,
    bool Autofilled,
    int ProfileIconId,
    int Level,
    int Seed,
    int[] ChampionPool,
    int WinPercent,
    int Streak,
    int LoadDelayMs,
    bool NoHistory);

public sealed record MockPlayerData(
    PlayerProfile Profile,
    PlayerStats Stats,
    RankInfo? Rank);

public static class MockData
{
    public const int MockQueueId = 420;
    public const int LocalCellId = 2;

    private static readonly int[] AllyMidChampions = { 103, 245 };

    public static IReadOnlyList<MockPlayerDefinition> Definitions { get; } = new List<MockPlayerDefinition>
    {
        new(0, TeamSide.MyTeam, "上单老张", 86, "top", true, false, false, 588, 312, 11,
            new[] { 86, 122, 54, 17 }, 58, 3, 0, false),
        new(1, TeamSide.MyTeam, "野区厂长", 64, "jungle", true, false, false, 607, 287, 22,
            new[] { 64, 121, 254, 113 }, 55, 0, 0, false),
        new(2, TeamSide.MyTeam, "星河入梦", 103, "middle", true, true, false, 4568, 421, 33,
            new[] { 103, 245, 157, 238 }, 61, 0, 0, false),
        new(3, TeamSide.MyTeam, "暴走萝莉", 222, "bottom", true, false, false, 521, 198, 44,
            new[] { 222, 202, 81, 51 }, 52, -3, 0, false),
        new(4, TeamSide.MyTeam, "只玩辅助", 412, "utility", true, false, true, 3549, 376, 55,
            new[] { 412, 89, 53, 201 }, 50, 0, 0, false),

        new(0, TeamSide.TheirTeam, "TopKing", 122, "top", true, false, false, 3360, 265, 66,
            new[] { 122, 86, 266, 58 }, 54, 0, 0, false),
        new(1, TeamSide.TheirTeam, "JungleDiff", 121, "jungle", true, false, false, 4322, 344, 77,
            new[] { 121, 64, 254, 59 }, 57, 0, 1500, false),
        new(2, TeamSide.TheirTeam, "神秘中单", 238, "middle", false, false, false, 0, 0, 88,
            Array.Empty<int>(), 0, 0, 0, false),
        new(3, TeamSide.TheirTeam, "ADC不会玩", 202, "bottom", true, false, false, 577, 152, 99,
            new[] { 202, 222, 81, 22 }, 45, 0, 0, true),
        new(4, TeamSide.TheirTeam, "敌军辅助", 555, "utility", false, false, false, 0, 0, 110,
            Array.Empty<int>(), 0, 0, 0, false)
    };

    public static ChampSelectSnapshot CreateSnapshot(long gameId, int tick)
    {
        var midChampion = AllyMidChampions[(tick / 30) % AllyMidChampions.Length];
        var myTeam = new List<ChampSelectPlayer>();
        var theirTeam = new List<ChampSelectPlayer>();

        foreach (var definition in Definitions)
        {
            var championId = definition.Local ? midChampion : definition.BaseChampionId;
            var player = new ChampSelectPlayer
            {
                CellId = definition.Team == TeamSide.MyTeam ? definition.Slot : definition.Slot + 5,
                Team = definition.Team,
                SlotIndex = definition.Slot,
                ChampionId = championId,
                AssignedPosition = definition.Position,
                SummonerId = definition.Visible ? 70_000_000_000 + definition.Seed : 0,
                Puuid = definition.Visible ? CreatePuuid(definition.Seed) : string.Empty,
                DisplayName = definition.Visible ? definition.Name : string.Empty,
                IsVisible = definition.Visible,
                IsAutofilled = definition.Autofilled,
                IsLocalPlayer = definition.Local
            };

            if (definition.Team == TeamSide.MyTeam)
            {
                myTeam.Add(player);
            }
            else
            {
                theirTeam.Add(player);
            }
        }

        return new ChampSelectSnapshot
        {
            GameId = gameId,
            QueueId = MockQueueId,
            LocalPlayerCellId = LocalCellId,
            MyTeam = myTeam,
            TheirTeam = theirTeam,
            SessionKey = $"mock:{gameId}:{midChampion}"
        };
    }

    public static string GetChampionName(int championId) => ChampionCatalog.GetMockName(championId);

    public static PlayerProfile CreateProfile(MockPlayerDefinition definition)
    {
        return new PlayerProfile(
            CreatePuuid(definition.Seed),
            70_000_000_000 + definition.Seed,
            definition.Name,
            definition.Name,
            "CN1",
            definition.ProfileIconId,
            definition.Level);
    }

    public static RankInfo? CreateRank(MockPlayerDefinition definition)
    {
        if (!definition.Visible)
        {
            return null;
        }

        var tiers = new[] { ("GOLD", "II", 42), ("PLATINUM", "IV", 18), ("DIAMOND", "III", 76), ("EMERALD", "IV", 12), ("SILVER", "I", 66) };
        var tier = tiers[definition.Seed % tiers.Length];
        return new RankInfo
        {
            QueueType = "RANKED_SOLO_5x5",
            Tier = tier.Item1,
            TierCn = RankTierNames.ToChinese(tier.Item1),
            Division = tier.Item2,
            LeaguePoints = tier.Item3,
            Wins = 80 + definition.Seed,
            Losses = 70 + definition.Seed % 17
        };
    }

    public static IReadOnlyList<MatchRecord> CreateMatches(
        MockPlayerDefinition definition, int currentChampionId, DateTimeOffset now)
    {
        if (!definition.Visible || definition.NoHistory || definition.ChampionPool.Length == 0)
        {
            return Array.Empty<MatchRecord>();
        }

        var random = new Random(definition.Seed * 7919);
        var matches = new List<MatchRecord>();
        for (var index = 0; index < 26; index++)
        {
            var championId = index is 0 or 4 or 9
                ? currentChampionId
                : definition.ChampionPool[(index + definition.Seed) % definition.ChampionPool.Length];

            var win = random.Next(100) < definition.WinPercent;
            if (definition.Streak > 0 && index < definition.Streak)
            {
                win = true;
            }
            else if (definition.Streak < 0 && index < -definition.Streak)
            {
                win = false;
            }

            var kills = 3 + random.Next(0, 15);
            var deaths = 2 + random.Next(0, 9);
            var assists = 4 + random.Next(0, 18);

            matches.Add(new MatchRecord(
                9_000_000_000 + definition.Seed * 100L + index,
                MockQueueId,
                now.AddHours(-(index * 5.5 + definition.Seed % 9)).AddMinutes(-random.Next(0, 50)),
                900 + random.Next(0, 1400),
                championId,
                win,
                kills,
                deaths,
                assists,
                "MATCHED_GAME",
                "CLASSIC"));
        }

        return matches;
    }

    public static MockPlayerData CreatePlayerData(
        MockPlayerDefinition definition, int currentChampionId, DateTimeOffset now)
    {
        var profile = CreateProfile(definition);
        var rank = CreateRank(definition);
        var matches = CreateMatches(definition, currentChampionId, now);
        var stats = MatchStatsCalculator.Calculate(
            matches, currentChampionId, MockQueueId, 20, true, false, now);
        return new MockPlayerData(profile, stats, rank);
    }

    public static string CreatePuuid(int seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, 0x51A5C0DE00000000L + seed);
        BitConverter.TryWriteBytes(bytes[8..], 0x0000000000000000L + seed * 31L);
        return new Guid(bytes).ToString();
    }
}
