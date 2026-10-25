using System.Text;
using GhubLite.Core.Hidpp;

namespace GhubLite.Core.Models;

/// <summary>
/// Legacy 0x8100 onboard profile (255 bytes). All multibyte fields except the name are little-endian.
/// Button, lighting and macro data are preserved untouched; this app only edits DPI/report-rate/name.
/// </summary>
public sealed class OnboardProfile
{
    public const int ProfileSize = 255;
    public const int DpiSlotCount = 5;

    public int Index { get; }
    public byte[] Raw { get; }
    public bool CrcValid { get; }

    public byte ReportRateMs
    {
        get => Raw[0];
        set => Raw[0] = value;
    }

    public byte DefaultDpiIndex
    {
        get => Raw[1];
        set => Raw[1] = (byte)Math.Clamp((int)value, 0, DpiSlotCount - 1);
    }

    public byte ShiftDpiIndex
    {
        get => Raw[2];
        set => Raw[2] = (byte)Math.Clamp((int)value, 0, DpiSlotCount - 1);
    }

    public int[] DpiSlots
    {
        get
        {
            var slots = new int[DpiSlotCount];
            for (int i = 0; i < DpiSlotCount; i++)
            {
                slots[i] = Raw[3 + i * 2] | (Raw[4 + i * 2] << 8);
            }
            return slots;
        }
    }

    public string Name
    {
        get => Encoding.Unicode.GetString(Raw, 160, 48).TrimEnd('\0', '\uFFFF');
        set
        {
            var bytes = Encoding.Unicode.GetBytes(value.Length > 24 ? value[..24] : value);
            Array.Clear(Raw, 160, 48);
            Array.Copy(bytes, 0, Raw, 160, Math.Min(bytes.Length, 48));
        }
    }

    public int StoredCrc => (Raw[253] << 8) | Raw[254];

    public OnboardProfile(int index, byte[] raw)
    {
        if (raw.Length != ProfileSize)
        {
            throw new ArgumentException($"Onboard profile must be {ProfileSize} bytes.", nameof(raw));
        }

        Index = index;
        Raw = raw;
        CrcValid = Crc16.Compute(raw.AsSpan(0, ProfileSize - 2)) == StoredCrc;
    }

    public static OnboardProfile Parse(int index, byte[] raw) => new(index, raw);

    public OnboardProfile Clone()
    {
        var copy = new byte[ProfileSize];
        Array.Copy(Raw, copy, ProfileSize);
        return new OnboardProfile(Index, copy);
    }

    public void SetDpiSlots(IReadOnlyList<int> slots, int defaultIndex)
    {
        if (slots.Count != DpiSlotCount)
        {
            throw new ArgumentException($"Exactly {DpiSlotCount} DPI slots are required.", nameof(slots));
        }

        for (int i = 0; i < DpiSlotCount; i++)
        {
            ushort value = (ushort)Math.Clamp(slots[i], 100, 25600);
            Raw[3 + i * 2] = (byte)(value & 0xFF);
            Raw[4 + i * 2] = (byte)(value >> 8);
        }

        DefaultDpiIndex = (byte)Math.Clamp(defaultIndex, 0, DpiSlotCount - 1);
    }

    public void RefreshCrc()
    {
        ushort crc = Crc16.Compute(Raw.AsSpan(0, ProfileSize - 2));
        Raw[253] = (byte)(crc >> 8);
        Raw[254] = (byte)(crc & 0xFF);
    }

    public byte[] ToBytes()
    {
        RefreshCrc();
        var copy = new byte[ProfileSize];
        Array.Copy(Raw, copy, ProfileSize);
        return copy;
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Profile {Index}" : Name;
}
