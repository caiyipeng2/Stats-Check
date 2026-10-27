using System.Text;
using System.Text.Json;
using LolStatsCheck.Core.Lcu;
using LolStatsCheck.Core.Models;
using LolStatsCheck.Core.Util;

namespace LolStatsCheck.Core.Services;

public sealed partial class SessionCoordinator : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly bool _mock;
    private readonly bool _diagnose;
    private readonly LcuLocator _locator;
    private readonly ChampionCatalog _champions;
    private readonly QueueCatalog _queues;
    private readonly PlayerDataService _playerData;
    private readonly SummonerResolver _resolver;
    private readonly LiveClientService _live;
    private readonly FallbackLauncher _fallback;
    private readonly SemaphoreSlim _liveResolveGate = new(3, 3);
    private readonly SemaphoreSlim _playerLoadGate = new(3, 3);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, PlayerRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autoLaunched = new(StringComparer.Ordinal);
    private readonly HashSet<string> _diagnosedSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<(TeamSide Team, int Slot), MockPlayerDefinition> _mockDefinitions;

    private LcuClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private SessionUpdate _last;
    private string _lastSignature = string.Empty;
    private ChampSelectSnapshot? _snapshot;
    private string _sessionKey = string.Empty;
    private long _sessionGameId;
    private int _queueId;
    private string _modeName = string.Empty;
    private string _phase = "None";
    private ConnectionStatus _connection = ConnectionStatus.Disconnected;
    private DateTimeOffset? _postGameUntil;
    private DateTimeOffset _lastUpdated = DateTimeOffset.MinValue;
    private DateTimeOffset _nextGameflowProbe = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLiveProbe = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLobbyProbe = DateTimeOffset.MinValue;
    private int _idleQueueId;
    private string _idleModeName = string.Empty;
    private int _lobbyMisses;
    private string? _banner;
    private string _liveStatus = string.Empty;
    private int _forceRefreshRequested;
    private DateTimeOffset _mockResetAt = DateTimeOffset.Now;
    private bool _started;
    private bool _disposed;

    public SessionCoordinator(
        SettingsService settings, LogService log, bool mock = false, bool diagnose = false)
    {
        _settings = settings;
        _log = log;
        _mock = mock;
        _diagnose = diagnose;

        _locator = new LcuLocator(log);
        _champions = new ChampionCatalog(() => _client, log);
        _queues = new QueueCatalog(() => _client, log);
        _playerData = new PlayerDataService(() => _client, log);
        _resolver = new SummonerResolver(() => _client, log);
        _live = new LiveClientService(log);
        _fallback = new FallbackLauncher(log);
        _mockDefinitions = MockData.Definitions.ToDictionary(d => (d.Team, d.Slot));

        _last = _mock
            ? SessionUpdate.Empty(ConnectionStatus.Connected, "模拟模式",
                "正在载入模拟对局…", "模拟模式", true)
            : SessionUpdate.Empty(ConnectionStatus.Disconnected, "等待客户端",
                "启动英雄联盟客户端后会自动连接。", "等待客户端");
        _phase = _mock ? "ChampSelect" : "None";
    }

    public event EventHandler<SessionUpdate>? Updated;

    public bool IsMock => _mock;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _started;
            }
        }
    }

    public ChampionCatalog Champions => _champions;

    public PlayerDataService PlayerData => _playerData;

    public SessionUpdate LastUpdate
    {
        get
        {
            lock (_stateLock)
            {
                return _last;
            }
        }
    }

    public Task StartAsync()
    {
        CancellationToken token;
        lock (_stateLock)
        {
            if (_disposed || _started)
            {
                return Task.CompletedTask;
            }

            _started = true;
            _cts = new CancellationTokenSource();
            _mockResetAt = DateTimeOffset.Now;
            token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }

        EmitCurrent(force: true);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_stateLock)
        {
            if (!_started && _cts is null)
            {
                return;
            }

            _started = false;
            cts = _cts;
            _cts = null;
            loop = _loop;
            _loop = null;
        }

        cts?.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        LcuClient? client;
        lock (_stateLock)
        {
            client = _client;
            _client = null;
            _connection = ConnectionStatus.Disconnected;
        }

        client?.Dispose();
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _live.Dispose();
    }

    public void RequestRefresh()
    {
        _log.Info("收到手动刷新请求");
        _playerData.ClearAll();
        Interlocked.Exchange(ref _forceRefreshRequested, 1);
        lock (_stateLock)
        {
            _banner = null;
        }

        if (_mock)
        {
            ResetMockLoading();
            EmitCurrent();
        }
    }

    public void RetryPlayer(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || _disposed)
        {
            return;
        }

        if (_mock)
        {
            RetryMockPlayer(key);
            return;
        }

        PlayerRuntime? runtime;
        int version;
        CancellationToken token;
        var needsResolve = false;
        lock (_stateLock)
        {
            if (!_runtimes.TryGetValue(key, out runtime) || !runtime.IsVisible)
            {
                return;
            }

            token = _cts?.Token ?? CancellationToken.None;
            if (!token.CanBeCanceled)
            {
                return;
            }

            if (!ChampSelectParser.IsValidPuuid(runtime.Puuid))
            {
                if (!IsInGamePhase(_phase))
                {
                    return;
                }

                runtime.ResolveAttempts = 0;
                runtime.ResolveInProgress = false;
                runtime.Status = PlayerCardStatus.Loading;
                runtime.Message = "正在重新解析玩家身份…";
                runtime.CanRetry = false;
                runtime.UpdatedAt = DateTimeOffset.Now;
                _nextLiveProbe = DateTimeOffset.MinValue;
                needsResolve = true;
                version = runtime.Version;
            }
            else
            {
                _playerData.Invalidate(runtime.Puuid);
                runtime.Version++;
                runtime.Loading = false;
                runtime.Status = PlayerCardStatus.Loading;
                runtime.Message = null;
                runtime.CanRetry = false;
                runtime.UpdatedAt = DateTimeOffset.Now;
                version = runtime.Version;
            }
        }

        if (needsResolve)
        {
            EmitCurrent();
            return;
        }

        StartPlayerLoad(runtime!, version, force: true, token);
        EmitCurrent();
    }

    public FallbackLaunchResult LaunchFallback()
    {
        if (_mock)
        {
            var simulated = new FallbackLaunchResult(
                FallbackLaunchStatus.Failed, "模拟模式不会启动外部工具。", null);
            SetBanner(simulated.Message);
            EmitCurrent();
            return simulated;
        }

        var settings = _settings.Current;
        var result = _fallback.Launch(settings.FallbackPath, settings.FallbackArguments);
        SetBanner(result.Message);
        EmitCurrent();
        return result;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _log.Info(_mock ? "进入模拟数据模式" : "开始监听本机客户端");
        try
        {
            if (_mock)
            {
                await MockLoopAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RealLoopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error("监听循环异常退出", ex);
            lock (_stateLock)
            {
                _connection = ConnectionStatus.Disconnected;
            }

            EmitCurrent(force: true);
        }
    }

    private async Task RealLoopAsync(CancellationToken cancellationToken)
    {
        var nextConnectAttempt = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_client is null)
            {
                if (DateTimeOffset.Now >= nextConnectAttempt)
                {
                    nextConnectAttempt = DateTimeOffset.Now.AddSeconds(1.5);
                    try
                    {
                        await TryConnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        _log.Debug("连接尝试失败：" + ex.Message);
                    }
                }

                if (_client is null)
                {
                    await Task.Delay(600, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            var polled = false;
            try
            {
                var client = _client;
                if (client is null)
                {
                    continue;
                }

                var phaseJson = await client
                    .GetStringAsync("/lol-gameflow/v1/gameflow-phase", cancellationToken)
                    .ConfigureAwait(false);
                var phase = GameflowParser.ParsePhase(phaseJson);
                polled = true;

                if (_connection != ConnectionStatus.Connected)
                {
                    lock (_stateLock)
                    {
                        _connection = ConnectionStatus.Connected;
                    }

                    EmitCurrent();
                }

                await HandlePhaseAsync(phase, cancellationToken).ConfigureAwait(false);
            }
            catch (LcuConnectionException ex)
            {
                HandleConnectionLost(ex.Message);
            }
            catch (LcuTimeoutException ex)
            {
                HandleConnectionLost(ex.Message);
            }
            catch (LcuHttpException ex)
            {
                _log.Debug("客户端接口返回错误：" + ex.Message);
            }
            catch (JsonException ex)
            {
                _log.Warn("客户端数据解析失败：" + ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _log.Debug("客户端请求被取消");
            }
            catch (LcuException ex)
            {
                _log.Debug("客户端请求失败：" + ex.Message);
            }

            await Task.Delay(polled ? 1000 : 500, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var location = _locator.Locate(settings.ClientPath);
        if (location is null)
        {
            return;
        }

        var client = new LcuClient(location.Credentials);
        try
        {
            await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LcuException ex)
        {
            client.Dispose();
            _log.Debug("客户端连通性检测失败：" + ex.Message);
            return;
        }

        lock (_stateLock)
        {
            _client = client;
            _connection = ConnectionStatus.Connected;
            _nextGameflowProbe = DateTimeOffset.MinValue;
        }

        _log.Info($"已连接客户端：{location.Credentials}（{location.Source}）");
        SaveDetectedClientPath(location.ClientExePath);
        _ = LoadCatalogsAsync(cancellationToken);
        EmitCurrent();
    }

    private void SaveDetectedClientPath(string clientExePath)
    {
        if (string.IsNullOrWhiteSpace(clientExePath))
        {
            return;
        }

        var settings = _settings.Current;
        if (!string.IsNullOrWhiteSpace(settings.ClientPath))
        {
            return;
        }

        try
        {
            settings.ClientPath = clientExePath;
            _settings.Save(settings);
            _log.Info("已保存自动检测到的客户端路径");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("保存客户端路径失败：" + ex.Message);
        }
    }

    private async Task LoadCatalogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _champions.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            await _queues.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            EmitCurrent();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warn("加载客户端静态数据失败：" + ex.Message);
        }
    }

    private void HandleConnectionLost(string reason)
    {
        LcuClient? client;
        lock (_stateLock)
        {
            client = _client;
            _client = null;
            _connection = ConnectionStatus.Disconnected;
            _liveStatus = string.Empty;
            _queueId = 0;
            _modeName = string.Empty;
            _idleQueueId = 0;
            _idleModeName = string.Empty;
            _lobbyMisses = 0;
        }

        client?.Dispose();
        _log.Warn("客户端连接中断：" + reason);
        EmitCurrent();
    }

    private async Task HandlePhaseAsync(string phase, CancellationToken cancellationToken)
    {
        switch (phase)
        {
            case "ChampSelect":
                await HandleChampSelectAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "GameStart":
            case "InProgress":
            case "Reconnect":
            case "WaitingForStats":
                await HandleInGameAsync(phase, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await HandleIdleAsync(phase, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleIdleAsync(string phase, CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(phase) ? "None" : phase;
        lock (_stateLock)
        {
            ResetSessionLocked();
            _phase = normalized;
            Interlocked.Exchange(ref _forceRefreshRequested, 0);
        }

        if (normalized is "Lobby" or "Matchmaking" or "ReadyCheck" or "None" or "EndOfGame" or "PreEndOfGame")
        {
            await TryReadLobbyModeAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            _queueId = _idleQueueId;
            _modeName = _idleModeName;
        }

        EmitCurrent();
    }

    private async Task TryReadLobbyModeAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_stateLock)
        {
            if (now < _nextLobbyProbe)
            {
                return;
            }

            _nextLobbyProbe = now.AddSeconds(3);
        }

        var queueId = 0;
        var hasLobby = false;
        try
        {
            var lobby = await client.TryGetStringAsync("/lol-lobby/v2/lobby", cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(lobby))
            {
                hasLobby = true;
                queueId = GameflowParser.ParseLobbyQueueId(lobby);
            }

            if (queueId == 0)
            {
                var session = await client.TryGetStringAsync("/lol-gameflow/v1/session", cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(session))
                {
                    queueId = GameflowParser.ParseSession(session).QueueId;
                }
            }
        }
        catch (LcuHttpException ex)
        {
            _log.Debug("读取房间模式失败：" + ex.Message);
            return;
        }
        catch (JsonException ex)
        {
            _log.Debug("解析房间模式失败：" + ex.Message);
            return;
        }

        lock (_stateLock)
        {
            if (queueId > 0)
            {
                _lobbyMisses = 0;
                _idleQueueId = queueId;
                _idleModeName = _queues.GetName(queueId);
            }
            else if (hasLobby)
            {
                _lobbyMisses = 0;
                _idleQueueId = 0;
                _idleModeName = string.Empty;
            }
            else if (++_lobbyMisses >= 3)
            {
                _idleQueueId = 0;
                _idleModeName = string.Empty;
            }
        }
    }

    private async Task HandleChampSelectAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        string sessionJson;
        try
        {
            sessionJson = await client
                .GetStringAsync("/lol-champ-select/v1/session", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LcuHttpException ex)
        {
            _log.Debug("读取 BP 会话失败：" + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionJson))
        {
            return;
        }

        if (_sessionGameId == 0 || _queueId == 0)
        {
            await TryReadGameflowIdentityAsync(cancellationToken).ConfigureAwait(false);
        }

        ChampSelectSnapshot snapshot;
        try
        {
            snapshot = ChampSelectParser.Parse(sessionJson, _sessionGameId, _queueId);
        }
        catch (JsonException ex)
        {
            _log.Warn("解析 BP 会话失败：" + ex.Message);
            return;
        }

        var force = Interlocked.Exchange(ref _forceRefreshRequested, 0) == 1;
        List<(PlayerRuntime Runtime, int Version)> starts;
        bool autoFallback;
        lock (_stateLock)
        {
            var newGame = snapshot.GameId > 0 && _sessionGameId > 0 &&
                          snapshot.GameId != _sessionGameId;
            var newSession = _snapshot is null || newGame ||
                             (_phase != "ChampSelect" &&
                              !string.Equals(_sessionKey, snapshot.SessionKey, StringComparison.Ordinal));

            if (newSession)
            {
                ResetSessionLocked();
            }

            _connection = ConnectionStatus.Connected;
            _phase = "ChampSelect";
            if (snapshot.GameId > 0)
            {
                _sessionGameId = snapshot.GameId;
            }

            if (snapshot.QueueId > 0)
            {
                _queueId = snapshot.QueueId;
            }

            _modeName = _queueId > 0 ? _queues.GetName(_queueId) : string.Empty;
            if (_queueId > 0)
            {
                _idleQueueId = _queueId;
                _idleModeName = _modeName;
                _lobbyMisses = 0;
            }

            _sessionKey = snapshot.SessionKey;
            _snapshot = snapshot;
            _postGameUntil = null;
            _liveStatus = string.Empty;

            var applied = ApplyChampSelectPlayersLocked(snapshot.AllPlayers);
            starts = force ? BeginForceReloadLocked() : applied;
            autoFallback = ShouldAutoLaunchFallbackLocked();
        }

        foreach (var (runtime, version) in starts)
        {
            StartPlayerLoad(runtime, version, force, cancellationToken);
        }

        if (autoFallback)
        {
            StartAutoFallback();
        }

        if (_diagnose)
        {
            LogSessionDiagnostics(snapshot);
        }

        EmitCurrent();
    }

    private async Task HandleInGameAsync(string phase, CancellationToken cancellationToken)
    {
        bool needGameflow;
        bool force;
        List<(PlayerRuntime Runtime, int Version)> forceStarts = new();
        lock (_stateLock)
        {
            _phase = phase;
            _connection = ConnectionStatus.Connected;
            if (string.IsNullOrEmpty(_sessionKey))
            {
                _sessionKey = "ingame";
            }

            _postGameUntil ??= ComputePostGameDeadlineLocked();
            needGameflow = _sessionGameId == 0 || _queueId == 0;
            force = Interlocked.Exchange(ref _forceRefreshRequested, 0) == 1;
            if (force)
            {
                forceStarts = BeginForceReloadLocked();
                foreach (var runtime in _runtimes.Values)
                {
                    runtime.ResolveAttempts = 0;
                    runtime.ResolveInProgress = false;
                }
            }
        }

        if (needGameflow)
        {
            await TryReadGameflowIdentityAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            if (_queueId > 0)
            {
                _modeName = _queues.GetName(_queueId);
            }
            else if (string.IsNullOrEmpty(_modeName))
            {
                _modeName = "对局中";
            }
        }

        foreach (var (runtime, version) in forceStarts)
        {
            StartPlayerLoad(runtime, version, force: true, cancellationToken);
        }

        if (!IsPostGameVisible())
        {
            EmitCurrent();
            return;
        }

        var now = DateTimeOffset.Now;
        if (now < _nextLiveProbe)
        {
            EmitCurrent();
            return;
        }

        _nextLiveProbe = now.AddSeconds(5);

        LiveGameSnapshot? live;
        try
        {
            live = await _live.TryGetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Debug("读取本地对局数据失败：" + ex.Message);
            live = null;
        }

        List<(PlayerRuntime Runtime, int Version)> starts = new();
        List<LiveResolveWork> resolves = new();
        lock (_stateLock)
        {
            if (live is null)
            {
                _liveStatus = "本地对局数据尚未就绪，继续等待…";
            }
            else
            {
                var result = ApplyLiveSnapshotLocked(live);
                starts = result.Starts;
                resolves = result.Resolves;
                _liveStatus = result.Status;
            }
        }

        foreach (var work in resolves)
        {
            StartLiveResolve(work, cancellationToken);
        }

        foreach (var (runtime, version) in starts)
        {
            StartPlayerLoad(runtime, version, force: true, cancellationToken);
        }

        EmitCurrent();
    }

    private async Task TryReadGameflowIdentityAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_stateLock)
        {
            if (now < _nextGameflowProbe)
            {
                return;
            }

            _nextGameflowProbe = now.AddSeconds(2);
        }

        var gameId = 0L;
        var queueId = 0;
        try
        {
            var json = await client.TryGetStringAsync("/lol-gameflow/v1/session", cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                (gameId, queueId) = GameflowParser.ParseSession(json);
            }

            if (queueId == 0)
            {
                var lobby = await client.TryGetStringAsync("/lol-lobby/v2/lobby", cancellationToken)
                    .ConfigureAwait(false);
                queueId = GameflowParser.ParseLobbyQueueId(lobby);
            }
        }
        catch (LcuHttpException ex)
        {
            _log.Debug("读取对局信息失败：" + ex.Message);
            return;
        }
        catch (JsonException ex)
        {
            _log.Debug("解析对局信息失败：" + ex.Message);
            return;
        }

        lock (_stateLock)
        {
            if (gameId > 0)
            {
                _sessionGameId = gameId;
            }

            if (queueId > 0)
            {
                _queueId = queueId;
                _modeName = _queues.GetName(queueId);
                _idleQueueId = queueId;
                _idleModeName = _modeName;
                _lobbyMisses = 0;
            }
        }
    }

    private DateTimeOffset ComputePostGameDeadlineLocked()
    {
        var seconds = _settings.Current.PostGameSeconds;
        if (seconds < 0)
        {
            return DateTimeOffset.MaxValue;
        }

        return seconds == 0 ? DateTimeOffset.Now : DateTimeOffset.Now.AddSeconds(seconds);
    }

    private bool IsPostGameVisible()
    {
        lock (_stateLock)
        {
            if (_postGameUntil is null)
            {
                return false;
            }

            return _postGameUntil.Value == DateTimeOffset.MaxValue ||
                   DateTimeOffset.Now < _postGameUntil.Value;
        }
    }

    private static bool IsInGamePhase(string phase) =>
        phase is "GameStart" or "InProgress" or "Reconnect" or "WaitingForStats";

    private void ResetSessionLocked()
    {
        _runtimes.Clear();
        _autoLaunched.Clear();
        _diagnosedSessions.Clear();
        _snapshot = null;
        _sessionKey = string.Empty;
        _sessionGameId = 0;
        _queueId = 0;
        _modeName = string.Empty;
        _banner = null;
        _liveStatus = string.Empty;
        _postGameUntil = null;
        _nextGameflowProbe = DateTimeOffset.MinValue;
        _nextLiveProbe = DateTimeOffset.MinValue;
    }

    private void SetBanner(string? message)
    {
        lock (_stateLock)
        {
            _banner = string.IsNullOrWhiteSpace(message) ? null : message;
        }
    }

    private void StartAutoFallback()
    {
        var settings = _settings.Current;
        var path = settings.FallbackPath;
        var arguments = settings.FallbackArguments;

        _ = Task.Run(() =>
        {
            if (_disposed)
            {
                return;
            }

            var result = _fallback.Launch(path, arguments);
            SetBanner(result.Message);
            EmitCurrent();
        });
    }

    private bool ShouldAutoLaunchFallbackLocked()
    {
        if (_mock)
        {
            return false;
        }

        var settings = _settings.Current;
        if (!settings.FallbackAutoLaunch || !FallbackLauncher.IsAvailable(settings.FallbackPath))
        {
            return false;
        }

        var hasQueryFailure = _runtimes.Values.Any(runtime =>
            runtime.IsVisible &&
            runtime.CanRetry &&
            runtime.Status is PlayerCardStatus.Unavailable or PlayerCardStatus.Error);
        if (!hasQueryFailure)
        {
            return false;
        }

        return _autoLaunched.Add(_sessionKey);
    }

    private void LogSessionDiagnostics(ChampSelectSnapshot snapshot)
    {
        lock (_stateLock)
        {
            if (!_diagnosedSessions.Add(_sessionKey))
            {
                return;
            }
        }

        _log.Info($"BP 诊断：game={snapshot.GameId} queue={_queueId} " +
                  $"localCell={snapshot.LocalPlayerCellId} mode={_modeName}");
        foreach (var player in snapshot.AllPlayers)
        {
            var puuid = player.Puuid.Length > 8 ? player.Puuid[..8] : "-";
            _log.Info($"  {(player.Team == TeamSide.MyTeam ? "我方" : "敌方")} " +
                      $"cell={player.CellId} slot={player.SlotIndex} visible={player.IsVisible} " +
                      $"champion={player.DisplayChampionId} puuid={puuid}");
        }
    }

    private void EmitCurrent(bool force = false)
    {
        if (_disposed)
        {
            return;
        }

        SessionUpdate update;
        string signature;
        lock (_stateLock)
        {
            update = BuildUpdateLocked();
            signature = BuildSignature(update);
            if (!force && signature == _lastSignature)
            {
                return;
            }

            if (signature != _lastSignature)
            {
                _lastUpdated = DateTimeOffset.Now;
                update = BuildUpdateLocked();
            }

            _lastSignature = signature;
            _last = update;
        }

        try
        {
            Updated?.Invoke(this, update);
        }
        catch (Exception ex)
        {
            _log.Error("界面更新事件处理失败", ex);
        }
    }

    private SessionUpdate BuildUpdateLocked()
    {
        var myTeam = BuildCardsLocked(TeamSide.MyTeam);
        var theirTeam = BuildCardsLocked(TeamSide.TheirTeam);
        var allCards = myTeam.Concat(theirTeam).ToList();
        var isChampSelect = _phase == "ChampSelect";
        var isInGame = IsInGamePhase(_phase);
        var overlayVisible = ComputeOverlayVisibleLocked(isChampSelect, isInGame);
        var hasHidden = allCards.Any(c => c.Status == PlayerCardStatus.Hidden);
        var hasIssues = allCards.Any(c => c.CanRetry);

        string statusText;
        string statusDetail;
        string trayText;
        if (_mock)
        {
            statusText = "模拟模式";
            statusDetail = "当前为模拟数据，用于预览界面。";
            trayText = "模拟模式";
        }
        else if (_connection is ConnectionStatus.Disconnected)
        {
            statusText = "等待客户端";
            statusDetail = _snapshot is not null
                ? "客户端连接中断，正在自动重连…"
                : "启动英雄联盟客户端后会自动连接。";
            trayText = "等待客户端";
        }
        else if (_connection is ConnectionStatus.Connecting)
        {
            statusText = "正在连接客户端";
            statusDetail = "正在解析客户端端口与凭据…";
            trayText = "连接中";
        }
        else if (isChampSelect)
        {
            statusText = "BP 进行中";
            var total = allCards.Count;
            var visible = allCards.Count(c => c.Status != PlayerCardStatus.Hidden);
            var mode = string.IsNullOrWhiteSpace(_modeName) ? "正在读取模式" : _modeName;
            statusDetail = total > 0
                ? $"{mode} · 公开身份 {visible}/{total}"
                : "正在读取 BP 会话…";
            trayText = "BP 中";
        }
        else if (isInGame)
        {
            statusText = "对局中";
            statusDetail = !string.IsNullOrWhiteSpace(_liveStatus)
                ? _liveStatus
                : IsPostGameVisibleLocked()
                    ? "正在尝试读取本地对局数据…"
                    : "窗口已按设置收起，对局结束后自动隐藏。";
            trayText = "对局中";
        }
        else
        {
            var mode = string.IsNullOrWhiteSpace(_modeName) ? null : _modeName;
            switch (_phase)
            {
                case "Lobby":
                    statusText = "已进入房间";
                    statusDetail = mode is null
                        ? "正在识别房间模式…"
                        : $"{mode} · 开始匹配后接受对局即可查看双方战绩。";
                    trayText = "房间中";
                    break;
                case "Matchmaking":
                    statusText = "正在匹配";
                    statusDetail = mode is null
                        ? "正在识别当前模式…"
                        : $"{mode} · 接受对局后自动显示双方战绩。";
                    trayText = "匹配中";
                    break;
                case "ReadyCheck":
                    statusText = "等待接受对局";
                    statusDetail = mode is null
                        ? "已找到对局，正在识别模式…"
                        : $"{mode} · 接受后进入 BP 并自动显示双方战绩。";
                    trayText = "待接受";
                    break;
                case "EndOfGame":
                case "PreEndOfGame":
                    statusText = "对局结束";
                    statusDetail = mode is null ? "等待返回大厅。" : $"{mode} · 等待返回大厅。";
                    trayText = "已结束";
                    break;
                default:
                    statusText = "已连接";
                    statusDetail = mode is null
                        ? "等待进入房间或 BP。"
                        : $"{mode} · 进入 BP 后自动显示双方战绩。";
                    trayText = "已连接";
                    break;
            }
        }

        string? banner = _banner;
        if (banner is null && _connection == ConnectionStatus.Connected)
        {
            var hasFallback = FallbackLauncher.IsAvailable(_settings.Current.FallbackPath);
            if (hasHidden && hasIssues)
            {
                banner = hasFallback
                    ? "部分玩家身份未公开或战绩查询失败：可重试；查询失败时会自动使用兜底工具。"
                    : "部分玩家身份未公开或战绩查询失败：可重试，或在设置中配置 LeagueAkari 兜底。";
            }
            else if (hasHidden)
            {
                banner = "对手身份未公开（客户端隐私保护），本工具不会尝试绕过或解密。";
            }
            else if (hasIssues)
            {
                banner = hasFallback
                    ? "部分玩家战绩查询未完成，已尝试启动兜底工具，也可点击重试。"
                    : "部分玩家战绩查询未完成，可点击重试或在设置中配置 LeagueAkari。";
            }
        }

        return new SessionUpdate(
            _mock ? ConnectionStatus.Connected : _connection,
            _phase,
            _queueId,
            _modeName,
            isChampSelect,
            isInGame,
            overlayVisible,
            _sessionKey,
            statusText,
            statusDetail,
            banner,
            hasHidden,
            hasIssues,
            _lastUpdated == DateTimeOffset.MinValue ? null : _lastUpdated,
            myTeam,
            theirTeam,
            _mock,
            trayText);
    }

    private bool ComputeOverlayVisibleLocked(bool isChampSelect, bool isInGame)
    {
        if (_connection != ConnectionStatus.Connected)
        {
            return false;
        }

        if (isChampSelect)
        {
            return _settings.Current.AutoShow;
        }

        if (isInGame)
        {
            return IsPostGameVisibleLocked();
        }

        return false;
    }

    private bool IsPostGameVisibleLocked() =>
        _postGameUntil is not null &&
        (_postGameUntil.Value == DateTimeOffset.MaxValue ||
         DateTimeOffset.Now < _postGameUntil.Value);

    private IReadOnlyList<PlayerCardState> BuildCardsLocked(TeamSide team)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return Array.Empty<PlayerCardState>();
        }

        var players = team == TeamSide.MyTeam ? snapshot.MyTeam : snapshot.TheirTeam;
        var cards = new List<PlayerCardState>(players.Count);
        foreach (var player in players.OrderBy(p => p.SlotIndex))
        {
            var key = BuildKey(player.Team, player.CellId, player.SlotIndex);
            if (_runtimes.TryGetValue(key, out var runtime))
            {
                cards.Add(BuildCardLocked(runtime));
            }
            else
            {
                cards.Add(BuildPlaceholderCardLocked(player));
            }
        }

        return cards;
    }

    private PlayerCardState BuildCardLocked(PlayerRuntime runtime)
    {
        var championId = runtime.ChampionId > 0 ? runtime.ChampionId : runtime.PickIntentChampionId;
        var status = runtime.IsVisible ? runtime.Status : PlayerCardStatus.Hidden;
        var message = runtime.IsVisible ? runtime.Message : "身份未公开";
        var displayName = runtime.IsVisible
            ? string.IsNullOrWhiteSpace(runtime.DisplayName) ? "玩家" : runtime.DisplayName
            : "身份未公开";
        var championName = ResolveChampionName(runtime, championId);
        var canUseFallback = !_mock &&
                             (status == PlayerCardStatus.Hidden ||
                              status is PlayerCardStatus.Unavailable or PlayerCardStatus.Error);

        return new PlayerCardState(
            runtime.Key,
            runtime.CellId,
            runtime.Team,
            runtime.SlotIndex,
            displayName,
            PositionNames.ToChinese(runtime.Position),
            runtime.ChampionId,
            runtime.PickIntentChampionId,
            championName,
            status,
            message,
            runtime.Profile,
            runtime.Stats,
            _settings.Current.ShowRank ? runtime.Rank : null,
            runtime.IsAutofilled,
            runtime.IsLocalPlayer,
            runtime.IsVisible && runtime.CanRetry,
            canUseFallback,
            runtime.UpdatedAt);
    }

    private PlayerCardState BuildPlaceholderCardLocked(ChampSelectPlayer player)
    {
        var championId = player.DisplayChampionId;
        return new PlayerCardState(
            BuildKey(player.Team, player.CellId, player.SlotIndex),
            player.CellId,
            player.Team,
            player.SlotIndex,
            player.IsVisible && !string.IsNullOrWhiteSpace(player.DisplayName)
                ? player.DisplayName
                : "身份未公开",
            PositionNames.ToChinese(player.AssignedPosition),
            player.ChampionId,
            player.PickIntentChampionId,
            championId > 0
                ? (_mock ? MockData.GetChampionName(championId) : _champions.GetName(championId))
                : string.Empty,
            player.IsVisible ? PlayerCardStatus.Loading : PlayerCardStatus.Hidden,
            player.IsVisible ? null : "身份未公开",
            null,
            null,
            null,
            player.IsAutofilled,
            player.IsLocalPlayer,
            false,
            !_mock,
            DateTimeOffset.MinValue);
    }

    private string ResolveChampionName(PlayerRuntime runtime, int championId)
    {
        if (championId <= 0)
        {
            return string.IsNullOrWhiteSpace(runtime.ChampionNameOverride)
                ? string.Empty
                : runtime.ChampionNameOverride;
        }

        if (_mock)
        {
            return MockData.GetChampionName(championId);
        }

        var name = _champions.GetName(championId);
        if (!string.IsNullOrWhiteSpace(runtime.ChampionNameOverride) &&
            (name.StartsWith('#') || string.IsNullOrEmpty(_champions.GetAlias(championId))))
        {
            return runtime.ChampionNameOverride;
        }

        return name;
    }

    private static string BuildSignature(SessionUpdate update)
    {
        var builder = new StringBuilder(1024);
        builder.Append((int)update.Connection).Append('|')
            .Append(update.Phase).Append('|')
            .Append(update.QueueId).Append('|')
            .Append(update.ModeName).Append('|')
            .Append(update.IsChampSelect).Append('|')
            .Append(update.IsInGame).Append('|')
            .Append(update.OverlayVisible).Append('|')
            .Append(update.SessionKey).Append('|')
            .Append(update.StatusText).Append('|')
            .Append(update.StatusDetail).Append('|')
            .Append(update.Banner).Append('|')
            .Append(update.HasHiddenPlayers).Append('|')
            .Append(update.HasQueryIssues).Append('|');
        AppendCardSignature(builder, update.MyTeam);
        AppendCardSignature(builder, update.TheirTeam);
        return builder.ToString();
    }

    private static void AppendCardSignature(StringBuilder builder, IReadOnlyList<PlayerCardState> cards)
    {
        foreach (var card in cards)
        {
            builder.Append(card.Key).Append(',')
                .Append(card.Status).Append(',')
                .Append(card.DisplayName).Append(',')
                .Append(card.PositionText).Append(',')
                .Append(card.ChampionId).Append(',')
                .Append(card.PickIntentChampionId).Append(',')
                .Append(card.ChampionName).Append(',')
                .Append(card.Message).Append(',')
                .Append(card.Profile?.ProfileIconId).Append(',')
                .Append(card.Profile?.SummonerLevel).Append(',')
                .Append(card.Rank?.DisplayText).Append(',')
                .Append(card.IsAutofilled).Append(',')
                .Append(card.IsLocalPlayer).Append(',')
                .Append(card.CanRetry).Append(',')
                .Append(card.CanUseFallback).Append(',');

            var stats = card.Stats;
            if (stats is not null)
            {
                builder.Append(stats.Games).Append(',')
                    .Append(stats.Wins).Append(',')
                    .Append(stats.Losses).Append(',')
                    .Append(stats.Kda.ToString("F2")).Append(',')
                    .Append(stats.CurrentChampion?.Games).Append(',')
                    .Append(stats.CurrentChampion?.Wins).Append(',')
                    .Append(stats.WinStreak).Append(',')
                    .Append(stats.LossStreak).Append(',')
                    .Append(stats.FilteredByQueue).Append(',')
                    .Append(stats.RemakesExcluded).Append(',')
                    .Append(stats.IsStale).Append(',');
                foreach (var champion in stats.RecentChampions)
                {
                    builder.Append(champion.ChampionId).Append(':')
                        .Append(champion.Games).Append(':')
                        .Append(champion.Wins).Append(';');
                }
            }

            builder.Append('/');
        }
    }
}
