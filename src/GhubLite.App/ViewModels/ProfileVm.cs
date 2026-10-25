using System.Windows.Media;

namespace GhubLite.App.ViewModels;

public sealed class ProfileVm : ObservableObject
{
    private static readonly string[] Palette =
    {
        "#39D0FF",
        "#7C5CFF",
        "#48E0A4",
        "#FFC857",
        "#FF7A9C",
    };

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    private string _name = "";
    private bool _isEnabled;
    private int[] _dpiSlots = { 400, 800, 1600, 3200, 6400 };
    private int _defaultDpiIndex = 1;
    private int _reportRateMs = 1;
    private bool _isActive;
    private int _assignedCount;

    public int Index { get; }
    public string ShortName => $"P{Index}";
    public string AccentHex { get; }
    public SolidColorBrush AccentBrush => GetBrush(AccentHex);

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                OnPropertyChanged(nameof(NameDisplay));
            }
        }
    }

    public string NameDisplay => string.IsNullOrWhiteSpace(Name) ? $"Profile {Index}" : Name;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (Set(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(EnabledText));
            }
        }
    }

    public string EnabledText => IsEnabled ? "已启用" : "未启用";

    public int[] DpiSlots
    {
        get => _dpiSlots;
        private set
        {
            _dpiSlots = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultDpi));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public int DefaultDpiIndex
    {
        get => _defaultDpiIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, 4);
            if (Set(ref _defaultDpiIndex, clamped))
            {
                OnPropertyChanged(nameof(DefaultDpi));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int DefaultDpi => DpiSlots[Math.Clamp(DefaultDpiIndex, 0, 4)];

    public int ReportRateMs
    {
        get => _reportRateMs;
        set
        {
            int clamped = Math.Clamp(value, 1, 8);
            if (Set(ref _reportRateMs, clamped))
            {
                OnPropertyChanged(nameof(ReportRateHz));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int ReportRateHz => 1000 / Math.Max(1, ReportRateMs);

    public string Summary => $"{DefaultDpi} DPI · {ReportRateHz} Hz";

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public int AssignedCount
    {
        get => _assignedCount;
        set
        {
            if (Set(ref _assignedCount, value))
            {
                OnPropertyChanged(nameof(AssignedText));
            }
        }
    }

    public string AssignedText => AssignedCount == 0 ? "未分配应用" : $"{AssignedCount} 个应用";

    public ProfileVm(int index)
    {
        Index = index;
        AccentHex = Palette[(index - 1) % Palette.Length];
        Name = index == 1 ? "Desktop" : $"Profile {index}";
    }

    public void Apply(int[] slots, int defaultIndex, int reportRateMs, string name, bool enabled)
    {
        DpiSlots = slots;
        DefaultDpiIndex = defaultIndex;
        ReportRateMs = reportRateMs;
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name;
        }

        IsEnabled = enabled;
    }

    private static SolidColorBrush GetBrush(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            BrushCache[hex] = brush;
        }

        return brush;
    }
}
