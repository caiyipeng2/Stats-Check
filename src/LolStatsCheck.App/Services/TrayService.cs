using System.Drawing;
using System.Windows.Forms;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Action _toggleOverlay;
    private readonly Action _refresh;
    private readonly Action _fallback;
    private readonly Action _settings;
    private readonly Action _openLogs;
    private readonly Action _exit;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _toggleItem;
    private bool _overlayVisible;
    private string _lastText = string.Empty;
    private bool _disposed;

    public TrayService(
        Action toggleOverlay,
        Action refresh,
        Action fallback,
        Action settings,
        Action openLogs,
        Action exit)
    {
        _toggleOverlay = toggleOverlay;
        _refresh = refresh;
        _fallback = fallback;
        _settings = settings;
        _openLogs = openLogs;
        _exit = exit;
    }

    public void Initialize(bool mock, bool showBalloon)
    {
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = Color.FromArgb(15, 22, 32),
            ForeColor = Color.FromArgb(230, 236, 245),
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            Padding = new Padding(4, 6, 4, 6)
        };

        _toggleItem = CreateItem("显示窗口", _toggleOverlay);
        var refreshItem = CreateItem("刷新数据", _refresh);
        var fallbackItem = CreateItem("启动兜底工具", _fallback);
        var settingsItem = CreateItem("设置…", _settings);
        var logsItem = CreateItem("打开日志目录", _openLogs);
        var exitItem = CreateItem("退出", _exit);

        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(refreshItem);
        _menu.Items.Add(fallbackItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(logsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) =>
        {
            if (_toggleItem is not null)
            {
                _toggleItem.Text = _overlayVisible ? "隐藏窗口" : "显示窗口";
            }
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "LOL BP 战绩速查",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseDoubleClick += (_, _) => _toggleOverlay();

        if (showBalloon)
        {
            _notifyIcon.ShowBalloonTip(
                4500,
                "LOL BP 战绩速查",
                mock
                    ? "模拟模式已启动，可在托盘中随时显示或隐藏窗口。"
                    : "已启动。进入 BP 后自动显示，Ctrl+Alt+L 可随时呼出。",
                ToolTipIcon.Info);
        }
    }

    public void Update(SessionUpdate update)
    {
        if (_notifyIcon is null || _disposed)
        {
            return;
        }

        var state = string.IsNullOrWhiteSpace(update.TrayText) ? "待机" : update.TrayText;
        var text = $"LOL BP 战绩速查 · {state}";
        if (text.Length > 63)
        {
            text = text[..63];
        }

        if (text == _lastText)
        {
            return;
        }

        _lastText = text;
        try
        {
            _notifyIcon.Text = text;
        }
        catch (ArgumentException)
        {
        }
    }

    public void SetOverlayVisible(bool visible) => _overlayVisible = visible;

    public void ShowMessage(string title, string text)
    {
        _notifyIcon?.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
    }

    private static ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(230, 236, 245),
            Padding = new Padding(4, 3, 4, 3)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception)
        {
        }

        return SystemIcons.Application;
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(15, 22, 32);
        private static readonly Color Border = Color.FromArgb(43, 55, 72);
        private static readonly Color Selected = Color.FromArgb(28, 38, 53);
        private static readonly Color Separator = Color.FromArgb(31, 42, 58);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Selected;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemSelectedGradientBegin => Selected;
        public override Color MenuItemSelectedGradientEnd => Selected;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;
    }
}
