namespace GhubLite.Core.Models;

public sealed record BatteryInfo(int Percentage, int Level, int Status, bool IsCharging)
{
    public string StatusText => Status switch
    {
        0 => "放电中",
        1 => "充电中",
        2 => "即将充满",
        3 => "已充满",
        4 => "慢速充电",
        5 => "电池异常",
        6 => "温度异常",
        7 => "充电异常",
        _ => "未知",
    };

    public string LevelText => Level switch
    {
        1 => "电量极低",
        2 => "电量偏低",
        4 => "电量良好",
        8 => "电量充足",
        _ => "",
    };
}

public sealed record ProfileDescriptor(
    byte MemoryModel,
    byte ProfileFormat,
    byte MacroFormat,
    int Count,
    int OobCount,
    int Buttons,
    int Sectors,
    int Size,
    byte Shift);

public sealed record DeviceInfo(
    string Name,
    int ProtocolMajor,
    int ProtocolMinor,
    bool IsWired,
    int ProductId,
    string PortName)
{
    public string ProtocolText => $"HID++ {ProtocolMajor}.{ProtocolMinor}";
    public string ConnectionText => IsWired ? "USB 有线" : "LIGHTSPEED 2.4G";
}
