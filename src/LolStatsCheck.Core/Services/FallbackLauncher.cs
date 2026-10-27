using System.Diagnostics;
using System.IO;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.Core.Services;

public sealed class FallbackLauncher
{
    private const string LeagueAkariExe = "LeagueAkari.exe";

    private static readonly string[] ExecutableNames =
    {
        LeagueAkariExe,
        "league-akari.exe",
        "LeagueAkari-win-x64.exe"
    };

    private static readonly string[] BundledRelativeDirectories =
    {
        Path.Combine("fallback", "LeagueAkari"),
        "fallback"
    };

    private readonly LogService _log;

    public FallbackLauncher(LogService log)
    {
        _log = log;
    }

    public FallbackLaunchResult Launch(string? configuredPath, string? arguments)
    {
        var configured = Normalize(configuredPath);
        var path = ResolvePath(configuredPath);
        if (path is null)
        {
            return configured is null
                ? new FallbackLaunchResult(FallbackLaunchStatus.NotConfigured,
                    "未检测到 LeagueAkari：可在设置中选择已安装的 LeagueAkari.exe，或点击“获取 LeagueAkari”。", null)
                : new FallbackLaunchResult(FallbackLaunchStatus.PathMissing,
                    "兜底工具路径不存在，请在设置中重新选择。", null);
        }

        try
        {
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

    public static bool IsAvailable(string? configuredPath) => ResolvePath(configuredPath) is not null;

    public static string? ResolvePath(string? configuredPath)
    {
        var normalized = Normalize(configuredPath);
        if (normalized is not null && File.Exists(normalized))
        {
            return normalized;
        }

        return FindBundled() ?? FindInstalled();
    }

    public static string? FindBundled()
    {
        foreach (var directory in BundledRelativeDirectories)
        {
            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(AppContext.BaseDirectory, directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static string? FindInstalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var directories = new[]
        {
            Path.Combine(local, "Programs", "LeagueAkari"),
            Path.Combine(local, "LeagueAkari"),
            Path.Combine(local, "Programs", "league-akari"),
            Path.Combine(programs, "LeagueAkari"),
            Path.Combine(programsX86, "LeagueAkari")
        };

        foreach (var directory in directories)
        {
            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return path.Trim().Trim('"');
        }
    }
}
