namespace AppSwitcher.Models;

public partial class AppGroup : ObservableObject
{
    [ObservableProperty] private string _key;
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private ObservableCollection<AppItem> _apps = [];
    [ObservableProperty] private int _sortOrder = 0;

    public AppGroup(string key, string name)
    {
        _key = key;
        _name = name;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
