using System.Text.Json;
using GhubLite.Core.Hidpp;
using GhubLite.Core.Models;

namespace GhubLite.Core.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConfigStore _store;

    public BackupService(ConfigStore store)
    {
        _store = store;
    }

    public BackupData Capture(HidppDevice device)
    {
        var backup = new BackupData
        {
            DeviceName = device.Name,
            ProductId = device.ProductId,
            CreatedAt = DateTimeOffset.Now,
            ControlBase64 = Convert.ToBase64String(device.ReadControlSector()),
        };

        for (int i = 1; i <= 5; i++)
        {
            backup.ProfilesBase64[i] = Convert.ToBase64String(device.ReadProfile(i).Raw);
        }

        return backup;
    }

    public string Save(BackupData backup)
    {
        string path = _store.CreateBackupPath();
        File.WriteAllText(path, JsonSerializer.Serialize(backup, JsonOptions));
        return path;
    }

    public BackupData Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BackupData>(json, JsonOptions)
               ?? throw new InvalidDataException("备份文件内容无效。");
    }

    public void Restore(HidppDevice device, BackupData backup)
    {
        if (!string.Equals(backup.DeviceName, device.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"备份属于 \"{backup.DeviceName}\"，当前设备是 \"{device.Name}\"。");
        }

        // Profiles first, then the control sector that enables/disables them.
        for (int i = 1; i <= 5; i++)
        {
            if (backup.ProfilesBase64.TryGetValue(i, out string? base64))
            {
                device.WriteSector(i, Convert.FromBase64String(base64));
            }
        }

        byte[] control = Convert.FromBase64String(backup.ControlBase64);
        device.WriteSector(0, control);

        var headers = device.ReadControlHeaders();
        if (headers.Count == 0)
        {
            throw new InvalidDataException("恢复后未读到板载 Profile 头。");
        }
    }
}
