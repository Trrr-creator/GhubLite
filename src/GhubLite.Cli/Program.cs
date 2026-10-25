using System.Text;
using GhubLite.Core.Hidpp;
using GhubLite.Core.Models;
using GhubLite.Core.Services;

Console.OutputEncoding = Encoding.UTF8;

var store = new ConfigStore();
var backupService = new BackupService(store);

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

if (args[0] == "crc")
{
    var sample = Enumerable.Range(0, 253).Select(i => (byte)i).ToArray();
    Console.WriteLine($"CRC sample = 0x{Crc16.Compute(sample):X4}");
    return 0;
}

using HidppDevice? device = Connect();
if (device is null)
{
    Console.WriteLine("未找到 GPW2 / PRO X Wireless。请确认鼠标已开机、接收器已插入，或 USB 线已连接。");
    return 2;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "info":
            PrintInfo(device);
            break;
        case "battery":
            var battery = device.ReadBattery();
            Console.WriteLine($"{battery.Percentage}% · {battery.StatusText} · {battery.LevelText}");
            break;
        case "dpi":
            if (args.Length == 1)
            {
                Console.WriteLine(device.ReadDpi());
            }
            else
            {
                ushort dpi = ushort.Parse(args[1]);
                device.SetDpi(dpi);
                Console.WriteLine($"DPI = {device.ReadDpi()}");
            }
            break;
        case "rate":
            if (args.Length == 1)
            {
                Console.WriteLine($"{1000 / device.ReadReportRateMs()} Hz ({device.ReadReportRateMs()} ms)");
            }
            else
            {
                int hz = int.Parse(args[1]);
                int ms = hz switch
                {
                    1000 => 1,
                    500 => 2,
                    250 => 4,
                    125 => 8,
                    _ => throw new ArgumentException("仅支持 1000 / 500 / 250 / 125 Hz。"),
                };
                device.SetReportRateMs((byte)ms);
                Console.WriteLine($"{1000 / device.ReadReportRateMs()} Hz");
            }
            break;
        case "profiles":
            PrintProfiles(device);
            break;
        case "profile":
            if (args.Length < 2)
            {
                throw new ArgumentException("用法: profile <1-5>");
            }
            PrintProfile(device, int.Parse(args[1]));
            break;
        case "profile-set":
            SetProfile(device, args, backupService);
            break;
        case "switch":
            if (args.Length < 2)
            {
                throw new ArgumentException("用法: switch <1-5>");
            }
            int target = int.Parse(args[1]);
            device.ApplyProfile(target);
            Console.WriteLine($"当前 Profile = {device.GetCurrentProfile()}, DPI = {device.ReadDpi()}, {1000 / device.ReadReportRateMs()} Hz");
            break;
        case "mode":
            if (args.Length == 1)
            {
                Console.WriteLine(device.ReadOnboardMode() == 2 ? "host" : "onboard");
            }
            else
            {
                int mode = args[1].Equals("host", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                device.SetOnboardMode(mode);
                Console.WriteLine(device.ReadOnboardMode() == 2 ? "host" : "onboard");
            }
            break;
        case "enable-profiles":
            EnableProfiles(device, backupService);
            break;
        case "backup":
            Console.WriteLine(SaveBackup(device, backupService));
            break;
        case "restore":
            if (args.Length < 2)
            {
                throw new ArgumentException("用法: restore <backup.json>");
            }
            Restore(device, backupService, args[1]);
            break;
        default:
            PrintHelp();
            return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("错误: " + ex.Message);
    return 3;
}
finally
{
    device.Dispose();
}

static HidppDevice? Connect()
{
    Console.WriteLine("连接鼠标...");
    return HidppDevice.Connect();
}

static void PrintInfo(HidppDevice device)
{
    var battery = device.ReadBattery();
    int rateMs = device.ReadReportRateMs();
    Console.WriteLine($"设备      : {device.Name}");
    Console.WriteLine($"连接      : {device.Info.ConnectionText} (PID 0x{device.ProductId:X4}, index 0x{device.DeviceIndex:X2})");
    Console.WriteLine($"协议      : {device.Info.ProtocolText}");
    Console.WriteLine($"端口      : {device.PortName}");
    Console.WriteLine($"电量      : {battery.Percentage}% · {battery.StatusText} · {battery.LevelText}");
    Console.WriteLine($"DPI       : {device.ReadDpi()}");
    Console.WriteLine($"回报率    : {1000 / rateMs} Hz ({rateMs} ms)");
    Console.WriteLine($"支持回报率: {string.Join(", ", device.ReadReportRateOptions().Select(ms => $"{1000 / ms} Hz"))}");
    Console.WriteLine($"板载模式  : {(device.ReadOnboardMode() == 2 ? "host（实时设置）" : "onboard（板载 Profile）")}");
    Console.WriteLine($"当前 Profile: {device.GetCurrentProfile()}");
    var d = device.Descriptor;
    Console.WriteLine($"板载配置  : {d.Count} 个, {d.Size} bytes/sector, {d.Buttons} 按键, memory={d.MemoryModel}");
}

static void PrintProfiles(HidppDevice device)
{
    var headers = device.ReadControlHeaders();
    for (int i = 1; i <= 5; i++)
    {
        var profile = device.ReadProfile(i);
        bool enabled = headers.Any(h => h.Sector == i && h.Enabled);
        Console.WriteLine(
            $"P{i} {(enabled ? "启用" : "禁用")}  {profile.DisplayName,-16}  " +
            $"rate={profile.ReportRateMs}ms  dpi=[{string.Join(", ", profile.DpiSlots)}]  " +
            $"default={profile.DefaultDpiIndex}  crc={(profile.CrcValid ? "OK" : "BAD")}");
    }
}

static void PrintProfile(HidppDevice device, int index)
{
    var profile = device.ReadProfile(index);
    Console.WriteLine($"Profile {index}: {profile.DisplayName}");
    Console.WriteLine($"  report rate : {profile.ReportRateMs} ms");
    Console.WriteLine($"  DPI slots   : {string.Join(", ", profile.DpiSlots)}");
    Console.WriteLine($"  default slot: {profile.DefaultDpiIndex}");
    Console.WriteLine($"  shift slot  : {profile.ShiftDpiIndex}");
    Console.WriteLine($"  CRC         : stored=0x{profile.StoredCrc:X4}, valid={profile.CrcValid}");
    Console.WriteLine($"  raw hex     : {Convert.ToHexString(profile.Raw)}");
}

static void SetProfile(HidppDevice device, string[] args, BackupService backups)
{
    if (args.Length < 6)
    {
        throw new ArgumentException("用法: profile-set <1-5> <dpi1,dpi2,dpi3,dpi4,dpi5> <defaultIndex> <rateMs> <name>");
    }

    int index = int.Parse(args[1]);
    var slots = args[2].Split(',').Select(int.Parse).ToArray();
    int defaultIndex = int.Parse(args[3]);
    int rateMs = int.Parse(args[4]);
    string name = args[5];

    string backupPath = SaveBackup(device, backups);
    Console.WriteLine("已备份: " + backupPath);

    var profile = device.ReadProfile(index);
    profile.Name = name;
    profile.ReportRateMs = (byte)rateMs;
    profile.SetDpiSlots(slots, defaultIndex);
    device.WriteProfile(profile);
    Console.WriteLine($"Profile {index} 已写入并回读校验。");
    PrintProfile(device, index);
}

static void EnableProfiles(HidppDevice device, BackupService backups)
{
    string backupPath = SaveBackup(device, backups);
    Console.WriteLine("已备份: " + backupPath);
    device.EnableProfiles(5);
    Console.WriteLine("已启用 5 个板载 Profile。");
    PrintProfiles(device);
}

static string SaveBackup(HidppDevice device, BackupService backups)
{
    var backup = backups.Capture(device);
    return backups.Save(backup);
}

static void Restore(HidppDevice device, BackupService backups, string path)
{
    string currentBackup = SaveBackup(device, backups);
    Console.WriteLine("恢复前已备份当前状态: " + currentBackup);
    var backup = backups.Load(path);
    backups.Restore(device, backup);
    Console.WriteLine("已恢复: " + backup.CreatedAt.LocalDateTime);
    PrintProfiles(device);
}

static void PrintHelp()
{
    Console.WriteLine("""
GhubLite CLI — GPW2 (PRO X Wireless / G PRO X Superlight)

  info                         设备、电量、DPI、回报率、Profile
  battery                      电量
  dpi [value]                  读/写实时 DPI
  rate [1000|500|250|125]      读/写实时回报率
  profiles                     列出 5 个板载 Profile
  profile <1-5>                查看 Profile 原始数据
  profile-set <n> <dpi1..dpi5> <default> <rateMs> <name>
                               写入板载 Profile（自动备份）
  switch <1-5>                 切换当前 Profile
  mode [host|onboard]          查看/切换板载模式
  enable-profiles              启用全部 5 个板载 Profile（自动备份）
  backup                       备份到 %APPDATA%\GhubLite\backups
  restore <backup.json>        从备份恢复（恢复前自动再备份）
""");
}
