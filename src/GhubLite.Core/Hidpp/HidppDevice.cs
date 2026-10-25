using GhubLite.Core.Hid;
using GhubLite.Core.Models;
using HidSharp;

namespace GhubLite.Core.Hidpp;

/// <summary>
/// High-level access to a Logitech mouse speaking HID++ 2.0.
/// Supports both the C547 LIGHTSPEED receiver (device index 1) and wired C094+ (device index 0xFF).
/// </summary>
public sealed class HidppDevice : IDisposable
{
    private readonly HidppTransport _transport;
    private readonly HidppConnection _connection;
    private ProfileDescriptor? _descriptor;
    private bool _disposed;

    public string Name { get; }
    public int ProductId { get; }
    public bool IsWired { get; }
    public byte DeviceIndex => _connection.DeviceIndex;
    public (int Major, int Minor) Protocol => _connection.Protocol ?? (0, 0);
    public IReadOnlyDictionary<ushort, byte> Features => _connection.Features;
    public string PortName => _transport.Device.DevicePath;

    public DeviceInfo Info => new(Name, Protocol.Major, Protocol.Minor, IsWired, ProductId, PortName);

    public HidppDevice(
        HidppTransport transport,
        HidppConnection connection,
        string name,
        int productId,
        bool isWired)
    {
        _transport = transport;
        _connection = connection;
        Name = name;
        ProductId = productId;
        IsWired = isWired;
    }

    public static HidppDevice? Connect()
    {
        foreach (var hid in HidDeviceLocator.FindLongReportDevices())
        {
            HidppTransport? transport = null;
            try
            {
                transport = new HidppTransport(hid);
            }
            catch
            {
                continue;
            }

            bool isWired = hid.ProductID != 0xC547;
            byte[] candidates = isWired
                ? new byte[] { 0xFF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }
                : new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xFF };

            foreach (byte deviceIndex in candidates)
            {
                var connection = new HidppConnection(transport, deviceIndex);
                (int Major, int Minor)? protocol;
                try
                {
                    protocol = connection.Ping();
                }
                catch
                {
                    continue;
                }

                if (protocol is null)
                {
                    continue;
                }

                string? name;
                try
                {
                    name = connection.GetDeviceName();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name) ||
                    !name.Contains("PRO X", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    connection.EnumerateFeatures();
                }
                catch
                {
                    // Keep going; the feature cache may still be partially populated.
                }

                if (!connection.Features.ContainsKey(HidppConstants.Features.AdjustableDpi) ||
                    !connection.Features.ContainsKey(HidppConstants.Features.OnboardProfiles))
                {
                    continue;
                }

                return new HidppDevice(transport, connection, name, hid.ProductID, isWired);
            }

            transport.Dispose();
        }

