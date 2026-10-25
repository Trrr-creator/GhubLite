using HidSharp;

namespace GhubLite.Core.Hid;

/// <summary>Thin, locked wrapper around a Windows HID stream.</summary>
public sealed class HidppTransport : IDisposable
{
    private readonly HidStream _stream;
    private readonly object _gate = new();
    private bool _disposed;

    public HidDevice Device { get; }
    public int MaxInputReportLength { get; }
    public int MaxOutputReportLength { get; }

    public HidppTransport(HidDevice device)
    {
        Device = device;
        MaxInputReportLength = device.GetMaxInputReportLength();
        MaxOutputReportLength = device.GetMaxOutputReportLength();
        _stream = device.Open();
        _stream.ReadTimeout = 1200;
        _stream.WriteTimeout = 1200;
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stream.Write(report.ToArray());
        }
    }

    public byte[]? Read(int timeoutMs)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var buffer = new byte[Math.Max(64, MaxInputReportLength)];
            _stream.ReadTimeout = Math.Max(1, timeoutMs);
            try
            {
                int n = _stream.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    return null;
                }

                var result = new byte[n];
                Array.Copy(buffer, result, n);
                return result;
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                _stream.ReadTimeout = 1200;
            }
        }
    }

    public void Drain()
    {
        for (int i = 0; i < 4; i++)
        {
            if (Read(5) is null)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
        }
    }
}
