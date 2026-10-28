using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using LolStatsCheck.Core.Services;
using Microsoft.Win32;

namespace LolStatsCheck.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly AppSettings _draft;

    public SettingsWindow(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
        InitializeComponent();

        _draft = settings.Current.Clone();
        ClientPathBox.Text = _draft.ClientPath ?? string.Empty;
        FallbackPathBox.Text = _draft.FallbackPath ?? string.Empty;
        FallbackArgsBox.Text = _draft.FallbackArguments ?? string.Empty;
        FallbackAutoCheck.IsChecked = _draft.FallbackAutoLaunch;
        UpdateFallbackHint();
        CurrentModeFirstCheck.IsChecked = _draft.CurrentModeFirst;
        ShowRankCheck.IsChecked = _draft.ShowRank;
        AlwaysOnTopCheck.IsChecked = _draft.AlwaysOnTop;
        AutoShowCheck.IsChecked = _draft.AutoShow;
        ShowDiagnosticsCheck.IsChecked = _draft.ShowDiagnostics;
        OpacitySlider.Value = _draft.Opacity;
        OpacityValueText.Text = $"{_draft.Opacity * 100:0}%";
        LogPathText.Text = settings.LogDirectory;
        LogPathText.ToolTip = settings.ConfigPath;
        VersionText.Text = $"版本 {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} · 配置文件保存在 {settings.ConfigPath}";

        Recent10.IsChecked = _draft.RecentGames == 10;
        Recent20.IsChecked = _draft.RecentGames == 20;
        Recent30.IsChecked = _draft.RecentGames == 30;

        Pg0.IsChecked = _draft.PostGameSeconds == 0;
        Pg15.IsChecked = _draft.PostGameSeconds == 15;
        Pg30.IsChecked = _draft.PostGameSeconds == 30;
        Pg60.IsChecked = _draft.PostGameSeconds == 60;
        PgAlways.IsChecked = _draft.PostGameSeconds == -1;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = $"{e.NewValue * 100:0}%";
        }
    }

    private void BrowseClient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择英雄联盟客户端程序",
            Filter = "客户端程序 (LeagueClient*.exe)|LeagueClient*.exe|可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        var current = ClientPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(current))
        {
            var directory = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) == true)
        {
            ClientPathBox.Text = dialog.FileName;
        }
    }

    private void BrowseFallback_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择兜底工具程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        var current = FallbackPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(current))
        {
            var directory = Path.GetDirectoryName(current);
            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) == true)
        {
            FallbackPathBox.Text = dialog.FileName;
        }
    }

    private void UpdateFallbackHint()
    {
        var resolved = FallbackLauncher.ResolvePath(_draft.FallbackPath);
        if (resolved is null)
        {
            FallbackHint.Text = "未检测到 LeagueAkari：发布包应包含 fallback\\LeagueAkari，缺失时可点击“官方发布页”手动下载，或选择本机已安装的 exe。";
            return;
        }

        var source = string.IsNullOrWhiteSpace(_draft.FallbackPath) ? "已自动检测到" : "当前使用";
        FallbackHint.Text = $"{source}：{resolved}";
    }

    private void GetLeagueAkari_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/LeagueAkari/LeagueAkari/releases/latest",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError("无法打开浏览器：" + ex.Message);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)    {
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
            ShowError("无法打开日志目录：" + ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var clientPath = ClientPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(clientPath) &&
            !File.Exists(clientPath) && !Directory.Exists(clientPath))
        {
            ShowError("客户端路径不存在，请重新选择，或留空使用自动检测。");
            return;
        }

        var fallbackPath = FallbackPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(fallbackPath) && !File.Exists(fallbackPath))
        {
            ShowError("兜底工具路径不存在，请重新选择，或留空表示不配置。");
            return;
        }

        _draft.ClientPath = string.IsNullOrWhiteSpace(clientPath) ? null : clientPath;
        _draft.FallbackPath = string.IsNullOrWhiteSpace(fallbackPath) ? null : fallbackPath;
        _draft.FallbackArguments = FallbackArgsBox.Text.Trim();
        _draft.FallbackAutoLaunch = FallbackAutoCheck.IsChecked == true;
        _draft.CurrentModeFirst = CurrentModeFirstCheck.IsChecked == true;
        _draft.ShowRank = ShowRankCheck.IsChecked == true;
        _draft.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _draft.AutoShow = AutoShowCheck.IsChecked == true;
        _draft.ShowDiagnostics = ShowDiagnosticsCheck.IsChecked == true;
        _draft.Opacity = OpacitySlider.Value;
        _draft.RecentGames = Recent10.IsChecked == true ? 10 : Recent30.IsChecked == true ? 30 : 20;
        _draft.PostGameSeconds =
            PgAlways.IsChecked == true ? -1 :
            Pg0.IsChecked == true ? 0 :
            Pg15.IsChecked == true ? 15 :
            Pg60.IsChecked == true ? 60 : 30;

        try
        {
            _settings.Save(_draft);
            _log.Info("设置已保存");
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("保存配置失败：" + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
