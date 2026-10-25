using System.Text;
using GhubLite.Core.Hid;

namespace GhubLite.Core.Hidpp;

/// <summary>
/// One HID++ 2.0 endpoint (a device index behind a receiver, or a directly wired device).
/// </summary>
public sealed class HidppConnection : IDisposable
{
    private readonly HidppTransport _transport;
    private readonly object _gate = new();
    private readonly Dictionary<ushort, byte> _features = new();
    private byte _swId;
    private bool _disposed;

    public byte DeviceIndex { get; }
    public (int Major, int Minor)? Protocol { get; private set; }
    public IReadOnlyDictionary<ushort, byte> Features => _features;

    public HidppConnection(HidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        DeviceIndex = deviceIndex;
    }

    public (int Major, int Minor)? Ping(int timeoutMs = 1500)
    {
        var response = RequestRaw(0x00, 0x01, new byte[] { 0x00, 0x00, 0x5A }, timeoutMs);
        if (response is null || response.Length < 7 || response[2] == 0xFF || response[6] != 0x5A)
        {
            return null;
        }

        Protocol = (response[4], response[5]);
        return Protocol;
    }

    public byte? GetFeatureIndex(ushort featureId)
    {
        if (featureId == HidppConstants.Features.Root)
        {
            return 0;
        }

        if (_features.TryGetValue(featureId, out byte cached))
        {
            return cached;
        }

        try
        {
            var response = RequestRaw(0x00, 0x00,
                new[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) });
            if (response is null || response.Length < 5 || response[2] == 0xFF)
            {
                return null;
            }

            byte index = response[4];
            if (index == 0)
            {
                return null;
            }

            _features[featureId] = index;
            return index;
        }
        catch (HidppException ex) when (ex.ErrorCode is 0x01 or 0x02)
        {
            return null;
        }
    }

    /// <summary>Walks the FEATURE_SET table and caches every supported feature index.</summary>
    public IReadOnlyDictionary<ushort, byte> EnumerateFeatures()
    {
        var setIndex = GetFeatureIndex(HidppConstants.Features.FeatureSet);
        if (setIndex is null)
        {
            return _features;
        }

        try
        {
            var countResponse = RequestRaw(setIndex.Value, 0x00, Array.Empty<byte>());
            int count = countResponse is { Length: >= 5 } ? countResponse[4] : 0;
            for (int i = 0; i < count; i++)
            {
                var response = RequestRaw(setIndex.Value, 0x01, new[] { (byte)i });
                if (response is null || response.Length < 6 || response[2] == 0xFF)
                {
                    continue;
                }

                ushort featureId = (ushort)((response[4] << 8) | response[5]);
                if (featureId == 0)
                {
                    continue;
                }

                var index = GetFeatureIndex(featureId);
                if (index is not null)
                {
                    _features[featureId] = index.Value;
                }
            }
        }
        catch (HidppException)
        {
            // Some firmware only supports root-level discovery; the feature cache still has what worked.
        }

        return _features;
    }

    public byte[]? RequestFeature(ushort featureId, byte function, params byte[] parameters)
    {
        var index = GetFeatureIndex(featureId);
        return index is null ? null : RequestRaw(index.Value, function, parameters);
    }

    public byte[]? RequestRaw(byte featureIndex, byte function, byte[] parameters, int timeoutMs = 1200)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (parameters.Length > 16)
            {
                throw new ArgumentException("HID++ long report payload is limited to 16 bytes.", nameof(parameters));
            }

            _transport.Drain();

            byte swId = NextSoftwareId();
            var report = new byte[HidppConstants.LongReportSize];
            report[0] = HidppConstants.ReportIdLong;
            report[1] = DeviceIndex;
            report[2] = featureIndex;
            report[3] = (byte)((function << 4) | swId);
            Array.Copy(parameters, 0, report, 4, parameters.Length);
            _transport.Write(report);

            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                int remaining = (int)Math.Max(10, deadline - Environment.TickCount64);
                var response = _transport.Read(remaining);
                if (response is null)
                {
                    break;
                }

                if (response.Length < 4)
                {
                    continue;
                }

                if (response[0] != HidppConstants.ReportIdLong &&
                    response[0] != HidppConstants.ReportIdShort)
                {
                    continue;
                }

                if (response[1] != DeviceIndex && response[1] != (byte)(DeviceIndex ^ 0xFF))
                {
                    continue;
                }

                if (response[2] == 0xFF)
                {
                    byte code = response.Length > 6 ? response[6] : (byte)0;
                    throw new HidppException($"HID++ error 0x{code:X2}", code);
                }

                if ((response[3] & 0x0F) == swId && response[2] == featureIndex)
                {
                    return response;
                }
            }

            return null;
        }
    }

    public string? GetDeviceName()
    {
        var index = GetFeatureIndex(HidppConstants.Features.DeviceName);
        if (index is null)
        {
            return null;
        }

        var countResponse = RequestRaw(index.Value, 0x00, Array.Empty<byte>());
        if (countResponse is null || countResponse.Length < 5)
        {
            return null;
        }

        int count = countResponse[4];
        if (count <= 0 || count > 255)
        {
            return null;
        }

        var bytes = new List<byte>(count);
        for (int chunk = 0; chunk * 16 < count; chunk++)
        {
            var response = RequestRaw(index.Value, 0x01, new[] { (byte)chunk });
            if (response is null || response.Length < 5)
            {
                break;
            }

            int take = Math.Min(16, count - bytes.Count);
            for (int i = 0; i < take && 4 + i < response.Length; i++)
            {
                bytes.Add(response[4 + i]);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0', ' ');
    }

    private byte NextSoftwareId()
    {
        _swId = (byte)((_swId % 15) + 1);
        return _swId;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
