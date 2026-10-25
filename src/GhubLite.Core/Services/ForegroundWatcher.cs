using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GhubLite.Core.Services;

public sealed class ForegroundWatcher : IDisposable
{
    private readonly Timer _timer;
    private string? _lastProcess;
    private int _tickGate;
    private bool _disposed;

    public event Action<string>? ForegroundProcessChanged;

    public bool Enabled { get; set; } = true;

    public ForegroundWatcher()
    {
        _timer = new Timer(Tick, null, 800, 700);
    }

    public static string? GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
        catch
        {
            return null;
        }
    }

    public static string? GetForegroundProcessPath()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private void Tick(object? _)
    {
        if (_disposed || !Enabled)
        {
            return;
        }

        if (Interlocked.Exchange(ref _tickGate, 1) != 0)
        {
            return;
        }

        try
        {
            string? process = GetForegroundProcessName();
            if (process is null || string.Equals(process, _lastProcess, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastProcess = process;
            ForegroundProcessChanged?.Invoke(process);
        }
        finally
        {
            Interlocked.Exchange(ref _tickGate, 0);
        }
    }

    public void Reset() => _lastProcess = null;

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
