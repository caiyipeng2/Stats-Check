using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LolStatsCheck.Core.Services;

namespace LolStatsCheck.Core.Lcu;

public sealed partial class LcuLocator
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    private readonly LogService _log;

    public LcuLocator(LogService log)
    {
        _log = log;
    }

    public LcuLocation? Locate(string? savedPath)
    {
        var processes = GetRunningClientProcesses();

        foreach (var process in processes)
        {
            var credentials = TryReadCommandLine(process.ProcessId);
            if (credentials is null)
            {
                continue;
            }

            var exePath = process.ExePath ?? string.Empty;
            var source = string.IsNullOrWhiteSpace(exePath)
                ? process.ProcessId.ToString()
                : exePath;
            _log.Info($"已通过进程命令行发现客户端：{source}（端口 {credentials.Port}）");
            return new LcuLocation(exePath, credentials, "process");
        }

        var directories = new List<string>();
        foreach (var process in processes)
        {
            if (!string.IsNullOrWhiteSpace(process.ExePath))
            {
                AddDirectoryVariants(directories, process.ExePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            AddDirectoryVariants(directories, savedPath);
        }

        foreach (var path in GetCommonInstallRoots())
        {
            AddDirectoryVariants(directories, path);
        }

        foreach (var dir in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var location = TryReadLockfile(dir);
            if (location is not null)
            {
                _log.Info($"已通过 lockfile 发现客户端：{location.ClientExePath}");
                return location;
            }
        }

        return null;
    }

    public static LcuCredentials? ParseLockfile(string content)
    {
        var parts = content.Trim().Split(':');
        if (parts.Length < 5)
        {
            return null;
        }

        if (!int.TryParse(parts[2], out var port) || port <= 0 || port > 65535)
        {
            return null;
        }

        var password = parts[3];
        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new LcuCredentials(port, password, parts[4]);
    }

    private LcuLocation? TryReadLockfile(string directory)
    {
        var lockfilePath = Path.Combine(directory, "lockfile");
        try
        {
            if (!File.Exists(lockfilePath))
            {
                return null;
            }

            using var stream = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var credentials = ParseLockfile(content);
            if (credentials is null)
            {
                return null;
            }

            if (!IsLockfileOwnerAlive(content))
            {
                _log.Debug($"忽略过期 lockfile：{lockfilePath}");
                return null;
            }

            var exePath = FindClientExe(directory) ?? string.Empty;
            return new LcuLocation(exePath, credentials, "lockfile");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsLockfileOwnerAlive(string content)
    {
        var parts = content.Trim().Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var pid) || pid <= 0)
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    private LcuCredentials? TryReadCommandLine(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var commandLine = ProcessCommandLineReader.TryRead(process);
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return null;
            }

            var portMatch = AppPortRegex().Match(commandLine);
            var tokenMatch = AuthTokenRegex().Match(commandLine);
            if (!portMatch.Success || !tokenMatch.Success)
            {
                return null;
            }

            if (!int.TryParse(portMatch.Groups[1].Value, out var port) || port <= 0 || port > 65535)
            {
                return null;
            }

            var token = tokenMatch.Groups[1].Success
                ? tokenMatch.Groups[1].Value
                : tokenMatch.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            return new LcuCredentials(port, token);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            _log.Debug("读取进程命令行失败：" + ex.Message);
            return null;
        }
    }

    private static string? FindClientExe(string directory)
    {
        var direct = Path.Combine(directory, "LeagueClientUx.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        var nested = Path.Combine(directory, "LeagueClient", "LeagueClientUx.exe");
        return File.Exists(nested) ? nested : null;
    }

    private sealed record ClientProcess(int ProcessId, string? ExePath);

    private static List<ClientProcess> GetRunningClientProcesses()
    {
        var result = new List<ClientProcess>();
        foreach (var name in new[] { "LeagueClientUx", "LeagueClient" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        result.Add(new ClientProcess(process.Id, TryGetProcessPath(process)));
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return result;
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    private static IEnumerable<string> GetCommonInstallRoots()
    {
        var roots = new[]
        {
            @"D:\WeGameApps\英雄联盟",
            @"C:\WeGameApps\英雄联盟",
            @"E:\WeGameApps\英雄联盟",
            @"F:\WeGameApps\英雄联盟",
            @"D:\WeGameApps\League of Legends",
            @"C:\Program Files\WeGameApps\英雄联盟",
            @"C:\Program Files (x86)\WeGameApps\英雄联盟",
            @"C:\Program Files\Tencent\League of Legends",
            @"C:\Program Files (x86)\Tencent\League of Legends",
            @"C:\Riot Games\League of Legends",
            @"D:\Riot Games\League of Legends"
        };

        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                yield return root;
            }
        }
    }

    private static void AddDirectoryVariants(List<string> directories, string path)
    {
        var trimmed = path.Trim().Trim('"');
        var isFile = trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.EndsWith("lockfile", StringComparison.OrdinalIgnoreCase);

        var directory = isFile
            ? Path.GetDirectoryName(trimmed)
            : trimmed;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        directories.Add(directory);
        directories.Add(Path.Combine(directory, "LeagueClient"));

        var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            directories.Add(parent);
        }
    }

    [GeneratedRegex(@"--app-port[= ]\s*""?(\d+)")]
    private static partial Regex AppPortRegex();

    [GeneratedRegex(@"--remoting-auth-token[= ]\s*(?:""([^""]+)""|([^\s""]+))")]
    private static partial Regex AuthTokenRegex();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process, int flags, StringBuilder exeName, ref int size);
}
