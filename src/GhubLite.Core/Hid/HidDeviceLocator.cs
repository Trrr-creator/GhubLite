using HidSharp;

namespace GhubLite.Core.Hid;

public static class HidDeviceLocator
{
    private static readonly int[] KnownProductIds =
    {
        0xC547, // Lightspeed receiver (GPW2 / G502 X family)
        0xC094, // PRO X Superlight, wired (black)
        0xC095, // PRO X Superlight, wired (white)
        0xC096,
        0xC098,
        0xC099,
    };

    /// <summary>
    /// Finds Logitech HID collections that can carry HID++ 2.0 long reports (20 bytes).
    /// Known PIDs are tried first; other Logitech devices are a last-resort fallback.
    /// </summary>
    public static IReadOnlyList<HidDevice> FindLongReportDevices()
    {
        var logitech = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == 0x046D)
            .Where(d => d.GetMaxInputReportLength() >= 20 && d.GetMaxOutputReportLength() >= 20)
            .GroupBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return logitech
            .OrderByDescending(d => KnownProductIds.Contains(d.ProductID))
            .ToList();
    }

    public static bool IsKnownProduct(int productId) => KnownProductIds.Contains(productId);
}
