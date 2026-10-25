namespace GhubLite.App.ViewModels;

public sealed class DpiSlotVm : ObservableObject
{
    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => Value.ToString();

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public DpiSlotVm(int value)
    {
        _value = value;
    }
}
