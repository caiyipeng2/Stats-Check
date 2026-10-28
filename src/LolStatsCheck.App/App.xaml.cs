using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using LolStatsCheck.App.Services;
using LolStatsCheck.Core.Models;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.App;

public partial class App : Application
{
    private const string MutexName = @"Local\LolStatsCheck.SingleInstance";
    private const string ShowSignalName = @"Local\LolStatsCheck.ShowWindow";

    private LogService? _log;
    private SettingsService? _settings;
    private SessionCoordinator? _coordinator;
    private ImageCache? _images;
    private MainWindow? _main;
    private TrayService? _tray;
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private CancellationTokenSource? _signalCts;
    private bool _isExiting;
    private bool _mock;
    private bool _diagnose;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args ?? Array.Empty<string>();
        _mock = HasArg(args, "--mock");
        _diagnose = HasArg(args, "--diagnose");
        var startHidden = HasArg(args, "--tray");
        var openSettings = HasArg(args, "--settings");

        if (!AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        _log = new LogService { Verbose = _diagnose };
        _log.Info($"应用启动 · mock={_mock} · diagnose={_diagnose}");
        _settings = new SettingsService(null, _log);
        _log.Verbose = _diagnose || _settings.Current.ShowDiagnostics;

        if (!_settings.Current.DisclaimerAccepted)
        {
            var disclaimer = new DisclaimerWindow();
            if (disclaimer.ShowDialog() != true)
            {
                _log.Info("用户未同意使用须知，退出应用");
                Shutdown();
                return;
            }

            _settings.Current.DisclaimerAccepted = true;
            SaveSettingsQuiet();
        }

        _coordinator = new SessionCoordinator(_settings, _log, _mock, _diagnose);
        _images = new ImageCache(_coordinator.Champions, _mock);

        _main = new MainWindow(_coordinator, _settings, _log, _images);
        MainWindow = _main;
        _main.SettingsRequested += (_, _) => OpenSettings();
        _main.ExitRequested += (_, _) => ExitApplication();
        _main.UpdateApplied += (_, update) => _tray?.Update(update);
        _main.OverlayVisibilityChanged += visible => _tray?.SetOverlayVisible(visible);

        _tray = new TrayService(
            toggleOverlay: () => _main?.ToggleOverlay(),
            refresh: () => _coordinator?.RequestRefresh(),
            fallback: LaunchFallbackFromTray,
            settings: OpenSettings,
            openLogs: OpenLogs,
            exit: ExitApplication);
        _tray.Initialize(_mock, showBalloon: startHidden);

        if (startHidden)
        {
            _tray.SetOverlayVisible(false);
        }
        else
        {
            _main.Show();
            _tray.SetOverlayVisible(true);
        }

        _ = _coordinator.StartAsync();
        StartShowSignalListener();

        if (openSettings)
        {
            Dispatcher.BeginInvoke(OpenSettings);
        }
    }

    private bool AcquireSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out var created);
            if (created)
            {
                _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
                return true;
            }

            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                {
                    signal.Set();
                    signal.Dispose();
                }
            }
            catch (Exception)
            {
            }

            MessageBox.Show(
                "LOL BP 战绩速查已经在运行。\n可以在系统托盘中找到它，或按 Ctrl+Alt+L 呼出窗口。",
                "已在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            _log?.Warn("单实例检测失败，将继续启动：" + ex.Message);
            return true;
        }
    }

    private void StartShowSignalListener()
    {
        if (_showSignal is null)
        {
            return;
        }

        _signalCts = new CancellationTokenSource();
        var token = _signalCts.Token;
        var signal = _showSignal;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!signal.WaitOne(400))
                    {
                        continue;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!_isExiting)
                        {
                            _main?.ShowOverlay(manual: true);
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
        }, token);
    }

    private void OpenSettings()
    {
        var settings = _settings;
        var log = _log;
        var main = _main;
        if (_isExiting || settings is null || log is null || main is null)
        {
            return;
        }

        if (!main.IsVisible)
        {
            main.ShowOverlay(manual: true);
        }

        try
        {
            var dialog = new SettingsWindow(settings, log) { Owner = main };
            if (dialog.ShowDialog() == true)
            {
                log.Verbose = _diagnose || settings.Current.ShowDiagnostics;
                main.ApplySettings();
                _coordinator?.RequestRefresh();
                log.Info("设置已保存并应用");
            }
        }
        catch (Exception ex)
        {
            log.Error("打开设置窗口失败", ex);
            MessageBox.Show("打开设置失败：" + ex.Message, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LaunchFallbackFromTray()
    {
        if (_coordinator is null)
        {
            return;
        }

        if (_main is { IsVisible: false })
        {
            _main.ShowOverlay(manual: true);
        }

        var result = _coordinator.LaunchFallback();
        if (result.Status is FallbackLaunchStatus.NotConfigured or FallbackLaunchStatus.PathMissing)
        {
            OpenSettings();
        }
    }

    private void OpenLogs()
    {
        if (_settings is null || _log is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_settings.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("打开日志目录失败：" + ex.Message);
        }
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        try
        {
            _signalCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _tray?.Dispose();
        _tray = null;
        _main?.PrepareForExit();
        Shutdown();
    }

    private void SaveSettingsQuiet()
    {
        try
        {
            _settings?.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Warn("保存配置失败：" + ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _main?.SaveBounds();
        }
        catch (Exception)
        {
        }

        _images?.Dispose();
        _tray?.Dispose();

        if (_coordinator is not null)
        {
            try
            {
                var task = _coordinator.DisposeAsync().AsTask();
                if (!task.Wait(TimeSpan.FromSeconds(2)))
                {
                    _log?.Warn("等待查询服务退出超时");
                }
            }
            catch (Exception ex)
            {
                _log?.Warn("退出查询服务时出现异常：" + ex.Message);
            }
        }

        _signalCts?.Dispose();
        _showSignal?.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
            _mutex = null;
        }

        _log?.Info("应用退出");
        base.OnExit(e);
    }

    private static bool HasArg(string[] args, string expected) =>
        args.Any(arg => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase));
}
