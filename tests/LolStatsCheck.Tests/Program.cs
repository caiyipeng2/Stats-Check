using System.Text;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;
using LolStatsCheck.Core.Services;

internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Run("lockfile 解析", TestLockfile);
        Run("BP 会话解析（可见/隐藏/本机）", TestChampSelect);
        Run("BP 会话键随身份可见性变化", TestSessionKeyVisibility);
        Run("BP 空数据与 PUUID 校验", TestChampSelectEdgeCases);
        Run("对局阶段与游戏会话解析", TestGameflow);
        Run("腾讯战绩历史解析（games 包装）", TestMatchHistory);
        Run("战绩历史根数组与日期回退", TestMatchHistoryArrayRoot);
        Run("战绩统计：胜率/KDA/连败/英雄聚合", TestCalculatorBasic);
        Run("战绩统计：连败阈值与同模式过滤", TestCalculatorQueueFilter);
        Run("战绩统计：重开/教程/重复对局与无效场次", TestCalculatorFiltering);
        Run("战绩统计：场次截断与场上限", TestCalculatorLimits);
        Run("对局记录：重开与教程判定", TestMatchRecord);
        Run("召唤师资料解析与名称回退", TestSummoner);
        Run("段位解析与展示", TestRankedStats);
        Run("设置归一化", TestSettingsNormalize);
        Run("设置读写与损坏配置回退", TestSettingsRoundTrip);
        Run("队列名称内置回退", TestQueueCatalog);
        Run("兜底工具路径解析", TestFallbackResolution);
        Run("Mock 模式数据生成", TestMockData);

        var realFixtures = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        foreach (var path in realFixtures)
        {
            Run("真实样本解析：" + Path.GetFileName(path), () => TestRealFixture(path));
        }

        Console.WriteLine();
        if (Failures.Count == 0)
        {
            Console.WriteLine($"全部通过：{_passed} 项");
            return 0;
        }

        Console.WriteLine($"失败 {Failures.Count} 项，通过 {_passed} 项：");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  - " + failure);
        }

        return 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception ex)
        {
            Failures.Add(name + "：" + ex.Message);
            Console.WriteLine("FAIL  " + name);
            Console.WriteLine("      " + ex.Message);
        }
    }

    private static void TestLockfile()
    {
        var credentials = LcuLocator.ParseLockfile("LeagueClient:12345:54321:test_token_value:https");
        Check(credentials is not null, "有效 lockfile 应解析成功");
        Eq(54321, credentials!.Port, "port");
        Eq("test_token_value", credentials.Password, "password");
        Eq("https", credentials.Protocol, "protocol");
        Eq("https://127.0.0.1:54321", credentials.BaseUrl, "baseUrl");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials.BasicAuth));
        Eq("riot:test_token_value", decoded, "basicAuth");

        Check(LcuLocator.ParseLockfile("only:three:parts") is null, "字段不足应返回 null");
        Check(LcuLocator.ParseLockfile("LeagueClient:0:0:token:https") is null, "端口 0 应返回 null");
        Check(LcuLocator.ParseLockfile("LeagueClient:0:70000:token:https") is null, "端口越界应返回 null");
        Check(LcuLocator.ParseLockfile("LeagueClient:0:54321::https") is null, "空密码应返回 null");
        Eq("http", LcuLocator.ParseLockfile(" LeagueClient:1:1:token:http \n")!.Protocol, "带空白与自定义协议");
    }

    private static void TestChampSelect()
    {
        var json = Fixture("champ-select.json");
        var snapshot = ChampSelectParser.Parse(json, 0, 0);

        Eq(7000000001L, snapshot.GameId, "gameId 从根节点读取");
        Eq(420, snapshot.QueueId, "queueId 从根节点读取");
        Eq(2, snapshot.LocalPlayerCellId, "localPlayerCellId");
        Eq(5, snapshot.MyTeam.Count, "我方人数");
        Eq(5, snapshot.TheirTeam.Count, "对方人数");
        Eq("ChampSelect", snapshot.Phase, "phase");

        Eq(0, snapshot.MyTeam[0].SlotIndex, "槽位序号从 0 开始");
        Eq(4, snapshot.MyTeam[4].SlotIndex, "槽位序号递增");
        Eq(103, snapshot.MyTeam[0].ChampionId, "已选英雄");
        Eq("top", snapshot.MyTeam[0].AssignedPosition, "分路");
        Check(snapshot.MyTeam[0].IsVisible, "可见玩家 IsVisible");
        Eq("上单玩家", snapshot.MyTeam[0].DisplayName, "可见玩家名称");

        Eq(0, snapshot.MyTeam[1].ChampionId, "未选英雄时 championId 为 0");
        Eq(64, snapshot.MyTeam[1].PickIntentChampionId, "预选英雄");
        Eq(64, snapshot.MyTeam[1].DisplayChampionId, "展示英雄回退到预选");
        Check(snapshot.MyTeam[1].IsAutofilled, "补位标记");

        var locals = snapshot.AllPlayers.Where(p => p.IsLocalPlayer).ToList();
        Eq(1, locals.Count, "本机玩家唯一");
        Eq(2, locals[0].CellId, "本机玩家 cellId");
        Eq(TeamSide.MyTeam, locals[0].Team, "本机玩家属于我方");
        Check(snapshot.TheirTeam.All(p => !p.IsLocalPlayer), "对方同 cellId 不应被标记为本机");

        var hidden = snapshot.TheirTeam[0];
        Check(!hidden.IsVisible, "隐藏玩家 IsVisible 为 false");
        Eq(string.Empty, hidden.Puuid, "隐藏玩家 PUUID 必须清空");
        Eq(0L, hidden.SummonerId, "隐藏玩家召唤师 ID 必须清空");
        Eq(string.Empty, hidden.DisplayName, "隐藏玩家名称必须清空");

        Check(!snapshot.TheirTeam[3].IsVisible, "第三名隐藏对手");
        Eq(string.Empty, snapshot.TheirTeam[3].DisplayName, "隐藏对手四名称清空");

        Eq("对手二", snapshot.TheirTeam[1].DisplayName, "可见对手保留名称");
        Check(snapshot.TheirTeam[1].IsVisible, "可见对手 IsVisible");
        Eq("对手五", snapshot.TheirTeam[4].DisplayName, "可见对手五名称");
        Check(snapshot.TheirTeam[4].IsAutofilled, "对手补位标记");

        Check(snapshot.SessionKey.StartsWith("7000000001|420|", StringComparison.Ordinal), "会话键前缀");
        Check(!snapshot.SessionKey.Contains("aaaaaaaa", StringComparison.Ordinal), "会话键不得泄露隐藏 PUUID");
        Check(snapshot.SessionKey.Contains("33333333-3333-4333-8333-333333333333", StringComparison.Ordinal),
            "会话键包含可见 PUUID");

        var overridden = ChampSelectParser.Parse(json, 8000000002, 440);
        Eq(8000000002L, overridden.GameId, "显式 gameId 覆盖");
        Eq(440, overridden.QueueId, "显式 queueId 覆盖");
        Check(overridden.SessionKey.StartsWith("8000000002|440|", StringComparison.Ordinal), "覆盖后的会话键");
    }

    private static void TestSessionKeyVisibility()
    {
        const string visible = """
            {"gameId":1,"queueId":420,"localPlayerCellId":0,
             "myTeam":[{"cellId":0,"puuid":"11111111-1111-4111-8111-111111111111","summonerId":1,"displayName":"A","nameVisibilityType":"VISIBLE"}],
             "theirTeam":[{"cellId":0,"puuid":"22222222-2222-4222-8222-222222222222","summonerId":2,"displayName":"B","nameVisibilityType":"VISIBLE"}]}
            """;
        const string hidden = """
            {"gameId":1,"queueId":420,"localPlayerCellId":0,
             "myTeam":[{"cellId":0,"puuid":"11111111-1111-4111-8111-111111111111","summonerId":1,"displayName":"A","nameVisibilityType":"VISIBLE"}],
             "theirTeam":[{"cellId":0,"puuid":"22222222-2222-4222-8222-222222222222","summonerId":2,"displayName":"B","nameVisibilityType":"HIDDEN"}]}
            """;

        var first = ChampSelectParser.Parse(visible, 0, 0);
        var second = ChampSelectParser.Parse(hidden, 0, 0);
        var third = ChampSelectParser.Parse(hidden, 0, 0);

        Check(first.SessionKey != second.SessionKey, "身份从可见变为隐藏时应更新会话键");
        Eq(second.SessionKey, third.SessionKey, "相同状态会话键应稳定");
        Eq(string.Empty, second.TheirTeam[0].Puuid, "隐藏后 PUUID 清空");
    }

    private static void TestChampSelectEdgeCases()
    {
        var empty = ChampSelectParser.Parse("{}", 123, 420);
        Eq(0, empty.MyTeam.Count, "缺失 myTeam 不应抛错");
        Eq(0, empty.TheirTeam.Count, "缺失 theirTeam 不应抛错");
        Eq(123L, empty.GameId, "缺失 gameId 时使用入参");
        Eq(420, empty.QueueId, "缺失 queueId 时使用入参");

        Check(ChampSelectParser.IsValidPuuid("11111111-1111-4111-8111-111111111111"), "合法 GUID");
        Check(!ChampSelectParser.IsValidPuuid("not-a-guid"), "非法 GUID");
        Check(!ChampSelectParser.IsValidPuuid(""), "空 PUUID");
        Check(!ChampSelectParser.IsValidPuuid("00000000-0000-0000-0000-000000000000"), "空 GUID");

        const string noVisibility = """
            {"myTeam":[{"cellId":0,"puuid":"11111111-1111-4111-8111-111111111111","summonerId":9,"displayName":"旧客户端"}],
             "theirTeam":[{"cellId":0,"puuid":"bad","summonerId":0,"displayName":"非法"}]}
            """;
        var legacy = ChampSelectParser.Parse(noVisibility, 0, 0);
        Check(legacy.MyTeam[0].IsVisible, "缺少 visibility 时以 PUUID+召唤师 ID 判定可见");
        Check(!legacy.TheirTeam[0].IsVisible, "缺少 visibility 且身份非法时按隐藏处理");
        Eq(string.Empty, legacy.TheirTeam[0].DisplayName, "非法身份名称清空");
    }

    private static void TestGameflow()
    {
        Eq("ChampSelect", GameflowParser.ParsePhase("\"ChampSelect\""), "字符串根节点");
        Eq("InProgress", GameflowParser.ParsePhase("{\"phase\":\"InProgress\"}"), "对象根节点");
        Eq(string.Empty, GameflowParser.ParsePhase(""), "空输入");

        var withQueueObject = GameflowParser.ParseSession("{\"gameData\":{\"gameId\":123,\"queue\":{\"id\":420}}}");
        Eq(123L, withQueueObject.GameId, "gameData.gameId");
        Eq(420, withQueueObject.QueueId, "gameData.queue.id");

        var withQueueId = GameflowParser.ParseSession("{\"gameData\":{\"gameId\":456,\"queueId\":440}}");
        Eq(440, withQueueId.QueueId, "gameData.queueId 回退");

        Eq((0L, 0), GameflowParser.ParseSession(""), "空会话");
        Eq((0L, 0), GameflowParser.ParseSession("{}"), "无游戏数据");
    }

    private static void TestMatchHistory()
    {
        const string puuid = "33333333-3333-4333-8333-333333333333";
        var result = MatchHistoryParser.Parse(Fixture("match-history.json"), puuid);

        Eq(2, result.Matches.Count, "仅保留本人且英雄有效的对局");
        Eq(50, result.TotalAvailable, "服务端总场次");

        var first = result.Matches[0];
        Eq(9000000001L, first.GameId, "gameId");
        Eq(420, first.QueueId, "queueId");
        Eq(103, first.ChampionId, "championId");
        Check(first.Win, "win");
        Eq(8, first.Kills, "kills");
        Eq(2, first.Deaths, "deaths");
        Eq(10, first.Assists, "assists");
        Eq(1750000000000L, first.GameCreation.ToUnixTimeMilliseconds(), "gameCreation 毫秒时间戳");
        Eq(1800, first.DurationSeconds, "gameDuration");
        Check(!first.IsRemake, "完整对局不算重开");

        var second = result.Matches[1];
        Eq(9000000002L, second.GameId, "第二局 gameId");
        Eq(2400, second.QueueId, "第二局 queueId");
        Eq(64, second.ChampionId, "第二局英雄");
        Check(second.IsRemake, "短对局判定为重开");
        Check(!second.Win, "第二局失败");

        Eq(2, MatchHistoryParser.Parse(Fixture("match-history.json"), puuid.ToUpperInvariant()).Matches.Count,
            "PUUID 匹配应忽略大小写");
        Eq(0, MatchHistoryParser.Parse(Fixture("match-history.json"), "cccccccc-cccc-4ccc-8ccc-cccccccccccc").Matches.Count,
            "未参与的对局不返回");
        Eq(0, MatchHistoryParser.Parse("", puuid).Matches.Count, "空输入");
        Eq(0, MatchHistoryParser.Parse("{}", puuid).Matches.Count, "缺失 games");
    }

    private static void TestMatchHistoryArrayRoot()
    {
        const string json = """
            [
              {
                "gameId": 7,
                "queueId": 450,
                "gameCreationDate": "2025-06-01T12:00:00Z",
                "gameDuration": 1200,
                "gameType": "MATCHED_GAME",
                "gameMode": "ARAM",
                "participantIdentities": [
                  {"participantId": 2, "player": {"puuid": "synthetic-puuid"}}
                ],
                "participants": [
                  {"participantId": 2, "championId": 1, "stats": {"win": true, "kills": 2, "deaths": 1, "assists": 3}}
                ]
              }
            ]
            """;
        var result = MatchHistoryParser.Parse(json, "synthetic-puuid");
        Eq(1, result.Matches.Count, "数组根节点");
        Eq(1, result.TotalAvailable, "数组总数");
        Eq(450, result.Matches[0].QueueId, "队列");
        Eq(DateTimeOffset.Parse("2025-06-01T12:00:00Z").ToUnixTimeMilliseconds(),
            result.Matches[0].GameCreation.ToUnixTimeMilliseconds(), "gameCreationDate 回退");

        const string wrappedArray = """
            {"games": [{"gameId": 8, "queueId": 420, "gameCreation": 1750000000000, "gameDuration": 1800,
              "gameType": "MATCHED_GAME", "gameMode": "CLASSIC",
              "participantIdentities": [{"participantId": 1, "player": {"puuid": "p"}}],
              "participants": [{"participantId": 1, "championId": 2, "stats": {"win": false}}]}]}
            """;
        Eq(1, MatchHistoryParser.Parse(wrappedArray, "p").Matches.Count, "games 直接为数组");
        Eq(0, MatchHistoryParser.Parse("{\"games\":{}}", "p").Matches.Count, "games 对象无内层数组");
    }

    private static void TestCalculatorBasic()
    {
        var matches = new List<MatchRecord>
        {
            Match(101, 420, 5, 103, true, 10, 2, 5),
            Match(102, 420, 4, 64, true, 4, 4, 6),
            Match(103, 420, 3, 103, true, 2, 3, 8),
            Match(104, 420, 2, 103, false, 1, 5, 2),
            Match(105, 420, 1, 99, true, 7, 1, 3)
        };
        var stats = MatchStatsCalculator.Calculate(matches, 103, 420, 20, currentModeFirst: false,
            now: DateTimeOffset.Parse("2025-06-30T00:00:00Z"));

        Eq(5, stats.Games, "场次");
        Check(stats.HasGames, "HasGames");
        Eq(4, stats.Wins, "胜场");
        Eq(1, stats.Losses, "负场");
        Close(80, stats.WinRate, "胜率");
        Eq(24, stats.Kills, "总击杀");
        Eq(15, stats.Deaths, "总死亡");
        Eq(24, stats.Assists, "总助攻");
        Close(48d / 15d, stats.Kda, "KDA");
        Close(4.8, stats.AvgKills, "场均击杀");

        Eq(3, stats.RecentChampions.Count, "英雄聚合数量");
        Eq(103, stats.RecentChampions[0].ChampionId, "最近英雄排序");
        Eq(3, stats.RecentChampions[0].Games, "当前英雄场次");
        Eq(2, stats.RecentChampions[0].Wins, "当前英雄胜场");
        Eq(1, stats.RecentChampions[0].Losses, "当前英雄负场");
        Check(stats.CurrentChampion is not null, "当前英雄聚合");
        Eq(103, stats.CurrentChampion!.ChampionId, "当前英雄 ID");

        Eq(3, stats.WinStreak, "三连胜应记录");
        Eq(0, stats.LossStreak, "无连败");
        Eq(5, stats.ConsideredMatches, "统计场次");
        Eq(5, stats.PoolAvailable, "可用池");
        Eq(0, stats.RemakesExcluded, "重开数");
        Check(!stats.FilteredByQueue, "未按队列过滤");
        Check(!stats.IsStale, "新鲜数据");
        Eq(DateTimeOffset.Parse("2025-06-30T00:00:00Z"), stats.FetchedAt, "抓取时间");

        var twoWins = MatchStatsCalculator.Calculate(new List<MatchRecord>
        {
            Match(201, 420, 3, 1, true, 1, 1, 1),
            Match(202, 420, 2, 1, true, 1, 1, 1),
            Match(203, 420, 1, 1, false, 1, 1, 1)
        }, 0, 420, 20, currentModeFirst: false);
        Eq(0, twoWins.WinStreak, "两连胜低于阈值不显示");
        Eq(0, twoWins.LossStreak, "无连败");

        var losses = MatchStatsCalculator.Calculate(new List<MatchRecord>
        {
            Match(211, 420, 3, 1, false, 1, 1, 1),
            Match(212, 420, 2, 1, false, 1, 1, 1),
            Match(213, 420, 1, 1, false, 1, 1, 1)
        }, 0, 420, 20, currentModeFirst: false);
        Eq(3, losses.LossStreak, "三连败应记录");
        Eq(0, losses.WinStreak, "无连胜");
    }

    private static void TestCalculatorQueueFilter()
    {
        var matches = new List<MatchRecord>
        {
            Match(301, 2400, 7, 777, true, 1, 1, 1),
            Match(302, 420, 6, 103, true, 1, 1, 1),
            Match(303, 420, 5, 104, true, 1, 1, 1),
            Match(304, 420, 4, 105, true, 1, 1, 1),
            Match(305, 420, 3, 106, false, 1, 1, 1),
            Match(306, 420, 2, 107, false, 1, 1, 1)
        };

        var filtered = MatchStatsCalculator.Calculate(matches, 0, 420, 10, currentModeFirst: true);
        Eq(5, filtered.Games, "同队列场次足够时过滤");
        Check(filtered.FilteredByQueue, "FilteredByQueue");
        Check(filtered.RecentChampions.All(c => c.ChampionId != 777), "其他队列英雄不应出现");

        var narrow = MatchStatsCalculator.Calculate(matches, 0, 420, 3, currentModeFirst: true);
        Eq(3, narrow.Games, "截断到最近 3 场");
        Check(narrow.FilteredByQueue, "少量场次同样按队列过滤");
        Check(narrow.RecentChampions.All(c => c.ChampionId is 103 or 104 or 105), "保留最近同队列对局");

        var unfiltered = MatchStatsCalculator.Calculate(matches, 0, 420, 10, currentModeFirst: false);
        Eq(6, unfiltered.Games, "关闭偏好后统计全部");
        Check(!unfiltered.FilteredByQueue, "未过滤标记");

        var insufficient = new List<MatchRecord>
        {
            Match(311, 420, 4, 1, true, 1, 1, 1),
            Match(312, 420, 3, 1, true, 1, 1, 1),
            Match(313, 2400, 2, 2, true, 1, 1, 1),
            Match(314, 2400, 1, 2, true, 1, 1, 1)
        };
        var fallback = MatchStatsCalculator.Calculate(insufficient, 0, 420, 20, currentModeFirst: true);
        Eq(4, fallback.Games, "同队列不足 5 场时不强制过滤");
        Check(!fallback.FilteredByQueue, "回退到全部模式");
    }

    private static void TestCalculatorFiltering()
    {
        var matches = new List<MatchRecord>
        {
            Match(401, 420, 1, 103, false, 0, 0, 0, durationSeconds: 200),
            Match(402, 420, 2, 104, true, 1, 1, 1, type: "TUTORIAL"),
            Match(403, 420, 3, 105, true, 2, 2, 2),
            Match(403, 420, 0, 106, false, 9, 9, 9),
            Match(404, 420, 4, 107, false, 3, 3, 3),
            new MatchRecord(0, 420, DateTimeOffset.MinValue, 1800, 108, true, 5, 5, 5, "MATCHED_GAME", "CLASSIC")
        };

        var stats = MatchStatsCalculator.Calculate(matches, 0, 420, 20, currentModeFirst: false);

        Eq(2, stats.Games, "仅统计有效且非重开的对局");
        Eq(1, stats.RemakesExcluded, "重开计数");
        Eq(2, stats.PoolAvailable, "可用池不含教程与重开");
        Eq(1, stats.Wins, "胜场");
        Eq(1, stats.Losses, "负场");
        Check(stats.RecentChampions.All(c => c.ChampionId != 104), "教程对局被排除");
        Check(stats.RecentChampions.All(c => c.ChampionId != 106), "重复 gameId 保留最新一条");
        Check(stats.RecentChampions.All(c => c.ChampionId != 108), "无 gameId 且无时间的对局被丢弃");
        Check(stats.RecentChampions.Any(c => c.ChampionId == 107), "最新对局参与统计");
    }

    private static void TestCalculatorLimits()
    {
        var matches = new List<MatchRecord>();
        for (var index = 1; index <= 10; index++)
        {
            matches.Add(Match(500 + index, 420, index, index, index % 2 == 0, 1, 1, 1));
        }

        var limited = MatchStatsCalculator.Calculate(matches, 0, 420, 3, currentModeFirst: false);
        Eq(3, limited.Games, "最近场次上限");
        Eq(10, limited.RecentChampions[0].ChampionId, "优先最新对局");

        var champions = new List<MatchRecord>();
        for (var index = 1; index <= 6; index++)
        {
            champions.Add(Match(600 + index, 420, index, index, true, 1, 1, 1));
        }

        var aggregated = MatchStatsCalculator.Calculate(champions, 0, 420, 20, currentModeFirst: false);
        Eq(6, aggregated.Games, "全部场次");
        Eq(4, aggregated.RecentChampions.Count, "英雄聚合最多展示 4 个");
        Eq(6, aggregated.RecentChampions[0].ChampionId, "英雄聚合按最近使用排序");

        var empty = MatchStatsCalculator.Calculate(Array.Empty<MatchRecord>(), 103, 420, 20, currentModeFirst: true);
        Eq(0, empty.Games, "空战绩场次");
        Check(!empty.HasGames, "空战绩 HasGames");
        Close(0, empty.WinRate, "空战绩胜率");
        Close(0, empty.Kda, "空战绩 KDA");
        Check(empty.CurrentChampion is null, "空战绩无当前英雄");
        Eq(0, empty.RecentChampions.Count, "空战绩无英雄聚合");
    }

    private static void TestMatchRecord()
    {
        Check(Match(1, 420, 1, 1, true, 0, 0, 0, durationSeconds: 299).IsRemake, "299 秒为重开");
        Check(!Match(1, 420, 1, 1, true, 0, 0, 0, durationSeconds: 300).IsRemake, "300 秒不为重开");
        Check(!Match(1, 420, 1, 1, true, 0, 0, 0, durationSeconds: 0).IsRemake, "未知时长不为重开");
        Check(Match(1, 420, 1, 1, true, 0, 0, 0, type: "TUTORIAL").IsTutorial, "教程判定");
        Check(!Match(1, 420, 1, 1, true, 0, 0, 0, type: "MATCHED_GAME").IsTutorial, "常规对局");
        Close(5, Match(1, 420, 1, 1, true, 5, 0, 0).Kda, "零死亡 KDA 防除零");
    }

    private static void TestSummoner()
    {
        var profile = SummonerParser.Parse(Fixture("summoner.json"), string.Empty, 0, string.Empty);
        Check(profile is not null, "资料解析");
        Eq("33333333-3333-4333-8333-333333333333", profile!.Puuid, "puuid");
        Eq(4242L, profile.SummonerId, "summonerId");
        Eq("测试召唤师", profile.DisplayName, "displayName");
        Eq("TestName", profile.GameName, "gameName");
        Eq("CN1", profile.TagLine, "tagLine");
        Eq(4568, profile.ProfileIconId, "profileIconId");
        Eq(321, profile.SummonerLevel, "summonerLevel");
        Eq("测试召唤师", profile.ShortName, "ShortName 优先 displayName");

        var gameNameOnly = SummonerParser.Parse(
            "{\"gameName\":\"GameNameOnly\",\"puuid\":\"p\"}", "p", 0, "fallback");
        Eq("GameNameOnly", gameNameOnly!.DisplayName, "缺少 displayName 时回退 gameName");

        var fallback = SummonerParser.Parse("{\"puuid\":\"p\"}", "p", 0, "回退名");
        Eq("回退名", fallback!.DisplayName, "缺少名称时回退入参");

        var puuidName = SummonerParser.Parse("{\"puuid\":\"abcdef12-3456-4789-8abc-def012345678\"}", string.Empty, 0, string.Empty);
        Eq("玩家 abcdef", puuidName!.DisplayName, "仅有 PUUID 时生成占位名");

        Check(SummonerParser.Parse("", "p", 0, "x") is null, "空输入");
        Check(SummonerParser.Parse("[]", "p", 0, "x") is null, "空数组");
    }

    private static void TestRankedStats()
    {
        var rank = RankedStatsParser.Parse(Fixture("ranked-stats.json"));
        Check(rank is not null, "段位解析");
        Eq("RANKED_SOLO_5x5", rank!.QueueType, "优先单双排");
        Eq("EMERALD", rank.Tier, "tier 大写");
        Eq("翡翠", rank.TierCn, "tier 中文");
        Eq("III", rank.Division, "division");
        Eq(77, rank.LeaguePoints, "LP");
        Eq(30, rank.Wins, "胜场");
        Eq(20, rank.Losses, "负场");
        Eq(50, rank.TotalGames, "总场次");
        Eq("翡翠 III 77LP", rank.DisplayText, "段位展示");
        Eq("单双排", rank.QueueLabel, "队列标签");
        Eq("emerald", rank.ColorKey, "颜色键");

        var flexFallback = RankedStatsParser.Parse("""
            {"queueMap":{
              "RANKED_SOLO_5x5":{"tier":"UNRANKED","division":"","leaguePoints":0,"wins":0,"losses":0},
              "RANKED_FLEX_SR":{"tier":"GOLD","division":"II","leaguePoints":40,"wins":10,"losses":5}}}
            """);
        Eq("RANKED_FLEX_SR", flexFallback!.QueueType, "单双排未定级时使用灵活排位");
        Eq("黄金", flexFallback.TierCn, "灵活排位中文段位");

        Check(RankedStatsParser.Parse("""
            {"queueMap":{"RANKED_SOLO_5x5":{"tier":"UNRANKED"},"RANKED_FLEX_SR":{"tier":"NONE"}}}
            """) is null, "全部未定级返回 null");
        Check(RankedStatsParser.Parse("{}") is null, "缺少 queueMap 返回 null");
        Check(RankedStatsParser.Parse("") is null, "空输入");

        Eq("大师 123LP", new RankInfo { Tier = "MASTER", TierCn = "大师", LeaguePoints = 123 }.DisplayText,
            "大师无小段位");
        Eq("未定级", new RankInfo { Tier = "UNRANKED" }.DisplayText, "未定级展示");
        Eq("黄金 III 45LP", new RankInfo { Tier = "GOLD", TierCn = "黄金", Division = "III", LeaguePoints = 45 }.DisplayText,
            "常规段位展示");
        Eq("灵活排位", new RankInfo { QueueType = "RANKED_FLEX_SR" }.QueueLabel, "灵活排位标签");
        Eq("排位", new RankInfo { QueueType = "OTHER" }.QueueLabel, "默认排队标签");
        Eq("王者", RankTierNames.ToChinese("challenger"), "王者");
        Eq("未定级", RankTierNames.ToChinese("unknown"), "未知段位");
    }

    private static void TestSettingsNormalize()
    {
        var settings = new AppSettings
        {
            RecentGames = 15,
            PostGameSeconds = 45,
            Opacity = 0.5,
            ClientPath = "  ",
            FallbackPath = " C:\\tools\\helper.exe ",
            FallbackArguments = null
        };
        settings.Normalize();
        Eq(20, settings.RecentGames, "非法场次归默认");
        Eq(30, settings.PostGameSeconds, "非法保留时长归默认");
        Close(0.7, settings.Opacity, "透明度下限");
        Check(settings.ClientPath is null, "空白路径归一为 null");
        Eq("C:\\tools\\helper.exe", settings.FallbackPath, "兜底路径去空白");
        Eq(string.Empty, settings.FallbackArguments, "参数默认空字符串");

        var upper = new AppSettings { Opacity = 2.5, RecentGames = 30, PostGameSeconds = -1 };
        upper.Normalize();
        Close(1.0, upper.Opacity, "透明度上限");
        Eq(30, upper.RecentGames, "合法场次保留");
        Eq(-1, upper.PostGameSeconds, "一直保留");
    }

    private static void TestSettingsRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LolStatsCheckTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new LogService(Path.Combine(directory, "logs"));
            var service = new SettingsService(directory, log);
            Eq(20, service.Current.RecentGames, "默认场次");
            Check(service.Current.ShowRank, "默认显示段位");
            Check(!service.Current.DisclaimerAccepted, "默认未确认免责声明");

            var draft = service.Current.Clone();
            draft.RecentGames = 10;
            draft.ShowRank = false;
            draft.AlwaysOnTop = false;
            draft.FallbackPath = "C:\\tools\\helper.exe";
            draft.DisclaimerAccepted = true;
            service.Save(draft);
            Check(File.Exists(service.ConfigPath), "配置文件应写入");

            var reloaded = new SettingsService(directory, log);
            Eq(10, reloaded.Current.RecentGames, "场次持久化");
            Check(!reloaded.Current.ShowRank, "显示段位持久化");
            Check(!reloaded.Current.AlwaysOnTop, "置顶持久化");
            Eq("C:\\tools\\helper.exe", reloaded.Current.FallbackPath, "兜底路径持久化");
            Check(reloaded.Current.DisclaimerAccepted, "免责声明持久化");

            File.WriteAllText(service.ConfigPath, "{ this is not valid json");
            var corrupted = new SettingsService(directory, log);
            Eq(20, corrupted.Current.RecentGames, "损坏配置回退默认值");
            Check(corrupted.Current.DisclaimerAccepted == false, "损坏配置不保留确认状态");
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TestQueueCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LolStatsCheckQueueTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new QueueCatalog(() => null, new LogService(directory));
            catalog.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
            Eq("单双排", catalog.GetName(420), "排位");
            Eq("极地大乱斗", catalog.GetName(450), "大乱斗");
            Eq("海克斯大乱斗", catalog.GetName(2400), "海克斯大乱斗");
            Eq("海克斯大乱斗 巅峰赛", catalog.GetName(2410), "海克斯大乱斗巅峰赛");
            Eq("无限乱斗", catalog.GetName(900), "无限乱斗");
            Eq("冰雪无限火力", catalog.GetName(1010), "冰雪无限火力");
            Eq("神木之门", catalog.GetName(2300), "神木之门");
            Eq("快速匹配", catalog.GetName(490), "快速匹配");
            Eq("自定义", catalog.GetName(0), "自定义");
            Eq("队列 12345", catalog.GetName(12345), "未知队列");
            Eq("未知模式", catalog.GetName(-1), "非法队列");

            Eq(420, GameflowParser.ParseLobbyQueueId("""{"gameConfig":{"queueId":420}}"""),
                "大厅队列 queueId");
            Eq(2400, GameflowParser.ParseLobbyQueueId("""{"gameConfig":{"queue":{"id":2400}}}"""),
                "大厅队列 queue.id");
            Eq(0, GameflowParser.ParseLobbyQueueId("""{"gameConfig":{}}"""), "大厅无队列");
            Eq(0, GameflowParser.ParseLobbyQueueId(""), "空大厅");
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TestFallbackResolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LolStatsCheckFallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exe = Path.Combine(directory, "LeagueAkari.exe");
            File.WriteAllText(exe, "stub");
            Eq(exe, FallbackLauncher.ResolvePath(exe), "兜底工具绝对路径");
            Eq(exe, FallbackLauncher.ResolvePath($"  \"{exe}\"  "), "兜底工具路径引号与空白");
            Check(FallbackLauncher.IsAvailable(exe), "兜底工具可用性");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TestMockData()    {
        var snapshot = MockData.CreateSnapshot(123456, tick: 0);
        Eq(MockData.MockQueueId, snapshot.QueueId, "Mock 队列");
        Eq(123456L, snapshot.GameId, "Mock 对局 ID");
        Eq(MockData.LocalCellId, snapshot.LocalPlayerCellId, "Mock 本机 cellId");
        Eq(5, snapshot.MyTeam.Count, "Mock 我方人数");
        Eq(5, snapshot.TheirTeam.Count, "Mock 对方人数");
        Eq(1, snapshot.AllPlayers.Count(p => p.IsLocalPlayer), "Mock 本机玩家唯一");
        Eq(2, snapshot.AllPlayers.Count(p => !p.IsVisible), "Mock 隐藏玩家数量");
        Check(snapshot.AllPlayers.Where(p => !p.IsVisible).All(p =>
            p.Puuid.Length == 0 && p.SummonerId == 0 && p.DisplayName.Length == 0), "Mock 隐藏身份清空");
        Eq(103, snapshot.MyTeam[2].ChampionId, "Mock 本机初始英雄");

        var later = MockData.CreateSnapshot(123456, tick: 30);
        Eq(245, later.MyTeam[2].ChampionId, "Mock 英雄轮换");
        Check(later.SessionKey != snapshot.SessionKey, "Mock 英雄变化时刷新会话键");

        var jungle = MockData.Definitions.First(d => d.Name == "野区厂长");
        var data = MockData.CreatePlayerData(jungle, jungle.BaseChampionId, DateTimeOffset.Parse("2025-06-30T00:00:00Z"));
        Eq("野区厂长", data.Profile.DisplayName, "Mock 资料名称");
        Check(data.Rank is not null, "Mock 段位");
        Check(data.Stats.HasGames, "Mock 战绩");
        Eq(20, data.Stats.Games, "Mock 战绩按默认场次截断");
        Check(data.Stats.CurrentChampion is not null, "Mock 当前英雄聚合");

        var hiddenDefinition = MockData.Definitions.First(d => !d.Visible);
        Check(MockData.CreateRank(hiddenDefinition) is null, "Mock 隐藏玩家无段位");
        Check(MockData.CreateMatches(hiddenDefinition, 1, DateTimeOffset.Now).Count == 0, "Mock 隐藏玩家无战绩");
    }

    private static void TestRealFixture(string path)
    {
        Check(File.Exists(path), "样本文件不存在");
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("games", out var gamesNode))
        {
            var games = gamesNode.ValueKind == JsonValueKind.Array
                ? gamesNode
                : gamesNode.GetProperty("games");
            Check(games.GetArrayLength() > 0, "样本对局为空");

            var puuid = FindIdentityPuuid(games[0]);
            var result = MatchHistoryParser.Parse(json, puuid);
            Check(result.Matches.Count > 0, "历史样本未解析出任何对局");
            Check(result.Matches.Count <= result.TotalAvailable, "解析数不应超过总可用数");
            Console.WriteLine($"      puuid={Shorten(puuid)} 对局={result.Matches.Count}/{result.TotalAvailable} " +
                              $"首场={result.Matches[0].ChampionId}/{result.Matches[0].QueueId}");

            var champIds = result.Matches.Select(m => m.ChampionId).Where(id => id > 0).ToList();
            Check(champIds.Count > 0, "历史样本缺少英雄 ID");
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("participants", out _))
        {
            var puuid = FindIdentityPuuid(root);
            var wrapped = "{\"games\":{\"gameCount\":1,\"games\":[" + json + "]}}";
            var result = MatchHistoryParser.Parse(wrapped, puuid);
            Eq(1, result.Matches.Count, "单局详情样本");
            Console.WriteLine($"      puuid={Shorten(puuid)} 英雄={result.Matches[0].ChampionId} " +
                              $"队列={result.Matches[0].QueueId} 时长={result.Matches[0].DurationSeconds}s");
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            var puuid = FindIdentityPuuid(root[0]);
            var result = MatchHistoryParser.Parse(json, puuid);
            Check(result.Matches.Count > 0, "数组样本未解析出对局");
            Console.WriteLine($"      puuid={Shorten(puuid)} 对局={result.Matches.Count}/{result.TotalAvailable}");
        }
        else
        {
            throw new InvalidOperationException("无法识别的样本结构");
        }
    }

    private static string FindIdentityPuuid(JsonElement game)
    {
        Check(game.TryGetProperty("participantIdentities", out var identities) &&
              identities.ValueKind == JsonValueKind.Array, "样本缺少 participantIdentities");
        foreach (var identity in identities.EnumerateArray())
        {
            if (identity.TryGetProperty("player", out var player))
            {
                var puuid = player.GetString("puuid");
                if (!string.IsNullOrWhiteSpace(puuid))
                {
                    return puuid!;
                }
            }
        }

        throw new InvalidOperationException("样本缺少 PUUID");
    }

    private static string Shorten(string puuid) =>
        puuid.Length <= 8 ? puuid : puuid[..8] + "…";

    private static MatchRecord Match(
        long gameId,
        int queueId,
        int daysAgo,
        int championId,
        bool win,
        int kills,
        int deaths,
        int assists,
        int durationSeconds = 1800,
        string type = "MATCHED_GAME")
    {
        var creation = daysAgo <= 0
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(daysAgo);
        return new MatchRecord(gameId, queueId, creation, durationSeconds, championId, win,
            kills, deaths, assists, type, type == "TUTORIAL" ? "TUTORIAL" : "CLASSIC");
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Eq<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{context}：期望 {expected}，实际 {actual}");
        }
    }

    private static void Close(double expected, double actual, string context)
    {
        if (Math.Abs(expected - actual) > 1e-9)
        {
            throw new InvalidOperationException($"{context}：期望 {expected}，实际 {actual}");
        }
    }
}
