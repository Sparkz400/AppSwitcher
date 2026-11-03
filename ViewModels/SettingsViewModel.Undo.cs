namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    private readonly List<string> _undoSnapshots = [];
    private string? _lastEditorSnapshot;
    private long _lastEditTimestamp;
    private bool _restoringEditor;
    internal async Task DiscardChangesAsync()
    {
        _restoringEditor = true;
        try
        {
            await _mainVm.DiscardEditorChangesAsync();
            HasInputErrors = false;
            ResetEditorHistory();
        }
        finally { _restoringEditor = false; RefreshSaveState(); }
    }
    public bool CanUndo => IsAdmin && _undoSnapshots.Count > 0 && !HasInputErrors && !SaveCommand.IsRunning && !_restoringEditor;

    private void TrackEditorChanges(string current)
    {
        if (!IsAdmin || _restoringEditor) return;
        if (_lastEditorSnapshot != null && current != _lastEditorSnapshot)
        {
            // A typing burst, a slider drag, or a multi-property command is one edit.
            if (_undoSnapshots.Count == 0 || Stopwatch.GetElapsedTime(_lastEditTimestamp) > TimeSpan.FromMilliseconds(600))
            {
                _undoSnapshots.Add(_lastEditorSnapshot);
                if (_undoSnapshots.Count > 30) _undoSnapshots.RemoveAt(0);
            }
            _lastEditTimestamp = Stopwatch.GetTimestamp();
        }
        _lastEditorSnapshot = current;
        if (!HasUnsavedChanges) _undoSnapshots.Clear();
        NotifyEditorCommands();
    }

    private void ResetEditorHistory()
    {
        _undoSnapshots.Clear();
        _lastEditorSnapshot = _mainVm.PersistedEditorSnapshot;
        _lastEditTimestamp = 0;
    }

    private void NotifyEditorCommands()
    {
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
        RestartLauncherCommand.NotifyCanExecuteChanged();
    }

    private void OnSaveCommandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaveCommand.IsRunning)) NotifyEditorCommands();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        if (!CanUndo || !_mainVm.CanAdminister) return;
        var snapshot = _undoSnapshots[^1];
        var selectedId = SelectedApp?.AppId;
        _restoringEditor = true;
        NotifyEditorCommands();
        try
        {
            await _mainVm.RestoreEditorSnapshotAsync(snapshot);
            _undoSnapshots.RemoveAt(_undoSnapshots.Count - 1);
            _lastEditorSnapshot = _mainVm.EditorSnapshot;
            _lastEditTimestamp = 0;
            SelectedApp = VisibleApps.FirstOrDefault(app => app.AppId == selectedId) ?? VisibleApps.FirstOrDefault();
        }
        catch (Exception ex) { _notificationService.Show(LocalizationService.T("UI_UndoFailed", ex.Message), NotificationType.Error); }
        finally { _restoringEditor = false; RefreshSaveState(); }
    }
}
