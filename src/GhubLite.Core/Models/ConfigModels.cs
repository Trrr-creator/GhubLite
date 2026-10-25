namespace GhubLite.Core.Models;

public sealed class AppRule
{
    public string ProcessName { get; set; } = "";
    public int Profile { get; set; } = 1;
    public string DisplayName { get; set; } = "";

    public AppRule Clone() => new()
    {
        ProcessName = ProcessName,
        Profile = Profile,
        DisplayName = DisplayName,
    };
}

public sealed class AppConfig
{
    public bool AutoSwitch { get; set; } = true;
    public int DefaultProfile { get; set; } = 1;
    public List<AppRule> Rules { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
}

public sealed class BackupData
{
    public string DeviceName { get; set; } = "";
    public int ProductId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string ControlBase64 { get; set; } = "";
    public Dictionary<int, string> ProfilesBase64 { get; set; } = new();
}
