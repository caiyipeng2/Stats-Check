using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using LolStatsCheck.App.Services;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.App.ViewModels;

public sealed class PlayerCardViewModel : ObservableObject, IDisposable
{
    private readonly ImageCache _images;
    private readonly Func<int, string> _championName;
    private readonly Action<string> _retry;
    private readonly Action _fallback;
    private readonly RelayCommand _retryCommand;
    private readonly RelayCommand _fallbackCommand;

    private string _lastChipSignature = string.Empty;
    private int _championId;
    private int _pickIntentId;
    private int _profileIconId;
    private int _lastDisplayChampion;
    private bool _disposed;

    private string _displayName = string.Empty;
    private string _championInitial = "?";
    private string _profileInitial = "?";
    private string _positionText = string.Empty;
    private string _championLine = string.Empty;
    private string _championTooltip = string.Empty;
    private string _rankText = string.Empty;
    private string _winRateText = "--";
    private string _recordText = string.Empty;
    private string _kdaText = string.Empty;
    private string _kdaTooltip = string.Empty;
    private string _streakText = string.Empty;
    private string _issueTitle = "暂无数据";
    private string _issueText = string.Empty;
    private bool _showHidden;
    private bool _showReady;
    private bool _showLoading;
    private bool _showIssue;
    private bool _showLocal;
    private bool _showAutofilled;
    private bool _showMock;
    private bool _hasRank;
    private bool _hasPosition;
    private bool _hasRecent;
    private bool _hasStreak;
    private bool _canRetry;
    private bool _canUseFallback;
    private Brush _accentBrush = Palette.Ally;
    private Brush _championAccentBrush = Palette.ChampionAccent(0);
    private Brush _winRateBrush = Palette.Muted;
    private Brush _rankBrush = Palette.Muted;
    private Brush _streakBrush = Palette.Muted;
    private ImageSource? _championImage;
    private ImageSource? _profileImage;

