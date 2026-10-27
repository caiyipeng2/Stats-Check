using System.Diagnostics;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed class FallbackLauncher
{
    private readonly LogService _log;

    public FallbackLauncher(LogService log)
    {
        _log = log;
    }

    public FallbackLaunchResult Launch(string? configuredPath, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new FallbackLaunchResult(FallbackLaunchStatus.NotConfigured,
                "尚未配置兜底工具路径，请先在设置中选择 exe。", null);
        }

        var path = configuredPath.Trim().Trim('"');
        try
        {
            path = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(path))
            {
                return new FallbackLaunchResult(FallbackLaunchStatus.PathMissing,
                    "兜底工具路径不存在，请在设置中重新选择。", null);
            }

            var processName = Path.GetFileNameWithoutExtension(path);
            var running = Process.GetProcessesByName(processName);
            if (running.Length > 0)
            {
                foreach (var process in running)
                {
                    process.Dispose();
                }

                return new FallbackLaunchResult(FallbackLaunchStatus.AlreadyRunning,
                    "兜底工具已在运行。", null);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            };

            var started = Process.Start(startInfo);
            _log.Info($"已启动兜底工具：{processName}");
            return new FallbackLaunchResult(FallbackLaunchStatus.Started,
                "已启动兜底工具。", started?.Id);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _log.Warn("启动兜底工具失败：" + ex.Message);
            return new FallbackLaunchResult(FallbackLaunchStatus.Failed,
                "启动兜底工具失败：" + ex.Message, null);
        }
    }
}
