using System.ComponentModel;
using System.Collections.Specialized;

namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    // Runtime metrics change frequently. Only persisted properties may trigger
    // configuration comparison; there is no polling or serialization on each tick.
    private static readonly HashSet<string> PersistedAppProperties = typeof(AppItem).GetProperties()
        .Where(property => property.CanRead && !Attribute.IsDefined(property, typeof(JsonIgnoreAttribute)))
        .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    private readonly HashSet<GroupNameEntry> _trackedGroupNames = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSaveAction), nameof(CanSave))]
    private bool _hasUnsavedChanges;

    public bool ShowSaveAction => IsAdmin && (HasUnsavedChanges || HasInputErrors);

    private void RefreshSaveState()
    {
        if (_restoringEditor) return;
        var snapshot = _mainVm.EditorSnapshot;
        HasUnsavedChanges = snapshot != _mainVm.PersistedEditorSnapshot;
        SaveStatus = LocalizationService.T(!IsAdmin ? "UI_ReadOnly"
            : HasInputErrors ? "UI_FieldErrors"
            : HasUnsavedChanges ? "UI_SaveHint" : "UI_NoChanges");
        TrackEditorChanges(snapshot);
        RefreshSetupSteps();
    }

    partial void OnHasInputErrorsChanged(bool value) => RefreshSaveState();

    private void OnLayoutsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshWorkspaceCards();
        RefreshSaveState();
    }

    private void TrackGroupNames()
    {
        var current = Groups.SelectMany(group => group.NameEntries).ToHashSet();
        foreach (var entry in _trackedGroupNames.Except(current).ToList())
        {
            entry.PropertyChanged -= OnGroupNameChanged;
            _trackedGroupNames.Remove(entry);
        }
        foreach (var entry in current.Except(_trackedGroupNames))
        {
            // BuildNameEntries has already subscribed the dictionary updater.
            entry.PropertyChanged += OnGroupNameChanged;
            _trackedGroupNames.Add(entry);
        }
    }

    private void OnGroupNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GroupNameEntry.Name)) return;
        UpdateGroupDisplayNames();
        RefreshOperatorScopeEntries();
        RefreshSaveState();
    }
}
