using System.Text.Json;

namespace LolStatsCheck.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LogService? _log;
    private readonly object _gate = new();

    public SettingsService(string? baseDirectory = null, LogService? log = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LolStatsCheck");
        ConfigPath = Path.Combine(BaseDirectory, "config.json");
        _log = log;
        Current = Load();
    }

    public string BaseDirectory { get; }
    public string ConfigPath { get; }
    public string LogDirectory => Path.Combine(BaseDirectory, "logs");
    public AppSettings Current { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(ConfigPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log?.Warn("读取配置失败，已使用默认配置：" + ex.Message);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        lock (_gate)
        {
            Directory.CreateDirectory(BaseDirectory);
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            var tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ConfigPath, overwrite: true);
            Current = settings;
        }
    }

    public void Save()
    {
        Save(Current);
    }
}
