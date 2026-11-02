namespace AppSwitcher.ViewModels;

internal enum WindowPickAction { Native, Website, Existing, Manual }
internal sealed record OpenWindowPick(WindowPickAction Action, OpenWindowCandidate? Candidate = null, string Name = "", string? ExistingAppId = null);

internal sealed record OpenWindowChoice(OpenWindowCandidate Candidate, int ExistingCount, string? ExistingName)
{
    public string Title => Candidate.Title;
    public string AppName => Candidate.AppName;
    public ImageSource? Icon => Candidate.Icon;
    public string Status => LocalizationService.T(ExistingCount > 0 ? "WindowPicker_AlreadyAdded" : Candidate.Kind switch
    {
        OpenWindowKind.Website => "WindowPicker_Website",
        OpenWindowKind.Unsupported => "WindowPicker_ManualRequired",
        OpenWindowKind.Unavailable => "WindowPicker_Unavailable",
        _ => ""
    });
}

internal sealed partial class OpenWindowPickerViewModel : ObservableObject, IDisposable
{
    private readonly IOpenWindowCatalog _catalog;
    private readonly Func<bool> _canEdit;
    private readonly Func<IReadOnlyList<AppItem>> _existingApps;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<OpenWindowChoice> _snapshot = [];
    private bool _disposed;
    public ObservableCollection<OpenWindowChoice> Windows { get; } = [];
    public event Action<OpenWindowPick>? Completed;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private OpenWindowChoice? _selectedWindow;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";

    public OpenWindowPickerViewModel(IOpenWindowCatalog catalog, Func<bool> canEdit, Func<IReadOnlyList<AppItem>> existingApps)
    { _catalog = catalog; _canEdit = canEdit; _existingApps = existingApps; }

    public bool CanRefresh => !_disposed && !IsBusy && _canEdit();
    public bool CanConfirm => CanRefresh && SelectedWindow is { } choice && choice.ExistingCount < 2
        && choice.Candidate.Kind is OpenWindowKind.Native or OpenWindowKind.Website
        && (!CanName || !string.IsNullOrWhiteSpace(Name));
    public bool CanName => SelectedWindow is { ExistingCount: 0, Candidate.Kind: OpenWindowKind.Native };
    public bool HasSelection => SelectedWindow != null;
    public string ExecutablePath => SelectedWindow?.Candidate.ExecutablePath ?? "";
    public string ActionLabel => LocalizationService.T(SelectedWindow?.ExistingCount == 1 ? "WindowPicker_ShowExisting"
        : SelectedWindow?.Candidate.Kind == OpenWindowKind.Website ? "WindowPicker_SetupWebsite" : "WindowPicker_Add");
    public string SelectionHint => SelectedWindow is not { } choice ? "" : choice.ExistingCount > 1
        ? LocalizationService.T("WindowPicker_MultipleExisting") : choice.ExistingCount == 1
        ? LocalizationService.T("WindowPicker_ExistingName", choice.ExistingName ?? "") : choice.Candidate.Kind switch
        {
            OpenWindowKind.Website => LocalizationService.T("WindowPicker_WebsiteHint"),
            OpenWindowKind.Unsupported => LocalizationService.T("WindowPicker_ManualHint"),
            OpenWindowKind.Unavailable => LocalizationService.T("WindowPicker_UnavailableHint"),
            _ => ""
        };
    public string EmptyText => IsBusy ? LocalizationService.T("WindowPicker_Loading") :
        LocalizationService.T(_snapshot.Count == 0 ? "WindowPicker_Empty" : "WindowPicker_NoMatches");
    public bool IsEmpty => Windows.Count == 0;

