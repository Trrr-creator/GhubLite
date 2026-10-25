using System.Windows.Media;
using GhubLite.Core.Models;

namespace GhubLite.App.ViewModels;

public sealed class RuleVm : ObservableObject
{
    public string ProcessName { get; }
    public string DisplayName { get; }
    public string Initial { get; }
    public SolidColorBrush AccentBrush { get; }

    private int _profile;

    public int Profile
    {
        get => _profile;
        set
        {
            if (Set(ref _profile, value))
            {
                OnPropertyChanged(nameof(ProfileLabel));
                OnPropertyChanged(nameof(AccentBrush));
            }
        }
    }

    public string ProfileLabel => $"P{Profile}";

    public RuleVm(AppRule rule, IReadOnlyList<ProfileVm> profiles)
    {
        ProcessName = rule.ProcessName;
        DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName)
            ? Path.GetFileNameWithoutExtension(rule.ProcessName)
            : rule.DisplayName;
        Initial = string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();
        _profile = rule.Profile;
        AccentBrush = profiles.FirstOrDefault(p => p.Index == _profile)?.AccentBrush
                      ?? new SolidColorBrush(Color.FromRgb(0x8C, 0x97, 0xAA));
    }

    public AppRule ToModel() => new()
    {
        ProcessName = ProcessName,
        DisplayName = DisplayName,
        Profile = Profile,
    };
}

public sealed class ReportRateVm : ObservableObject
{
    public int Ms { get; }
    public int Hz => 1000 / Ms;
    public string HzText => Hz.ToString();
    public string Summary => $"{Hz} Hz";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public ReportRateVm(int ms)
    {
        Ms = ms;
    }
}
