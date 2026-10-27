using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LolStatsCheck.App.Services;
using LolStatsCheck.Core.Models;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.App.ViewModels;

public sealed class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly SessionCoordinator _coordinator;
    private readonly LogService _log;
    private readonly ImageCache _images;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, PlayerCardViewModel> _cards = new(StringComparer.Ordinal);

    private bool _disposed;
    private string _statusText = "等待客户端";
    private string _statusDetail = string.Empty;
    private string _modeText = string.Empty;
    private string _banner = string.Empty;
    private string _updatedText = "尚未更新";
    private string _footerSourceText = "仅读取本机客户端";
    private string _emptyTitle = "等待英雄联盟客户端";
    private string _emptyDetail = "启动并登录客户端后，工具会自动连接。进入 BP 阶段将自动显示双方战绩。";
    private string _myVisibleText = string.Empty;
    private string _theirVisibleText = string.Empty;
    private bool _hasMode;
    private bool _hasBanner;
    private bool _hasSession;
    private bool _isEmpty = true;
    private bool _isMock;
    private Brush _statusBrush = Palette.StatusDisconnected;

    public OverlayViewModel(
        SessionCoordinator coordinator,
        LogService log,
        ImageCache images)
    {
        _coordinator = coordinator;
        _log = log;
        _images = images;
        _dispatcher = Dispatcher.CurrentDispatcher;

        RefreshCommand = new RelayCommand(RequestRefresh);
        FallbackCommand = new RelayCommand(LaunchFallback);
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        HideCommand = new RelayCommand(() => HideRequested?.Invoke(this, EventArgs.Empty));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version is null ? "v1.0" : $"v{version.Major}.{version.Minor}.{version.Build}";

        _coordinator.Updated += OnCoordinatorUpdated;
        Apply(_coordinator.LastUpdate);
    }

    public ObservableCollection<PlayerCardViewModel> MyTeam { get; } = new();
    public ObservableCollection<PlayerCardViewModel> TheirTeam { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand FallbackCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand HideCommand { get; }

    public string VersionText { get; }

    public event EventHandler<SessionUpdate>? Applied;
    public event EventHandler? SettingsRequested;
    public event EventHandler? HideRequested;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public string ModeText { get => _modeText; private set => Set(ref _modeText, value); }
    public string Banner { get => _banner; private set => Set(ref _banner, value); }
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
    public string FooterSourceText { get => _footerSourceText; private set => Set(ref _footerSourceText, value); }
    public string EmptyTitle { get => _emptyTitle; private set => Set(ref _emptyTitle, value); }
    public string EmptyDetail { get => _emptyDetail; private set => Set(ref _emptyDetail, value); }
    public string MyVisibleText { get => _myVisibleText; private set => Set(ref _myVisibleText, value); }
    public string TheirVisibleText { get => _theirVisibleText; private set => Set(ref _theirVisibleText, value); }

    public bool HasMode { get => _hasMode; private set => Set(ref _hasMode, value); }
    public bool HasBanner { get => _hasBanner; private set => Set(ref _hasBanner, value); }
    public bool HasSession { get => _hasSession; private set => Set(ref _hasSession, value); }
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }
    public bool IsMock { get => _isMock; private set => Set(ref _isMock, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    public SessionUpdate? LastUpdate { get; private set; }

    private void OnCoordinatorUpdated(object? sender, SessionUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            Apply(update);
        }
        else
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    Apply(update);
                }
            });
        }
    }

    public void Apply(SessionUpdate update)
    {
        LastUpdate = update;
        IsMock = update.IsMock;
        StatusText = update.StatusText;
        StatusDetail = update.StatusDetail;
        StatusBrush = ResolveStatusBrush(update);

        var mode = update.ModeName;
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = update.IsChampSelect ? "BP" : update.IsInGame ? "对局中" : string.Empty;
        }

        ModeText = mode;
        HasMode = !string.IsNullOrWhiteSpace(mode);

        Banner = update.Banner ?? string.Empty;
        HasBanner = !string.IsNullOrWhiteSpace(Banner);
        HasSession = update.HasSession;
        IsEmpty = !update.HasSession;
        UpdatedText = update.LastUpdated is null
            ? "尚未更新"
            : $"数据更新 {update.LastUpdated:HH:mm:ss}";
        FooterSourceText = update.IsMock
            ? "模拟数据 · 未连接客户端"
            : "仅读取本机客户端 · 不修改游戏数据 · 不向外部服务器发送数据";
        MyVisibleText = VisibleText(update.MyTeam);
        TheirVisibleText = VisibleText(update.TheirTeam);

        if (update.IsMock)
        {
            EmptyTitle = "模拟对局载入中";
            EmptyDetail = "当前为界面预览数据，不会连接游戏客户端。关闭模拟模式后即可查询真实战绩。";
        }
        else if (update.Connection == ConnectionStatus.Disconnected)
        {
            EmptyTitle = "等待英雄联盟客户端";
            EmptyDetail = "启动并登录客户端后，工具会自动连接。进入排位、匹配或大乱斗的 BP 阶段将自动显示双方战绩。";
        }
        else if (update.IsInGame)
        {
            EmptyTitle = "正在读取本地对局信息";
            EmptyDetail = "对局已经开始，工具会尝试补齐双方战绩。读不到时会保持 BP 阶段的结果。";
        }
        else
        {
            EmptyTitle = "已连接客户端";
            EmptyDetail = "进入排位、匹配或大乱斗的 BP 阶段后，这里会自动显示双方最近战绩。";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        SyncCards(MyTeam, update.MyTeam, update.IsMock, seen);
        SyncCards(TheirTeam, update.TheirTeam, update.IsMock, seen);

        foreach (var key in _cards.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _cards[key].Dispose();
            _cards.Remove(key);
        }

        Applied?.Invoke(this, update);
    }

    private void SyncCards(
        ObservableCollection<PlayerCardViewModel> target,
        IReadOnlyList<PlayerCardState> states,
        bool isMock,
        HashSet<string> seen)
    {
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            if (!_cards.TryGetValue(state.Key, out var card))
            {
                card = new PlayerCardViewModel(
                    _images,
                    ResolveChampionName,
                    RetryPlayer,
                    LaunchFallback);
                _cards[state.Key] = card;
            }

            card.Update(state, isMock);
            seen.Add(state.Key);

            var currentIndex = target.IndexOf(card);
            if (currentIndex < 0)
            {
                target.Insert(Math.Min(index, target.Count), card);
            }
            else if (currentIndex != index)
            {
                target.Move(currentIndex, index);
            }
        }

        while (target.Count > states.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private string ResolveChampionName(int championId) =>
        championId <= 0
            ? string.Empty
            : _coordinator.IsMock
                ? MockData.GetChampionName(championId)
                : _coordinator.Champions.GetName(championId);

    private void RetryPlayer(string key)
    {
        _log.Info($"手动重试玩家：{key}");
        _coordinator.RetryPlayer(key);
    }

    private void RequestRefresh()
    {
        _log.Info("界面请求刷新数据");
        _coordinator.RequestRefresh();
    }

    private void LaunchFallback()
    {
        var result = _coordinator.LaunchFallback();
        if (result.Status is FallbackLaunchStatus.NotConfigured or FallbackLaunchStatus.PathMissing)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string VisibleText(IReadOnlyList<PlayerCardState> cards)
    {
        if (cards.Count == 0)
        {
            return string.Empty;
        }

        var visible = cards.Count(card => card.Status != PlayerCardStatus.Hidden);
        return $"公开 {visible}/{cards.Count}";
    }

    private static Brush ResolveStatusBrush(SessionUpdate update)
    {
        if (update.IsMock)
        {
            return Palette.Gold;
        }

        if (update.Connection == ConnectionStatus.Disconnected)
        {
            return Palette.StatusDisconnected;
        }

        if (update.IsChampSelect)
        {
            return Palette.StatusChampSelect;
        }

        return update.IsInGame ? Palette.StatusInGame : Palette.StatusConnected;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.Updated -= OnCoordinatorUpdated;
        foreach (var card in _cards.Values)
        {
            card.Dispose();
        }

        _cards.Clear();
        MyTeam.Clear();
        TheirTeam.Clear();
    }
}