    partial void OnSearchChanged(string value) => Filter();
    partial void OnNameChanged(string value) => ConfirmCommand.NotifyCanExecuteChanged();
    partial void OnSelectedWindowChanged(OpenWindowChoice? value)
    {
        Name = value?.Candidate.AppName ?? "";
        Status = "";
        foreach (var property in new[] { nameof(CanName), nameof(HasSelection), nameof(ExecutablePath), nameof(ActionLabel), nameof(SelectionHint) }) OnPropertyChanged(property);
        ConfirmCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value)
    {
        RefreshAuthorization();
        OnPropertyChanged(nameof(EmptyText));
    }
    public void RefreshAuthorization()
    {
        RefreshCommand.NotifyCanExecuteChanged(); ConfirmCommand.NotifyCanExecuteChanged(); ManualCommand.NotifyCanExecuteChanged();
        if (!_disposed && !_canEdit()) Status = LocalizationService.T("WindowPicker_AccessExpired");
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        if (!CanRefresh) return;
        IsBusy = true;
        Status = "";
        var previous = SelectedWindow?.Candidate;
        var previousName = Name;
        try
        {
            var windows = await _catalog.ScanAsync(_lifetime.Token);
            if (_disposed || !_canEdit()) return;
            _snapshot = windows.Select(candidate =>
            {
                var existing = FindExisting(candidate, _existingApps());
                return new OpenWindowChoice(candidate, existing.Count, existing.FirstOrDefault()?.Name);
            }).ToArray();
            Filter();
            SelectedWindow = previous == null ? null : Windows.FirstOrDefault(choice => OpenWindowCatalog.SameIdentity(previous, choice.Candidate));
            if (SelectedWindow != null) Name = previousName;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_disposed) Status = LocalizationService.T("WindowPicker_ScanFailed"); }
        finally { if (!_disposed) IsBusy = false; }
    }

    private void Filter()
    {
        var previous = SelectedWindow;
        var name = Name;
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Windows.Clear();
        foreach (var choice in _snapshot)
        {
            var text = choice.Title + " " + choice.AppName + " " + choice.Candidate.ExecutablePath;
            if (words.All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase))) Windows.Add(choice);
        }
        SelectedWindow = previous != null && Windows.Contains(previous) ? previous : null;
        if (SelectedWindow != null) Name = name;
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyText));
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task Confirm()
    {
        if (!CanConfirm || SelectedWindow is not { } choice) return;
        var name = Name.Trim();
        IsBusy = true;
        Status = "";
        try
        {
            var current = await _catalog.RevalidateAsync(choice.Candidate, _lifetime.Token);
            if (_disposed || !_canEdit()) return;
            if (!ReferenceEquals(SelectedWindow, choice) || (CanName && Name.Trim() != name)) return;
            if (current == null || !OpenWindowCatalog.SameIdentity(choice.Candidate, current))
            { Status = LocalizationService.T("WindowPicker_Changed"); return; }
            var existing = FindExisting(current, _existingApps());
            if (existing.Count > 1) { Status = LocalizationService.T("WindowPicker_MultipleExisting"); return; }
            if (existing.Count == 1) Completed?.Invoke(new(WindowPickAction.Existing, current, ExistingAppId: existing[0].AppId));
            else if (current.Kind == OpenWindowKind.Website && current.Browser != null) Completed?.Invoke(new(WindowPickAction.Website, current));
            else if (current.Kind == OpenWindowKind.Native && name.Length > 0) Completed?.Invoke(new(WindowPickAction.Native, current, name));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_disposed) Status = LocalizationService.T("WindowPicker_Changed"); }
        finally { if (!_disposed) IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void Manual()
    {
        if (CanRefresh) Completed?.Invoke(new(WindowPickAction.Manual));
    }

    internal static IReadOnlyList<AppItem> FindExisting(OpenWindowCandidate candidate, IReadOnlyList<AppItem> apps) =>
        candidate.Kind != OpenWindowKind.Native ? [] : apps.Where(app => app.IsNative
            && OpenWindowCatalog.SameExecutable(app.Path, candidate.ExecutablePath)).ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        // In-flight operations can still observe the token; dispose after their continuation completes.
        var tasks = new[] { RefreshCommand.ExecutionTask, ConfirmCommand.ExecutionTask }.OfType<Task>().ToArray();
        if (tasks.Length == 0) _lifetime.Dispose();
        else _ = Task.WhenAll(tasks).ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        Completed = null;
        SelectedWindow = null;
        _snapshot = [];
        Windows.Clear();
    }
}