    public PlayerCardViewModel(
        ImageCache images,
        Func<int, string> championName,
        Action<string> retry,
        Action fallback)
    {
        _images = images;
        _championName = championName;
        _retry = retry;
        _fallback = fallback;
        _retryCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(Key))
            {
                _retry(Key);
            }
        });
        _fallbackCommand = new RelayCommand(_fallback);
        _images.IconLoaded += OnIconLoaded;
    }

    public string Key { get; private set; } = string.Empty;

    public ObservableCollection<ChampionChipViewModel> RecentChampions { get; } = new();

    public ICommand RetryCommand => _retryCommand;
    public ICommand FallbackCommand => _fallbackCommand;

    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
    public string ChampionInitial { get => _championInitial; private set => Set(ref _championInitial, value); }
    public string ProfileInitial { get => _profileInitial; private set => Set(ref _profileInitial, value); }
    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }
    public string ChampionLine { get => _championLine; private set => Set(ref _championLine, value); }
    public string ChampionTooltip { get => _championTooltip; private set => Set(ref _championTooltip, value); }
    public string RankText { get => _rankText; private set => Set(ref _rankText, value); }
    public string WinRateText { get => _winRateText; private set => Set(ref _winRateText, value); }
    public string RecordText { get => _recordText; private set => Set(ref _recordText, value); }
    public string KdaText { get => _kdaText; private set => Set(ref _kdaText, value); }
    public string KdaTooltip { get => _kdaTooltip; private set => Set(ref _kdaTooltip, value); }
    public string StreakText { get => _streakText; private set => Set(ref _streakText, value); }
    public string IssueTitle { get => _issueTitle; private set => Set(ref _issueTitle, value); }
    public string IssueText { get => _issueText; private set => Set(ref _issueText, value); }

    public bool ShowHidden { get => _showHidden; private set => Set(ref _showHidden, value); }
    public bool ShowReady { get => _showReady; private set => Set(ref _showReady, value); }
    public bool ShowLoading { get => _showLoading; private set => Set(ref _showLoading, value); }
    public bool ShowIssue { get => _showIssue; private set => Set(ref _showIssue, value); }
    public bool ShowLocal { get => _showLocal; private set => Set(ref _showLocal, value); }
    public bool ShowAutofilled { get => _showAutofilled; private set => Set(ref _showAutofilled, value); }
    public bool ShowMock { get => _showMock; private set => Set(ref _showMock, value); }
    public bool HasRank { get => _hasRank; private set => Set(ref _hasRank, value); }
    public bool HasPosition { get => _hasPosition; private set => Set(ref _hasPosition, value); }
    public bool HasRecent { get => _hasRecent; private set => Set(ref _hasRecent, value); }
    public bool HasStreak { get => _hasStreak; private set => Set(ref _hasStreak, value); }
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    public bool CanUseFallback { get => _canUseFallback; private set => Set(ref _canUseFallback, value); }

    public Brush AccentBrush { get => _accentBrush; private set => Set(ref _accentBrush, value); }
    public Brush ChampionAccentBrush { get => _championAccentBrush; private set => Set(ref _championAccentBrush, value); }
    public Brush WinRateBrush { get => _winRateBrush; private set => Set(ref _winRateBrush, value); }
    public Brush RankBrush { get => _rankBrush; private set => Set(ref _rankBrush, value); }
    public Brush StreakBrush { get => _streakBrush; private set => Set(ref _streakBrush, value); }

    public ImageSource? ChampionImage { get => _championImage; private set => Set(ref _championImage, value); }
    public ImageSource? ProfileImage { get => _profileImage; private set => Set(ref _profileImage, value); }

    public void Update(PlayerCardState state, bool isMock)
    {
        Key = state.Key;
        var hidden = state.Status == PlayerCardStatus.Hidden;
        AccentBrush = Palette.TeamAccent(state.Team, state.IsLocalPlayer);
        ShowLocal = state.IsLocalPlayer;
        ShowAutofilled = state.IsAutofilled;
        ShowMock = isMock;

        DisplayName = hidden
            ? "身份未公开"
            : string.IsNullOrWhiteSpace(state.DisplayName) ? "玩家" : state.DisplayName;
        ProfileInitial = Palette.Initial(DisplayName);
        PositionText = state.PositionText == "待定" ? string.Empty : state.PositionText;
        HasPosition = !string.IsNullOrWhiteSpace(PositionText);

        _championId = state.ChampionId;
        _pickIntentId = state.PickIntentChampionId;
        var displayChampion = state.ChampionId > 0 ? state.ChampionId : state.PickIntentChampionId;
        var championName = state.ChampionName;
        ChampionInitial = Palette.Initial(championName, "?");
        ChampionLine = displayChampion > 0
            ? $"{(state.ChampionId > 0 ? "锁定" : "预选")} · {(string.IsNullOrWhiteSpace(championName) ? "未知英雄" : championName)}"
            : hidden ? "英雄未公开" : "尚未选择英雄";
        ChampionAccentBrush = Palette.ChampionAccent(displayChampion);

        if (displayChampion != _lastDisplayChampion)
        {
            _lastDisplayChampion = displayChampion;
            ChampionImage = _images.GetChampion(displayChampion);
        }

        _images.PreloadChampion(state.ChampionId);
        _images.PreloadChampion(state.PickIntentChampionId);

        var iconId = state.Profile?.ProfileIconId ?? 0;
        if (iconId != _profileIconId)
        {
            _profileIconId = iconId;
            ProfileImage = _images.GetProfile(iconId);
        }

        RankText = state.Rank?.DisplayText ?? string.Empty;
        HasRank = state.Rank is { TotalGames: > 0 } && !string.IsNullOrWhiteSpace(RankText);
        RankBrush = Palette.Rank(state.Rank?.Tier ?? string.Empty);

        var stats = state.Stats;
        var hasStats = stats is { Games: > 0 };
        ShowHidden = hidden;
        ShowReady = !hidden && hasStats;
        ShowLoading = !hidden && !hasStats && state.Status == PlayerCardStatus.Loading;
        ShowIssue = !hidden && !hasStats && state.Status is PlayerCardStatus.Unavailable or PlayerCardStatus.Error;
        CanRetry = state.CanRetry;
        CanUseFallback = state.CanUseFallback;

        if (hasStats)
        {
            WinRateText = $"{stats!.WinRate:0}%";
            WinRateBrush = Palette.WinRate(stats.WinRate, true);
            RecordText = $"{stats.Wins} 胜 {stats.Losses} 负 · 近 {stats.Games} 场";
            KdaText = $"{stats.Kda:0.0} KDA";
            KdaTooltip = $"场均 {stats.AvgKills:0.0} / {stats.AvgDeaths:0.0} / {stats.AvgAssists:0.0}";
            if (stats.WinStreak > 0)
            {
                StreakText = $"{stats.WinStreak} 连胜";
                StreakBrush = Palette.Win;
                HasStreak = true;
            }
            else if (stats.LossStreak > 0)
            {
                StreakText = $"{stats.LossStreak} 连败";
                StreakBrush = Palette.Loss;
                HasStreak = true;
            }
            else if (!string.IsNullOrWhiteSpace(state.Message))
            {
                StreakText = state.Message!;
                StreakBrush = Palette.Muted;
                HasStreak = true;
            }
            else
            {
                StreakText = string.Empty;
                HasStreak = false;
            }
        }
        else
        {
            WinRateText = "--";
            WinRateBrush = Palette.Muted;
            RecordText = string.Empty;
            KdaText = string.Empty;
            KdaTooltip = string.Empty;
            StreakText = string.Empty;
            HasStreak = false;
        }

        if (ShowIssue)
        {
            IssueTitle = state.Status == PlayerCardStatus.Error ? "查询失败" : "暂无数据";
            IssueText = state.Message ?? (state.Status == PlayerCardStatus.Error
                ? "客户端接口暂时不可用"
                : "未获取到可用战绩");
        }

        var tooltip = string.IsNullOrWhiteSpace(championName) ? "尚未选择英雄" : championName;
        if (stats?.CurrentChampion is { Games: > 0 } current)
        {
            tooltip += $"\n本英雄 {current.Games} 场 · {current.WinRate:0}% 胜率";
        }

        ChampionTooltip = tooltip;
        UpdateRecentChampions(stats);
    }

    private void UpdateRecentChampions(PlayerStats? stats)
    {
        var aggregates = stats?.RecentChampions;
        if (aggregates is null || aggregates.Count == 0)
        {
            if (RecentChampions.Count > 0)
            {
                RecentChampions.Clear();
            }

            _lastChipSignature = string.Empty;
            HasRecent = false;
            return;
        }

        var top = aggregates.Take(4).ToList();
        var signature = string.Join("|", top.Select(item => $"{item.ChampionId}:{item.Games}:{item.Wins}"));
        if (signature != _lastChipSignature)
        {
            _lastChipSignature = signature;
            RecentChampions.Clear();
            foreach (var aggregate in top)
            {
                var chip = new ChampionChipViewModel(
                    aggregate,
                    _championName(aggregate.ChampionId),
                    Palette.ChampionAccent(aggregate.ChampionId));
                var image = _images.GetChampion(aggregate.ChampionId);
                if (image is not null)
                {
                    chip.SetImage(image);
                }

                RecentChampions.Add(chip);
            }
        }

        HasRecent = RecentChampions.Count > 0;
    }

    private void OnIconLoaded(object? sender, IconLoadedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        if (args.Kind == IconKind.Champion)
        {
            if (args.Id == _championId || args.Id == _pickIntentId)
            {
                ChampionImage = args.Image;
            }

            foreach (var chip in RecentChampions)
            {
                if (chip.ChampionId == args.Id)
                {
                    chip.SetImage(args.Image);
                }
            }
        }
        else if (args.Id == _profileIconId)
        {
            ProfileImage = args.Image;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _images.IconLoaded -= OnIconLoaded;
    }
}
