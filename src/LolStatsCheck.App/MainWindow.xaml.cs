using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LolStatsCheck.App.Services;
using LolStatsCheck.App.ViewModels;
using LolStatsCheck.Core.Models;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xB001;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkL = 0x4C;

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly OverlayViewModel _viewModel;

    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;
    private bool _allowClose;
    private bool _userHidden;
    private bool _autoShown;
    private bool _hasEnteredSession;
    private string _lastSessionKey = string.Empty;

    public MainWindow(
        SessionCoordinator coordinator,
        SettingsService settings,
        LogService log,
        ImageCache images)
    {
        _settings = settings;
        _log = log;

        InitializeComponent();

        _viewModel = new OverlayViewModel(coordinator, log, images);
        DataContext = _viewModel;
        _viewModel.Applied += OnUpdateApplied;
        _viewModel.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _viewModel.HideRequested += (_, _) => HideByUser();

        IsVisibleChanged += (_, _) => OverlayVisibilityChanged?.Invoke(IsVisible);

        RestoreWindowBounds();
        ApplySettings();
        _ = new WindowInteropHelper(this).EnsureHandle();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler<SessionUpdate>? UpdateApplied;
    public event EventHandler? ExitRequested;
    public event Action<bool>? OverlayVisibilityChanged;

    public void ApplySettings()
    {
        Opacity = _settings.Current.Opacity;
        Topmost = _settings.Current.AlwaysOnTop;
        PinToggle.IsChecked = Topmost;
    }

    public void ShowOverlay(bool manual)
    {
        if (manual)
        {
            _userHidden = false;
            _autoShown = false;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
    }

    public void ToggleOverlay()
    {
        if (IsVisible)
        {
            HideByUser();
        }
        else
        {
            ShowOverlay(manual: true);
        }
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        SaveBounds();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        if (RegisterHotKey(handle, HotkeyId, ModControl | ModAlt, VkL))
        {
            _hotkeyRegistered = true;
            _log.Info("全局快捷键 Ctrl+Alt+L 已注册");
        }
        else
        {
            _log.Warn("全局快捷键 Ctrl+Alt+L 注册失败，可能被其他程序占用");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideByUser();
            return;
        }

        SaveBounds();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyId);
            }

            _hotkeyRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnUpdateApplied(object? sender, SessionUpdate update)
    {
        UpdateApplied?.Invoke(this, update);

        if (!string.Equals(update.SessionKey, _lastSessionKey, StringComparison.Ordinal))
        {
            _lastSessionKey = update.SessionKey;
            _userHidden = false;
            _hasEnteredSession = false;
        }

        if (update.HasSession && !_hasEnteredSession)
        {
            _hasEnteredSession = true;
            if (IsVisible && _settings.Current.AutoShow && !_userHidden)
            {
                _autoShown = true;
            }
        }

        if (update.OverlayVisible)
        {
            if (!_userHidden && !IsVisible)
            {
                ShowOverlay(manual: false);
                _autoShown = true;
            }
            else if (!_userHidden && IsVisible && _settings.Current.AutoShow)
            {
                _autoShown = true;
            }
        }
        else if (IsVisible && _autoShown && _hasEnteredSession)
        {
            HideWindow();
            _autoShown = false;
        }
    }

    private void HideByUser()
    {
        _userHidden = true;
        _autoShown = false;
        HideWindow();
    }

    private void HideWindow()
    {
        SaveBounds();
        Hide();
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => HideByUser();

    private void Close_Click(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
        Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinToggle.IsChecked == true;
        _settings.Current.AlwaysOnTop = Topmost;
        SaveSettingsQuiet();
    }

    private void RestoreWindowBounds()
    {
        var settings = _settings.Current;
        var width = settings.WindowWidth ?? Width;
        var height = settings.WindowHeight ?? Height;
        width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth - 20));
        height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight - 20));
        Width = width;
        Height = height;

        if (settings.WindowLeft is not null && settings.WindowTop is not null)
        {
            Left = settings.WindowLeft.Value;
            Top = settings.WindowTop.Value;
        }

        if (!IsPositionVisible())
        {
            CenterOnPrimary();
        }
    }

    private bool IsPositionVisible()
    {
        const double margin = 80;
        var left = Left;
        var top = Top;
        var right = left + Width;
        var bottom = top + Height;
        return right > SystemParameters.VirtualScreenLeft + margin &&
               left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - margin &&
               bottom > SystemParameters.VirtualScreenTop + margin &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - margin;
    }

    private void CenterOnPrimary()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    public void SaveBounds()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var settings = _settings.Current;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
        SaveSettingsQuiet();
    }

    private void SaveSettingsQuiet()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("保存窗口配置失败：" + ex.Message);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
