using System.Text.Json;
using GhubLite.Core.Models;

namespace GhubLite.Core.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string RootDirectory { get; }
    public string ConfigPath => Path.Combine(RootDirectory, "config.json");
    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    public ConfigStore()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GhubLite");
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, ConfigPath, overwrite: true);
    }

    public string CreateBackupPath()
    {
        string name = $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        return Path.Combine(BackupsDirectory, name);
    }

    public IReadOnlyList<string> ListBackups()
    {
        return Directory.Exists(BackupsDirectory)
            ? Directory.GetFiles(BackupsDirectory, "backup-*.json").OrderByDescending(f => f).ToList()
            : Array.Empty<string>();
    }
}