        return null;
    }

    public bool HasFeature(ushort featureId) => _connection.Features.ContainsKey(featureId);

    public ProfileDescriptor Descriptor
    {
        get
        {
            if (_descriptor is not null)
            {
                return _descriptor;
            }

            var index = RequireFeature(HidppConstants.Features.OnboardProfiles);
            var response = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.GetDescription, Array.Empty<byte>());
            if (response is null || response.Length < 14)
            {
                throw new HidppTimeoutException("读取板载配置描述超时。");
            }

            int size = (response[11] << 8) | response[12];
            if (size < 16 || size > 4096)
            {
                throw new HidppException($"板载配置尺寸异常: {size}");
            }

            _descriptor = new ProfileDescriptor(
                MemoryModel: response[4],
                ProfileFormat: response[5],
                MacroFormat: response[6],
                Count: response[7],
                OobCount: response[8],
                Buttons: response[9],
                Sectors: response[10],
                Size: size,
                Shift: response[13]);
            return _descriptor;
        }
    }

    public BatteryInfo ReadBattery()
    {
        var unified = _connection.GetFeatureIndex(HidppConstants.Features.BatteryUnified);
        if (unified is not null)
        {
            try
            {
                var caps = _connection.RequestRaw(unified.Value, 0x00, Array.Empty<byte>());
                bool hasPercentage = caps is { Length: >= 6 } && (caps[5] & 0x02) != 0;
                var info = _connection.RequestRaw(unified.Value, 0x01, Array.Empty<byte>());
                if (info is { Length: >= 7 })
                {
                    int percentage = info[4];
                    int level = info[5];
                    int status = info[6];
                    if (!hasPercentage)
                    {
                        percentage = level switch
                        {
                            1 => 5,
                            2 => 20,
                            4 => 60,
                            8 => 100,
                            _ => 0,
                        };
                    }

                    return new BatteryInfo(percentage, level, status, status is 1 or 2 or 4);
                }
            }
            catch (HidppException)
            {
                // Fall through to the legacy battery feature.
            }
        }

        var legacy = _connection.GetFeatureIndex(HidppConstants.Features.BatteryStatus);
        if (legacy is not null)
        {
            try
            {
                var response = _connection.RequestRaw(legacy.Value, 0x00, Array.Empty<byte>());
                if (response is { Length: >= 6 })
                {
                    int level = response[4];
                    int status = response[5];
                    int percentage = level switch
                    {
                        1 => 5,
                        2 => 20,
                        4 => 60,
                        8 => 100,
                        _ => 0,
                    };
                    return new BatteryInfo(percentage, level, status, status is 1 or 2 or 4);
                }
            }
            catch (HidppException)
            {
                // Ignore; caller will show unavailable.
            }
        }

        return new BatteryInfo(0, 0, 0, false);
    }

    public ushort ReadDpi()
    {
        byte index = RequireFeature(HidppConstants.Features.AdjustableDpi);
        var response = _connection.RequestRaw(index, 0x02, new byte[] { 0x00 });
        if (response is null || response.Length < 7)
        {
            throw new HidppTimeoutException("读取 DPI 超时。");
        }

        return (ushort)((response[5] << 8) | response[6]);
    }

    public void SetDpi(ushort dpi)
    {
        byte index = RequireFeature(HidppConstants.Features.AdjustableDpi);
        var response = _connection.RequestRaw(index, 0x03,
            new byte[] { 0x00, (byte)(dpi >> 8), (byte)(dpi & 0xFF) });
        if (response is null)
        {
            throw new HidppTimeoutException("写入 DPI 超时。");
        }

        ushort actual = ReadDpi();
        if (actual != dpi)
        {
            throw new HidppException($"DPI 写入未生效：期望 {dpi}，实际 {actual}。");
        }
    }

    public int ReadReportRateMs()
    {
        byte index = RequireFeature(HidppConstants.Features.ReportRate);
        var response = _connection.RequestRaw(index, 0x01, Array.Empty<byte>());
        if (response is null || response.Length < 5)
        {
            throw new HidppTimeoutException("读取回报率超时。");
        }

        return response[4];
    }

    public IReadOnlyList<int> ReadReportRateOptions()
    {
        byte index = RequireFeature(HidppConstants.Features.ReportRate);
        var response = _connection.RequestRaw(index, 0x00, Array.Empty<byte>());
        if (response is null || response.Length < 5)
        {
            return new[] { 1, 2, 4, 8 };
        }

        byte mask = response[4];
        var options = new List<int>();
        for (int bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                options.Add(bit + 1);
            }
        }

        return options.Count > 0 ? options : new[] { 1, 2, 4, 8 };
    }

    public void SetReportRateMs(byte milliseconds)
    {
        byte index = RequireFeature(HidppConstants.Features.ReportRate);
        var response = _connection.RequestRaw(index, 0x02, new[] { milliseconds });
        if (response is null)
        {
            throw new HidppTimeoutException("写入回报率超时。");
        }

        int actual = ReadReportRateMs();
        if (actual != milliseconds)
        {
            throw new HidppException($"回报率写入未生效：期望 {milliseconds}ms，实际 {actual}ms。");
        }
    }

    public byte[] ReadControlSector()
    {
        return ReadSector(0, Descriptor.Size);
    }

    public IReadOnlyList<ControlHeader> ReadControlHeaders()
    {
        var control = ReadControlSector();
        var headers = new List<ControlHeader>();
        for (int offset = 0; offset + 4 <= control.Length; offset += 4)
        {
            int sector = (control[offset] << 8) | control[offset + 1];
            if (sector == 0xFFFF)
            {
                break;
            }

            headers.Add(new ControlHeader(sector, control[offset + 2] != 0, offset));
        }

        return headers;
    }

    public void EnableProfiles(int count)
    {
        if (count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var control = ReadControlSector();
        var headers = ReadControlHeaders();
        int changed = 0;
        foreach (var header in headers)
        {
            if (header.Sector is >= 1 and <= 5 && header.Sector <= count && !header.Enabled)
            {
                control[header.Offset + 2] = 0x01;
                changed++;
            }
        }

        if (changed == 0)
        {
            return;
        }

        SetCrc(control);
        WriteSector(0, control);

        var verify = ReadControlHeaders();
        foreach (int sector in Enumerable.Range(1, count))
        {
            if (!verify.Any(h => h.Sector == sector && h.Enabled))
            {
                throw new HidppException($"启用 Profile {sector} 失败。");
            }
        }
    }

    public byte[] ReadSector(int sector, int size)
    {
        if (size is < 16 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        byte index = RequireFeature(HidppConstants.Features.OnboardProfiles);
        var data = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            int requestOffset = Math.Min(offset, size - 16);
            var response = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.ReadSector,
                new[]
                {
                    (byte)(sector >> 8), (byte)(sector & 0xFF),
                    (byte)(requestOffset >> 8), (byte)(requestOffset & 0xFF),
                });
            if (response is null || response.Length < 20)
            {
                throw new HidppTimeoutException($"读取板载配置 sector {sector} 超时。");
            }

            int copyStart = offset - requestOffset;
            int copyLength = Math.Min(16, size - offset);
            Array.Copy(response, 4 + copyStart, data, offset, copyLength);
            offset += copyLength;
        }

        return data;
    }

    public void WriteSector(int sector, byte[] data)
    {
        if (data.Length is < 16 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        byte index = RequireFeature(HidppConstants.Features.OnboardProfiles);
        int size = data.Length;
        var start = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.StartWrite,
            new[]
            {
                (byte)(sector >> 8), (byte)(sector & 0xFF),
                (byte)0x00, (byte)0x00,
                (byte)(size >> 8), (byte)(size & 0xFF),
            });
        if (start is null)
        {
            throw new HidppTimeoutException($"开始写入 sector {sector} 超时。");
        }

        for (int offset = 0; offset < size; offset += 16)
        {
            var chunk = new byte[16];
            Array.Copy(data, offset, chunk, 0, Math.Min(16, size - offset));
            var response = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.WriteChunk, chunk);
            if (response is null)
            {
                throw new HidppTimeoutException($"写入 sector {sector} 数据块超时。");
            }
        }

        var commit = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.CommitWrite, Array.Empty<byte>());
        if (commit is null)
        {
            throw new HidppTimeoutException($"提交 sector {sector} 超时。");
        }

        Thread.Sleep(120);
    }

    public OnboardProfile ReadProfile(int index)
    {
        if (index is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return OnboardProfile.Parse(index, ReadSector(index, Descriptor.Size));
    }

    public void WriteProfile(OnboardProfile profile)
    {
        WriteSector(profile.Index, profile.ToBytes());

        var verify = ReadProfile(profile.Index);
        if (!verify.CrcValid)
        {
            throw new HidppException($"Profile {profile.Index} 写入后 CRC 校验失败。");
        }

        if (verify.ReportRateMs != profile.ReportRateMs ||
            verify.DefaultDpiIndex != profile.DefaultDpiIndex ||
            !verify.DpiSlots.SequenceEqual(profile.DpiSlots) ||
            verify.Name != profile.Name)
        {
            throw new HidppException($"Profile {profile.Index} 写入后回读不一致。");
        }
    }

    public int GetCurrentProfile()
    {
        byte index = RequireFeature(HidppConstants.Features.OnboardProfiles);
        var response = _connection.RequestRaw(index, HidppConstants.OnboardFunctions.GetCurrentProfile, Array.Empty<byte>());
        if (response is null || response.Length < 6)
        {
            throw new HidppTimeoutException("读取当前 Profile 超时。");
        }

        return (response[4] << 8) | response[5];
    }

    /// <summary>
    /// Switches the onboard profile and applies its DPI + report rate immediately.
    /// The firmware applies report rate on profile switch, but (at least on GPW2) does not
    /// always reload the profile's default DPI, so the host re-applies it explicitly.
    /// </summary>
    public int ReadOnboardMode()
    {
        byte feature = RequireFeature(HidppConstants.Features.OnboardProfiles);
        var response = _connection.RequestRaw(feature, HidppConstants.OnboardFunctions.GetMode, Array.Empty<byte>());
        if (response is null || response.Length < 5)
        {
            throw new HidppTimeoutException("读取板载模式超时。");
        }

        return response[4];
    }

    /// <summary>Switches between onboard profiles (1) and host control (2).</summary>
    public void SetOnboardMode(int mode)
    {
        if (mode is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        byte feature = RequireFeature(HidppConstants.Features.OnboardProfiles);
        // Some firmware does not acknowledge the mode switch; the mode read-back is authoritative.
        _connection.RequestRaw(feature, HidppConstants.OnboardFunctions.SetMode, new[] { (byte)mode });
        Thread.Sleep(100);
        int actual = ReadOnboardMode();
        if (actual != mode)
        {
            throw new HidppException($"板载模式切换失败：期望 {mode}，实际 {actual}。");
        }
    }

    /// <summary>
    /// In onboard mode the firmware can report feature values without the sensor following them.
    /// Host mode makes 0x2201/0x8060 the authoritative live settings.
    /// </summary>
    public void EnsureHostMode()
    {
        if (ReadOnboardMode() != 2)
        {
            SetOnboardMode(2);
        }
    }

    /// <summary>Applies DPI and report rate to the live sensor without touching onboard flash.</summary>
    public void ApplyLive(ushort dpi, byte reportRateMs)
    {
        if (ReadDpi() != dpi)
        {
            SetDpi(dpi);
        }

        if (ReadReportRateMs() != reportRateMs)
        {
            SetReportRateMs(reportRateMs);
        }
    }

    public void ApplyProfile(int index)
    {
        try
        {
            if (GetCurrentProfile() != index)
            {
                SetCurrentProfile(index);
            }
        }
        catch
        {
            // Host mode may reject profile switching; live DPI/rate below still apply.
        }

        var profile = ReadProfile(index);
        int slot = Math.Clamp((int)profile.DefaultDpiIndex, 0, OnboardProfile.DpiSlotCount - 1);
        ushort dpi = (ushort)profile.DpiSlots[slot];
        if (ReadDpi() != dpi)
        {
            SetDpi(dpi);
        }

        if (ReadReportRateMs() != profile.ReportRateMs)
        {
            SetReportRateMs(profile.ReportRateMs);
        }
    }

    public void SetCurrentProfile(int index)
    {
        if (index is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        byte feature = RequireFeature(HidppConstants.Features.OnboardProfiles);
        var response = _connection.RequestRaw(feature, HidppConstants.OnboardFunctions.SetCurrentProfile,
            new[] { (byte)(index >> 8), (byte)(index & 0xFF) });
        if (response is null)
        {
            throw new HidppTimeoutException("切换 Profile 超时。");
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(80);
            if (GetCurrentProfile() == index)
            {
                return;
            }
        }

        throw new HidppException($"切换到 Profile {index} 未生效。");
    }

    private byte RequireFeature(ushort featureId)
    {
        var index = _connection.GetFeatureIndex(featureId);
        if (index is null)
        {
            throw new HidppException($"设备不支持 feature 0x{featureId:X4}。");
        }

        return index.Value;
    }

    private static void SetCrc(byte[] data)
    {
        ushort crc = Crc16.Compute(data.AsSpan(0, data.Length - 2));
        data[^2] = (byte)(crc >> 8);
        data[^1] = (byte)(crc & 0xFF);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
        _transport.Dispose();
    }
}

public sealed record ControlHeader(int Sector, bool Enabled, int Offset);
