using System.Text;

namespace LolStatsCheck.Core.Services;

public sealed class LogService
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 3;
    private readonly object _gate = new();
    private readonly string _logFile;

    public LogService(string? logDirectory = null)
    {
        LogDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LolStatsCheck", "logs");

        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
        }

        _logFile = Path.Combine(LogDirectory, "app.log");
    }

    public string LogDirectory { get; }
    public bool Verbose { get; set; }

    public void Debug(string message)
    {
        if (Verbose)
        {
            Write("DEBUG", message);
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text);
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(_logFile, line, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_logFile);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        for (var index = MaxFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? _logFile : Path.Combine(LogDirectory, $"app.{index - 1}.log");
            var target = Path.Combine(LogDirectory, $"app.{index}.log");
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }
    }
}
