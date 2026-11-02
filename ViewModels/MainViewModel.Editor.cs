namespace AppSwitcher.ViewModels;

public partial class MainViewModel
{
    internal string EditorSnapshot => CreateConfigSignature(BuildCurrentConfigRoot());
    internal string PersistedEditorSnapshot => _persistedConfigSignature;

    internal async Task RestoreEditorSnapshotAsync(string json)
    {
        if (!CanAdminister) return;
        var snapshot = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
            ?? throw new InvalidDataException("Invalid editor snapshot.");
        _suppressSettingsNotifications = true;
        try
        {
            CopySettings(snapshot.Settings, Settings);
            var existing = Apps.ToDictionary(app => app.AppId);
            var restored = new List<AppItem>();
            foreach (var source in snapshot.Apps)
            {
                if (!existing.TryGetValue(source.AppId, out var target)) target = source;
                else
                    foreach (var property in typeof(AppItem).GetProperties().Where(property => property.CanRead && property.CanWrite
                        && !Attribute.IsDefined(property, typeof(JsonIgnoreAttribute))))
                        property.SetValue(target, property.GetValue(source));
                restored.Add(target);
            }
            // Preserve live process/health objects; undo never launches or stops an app.
            SyncCollection(Apps, restored);
            SyncCollection(Groups, snapshot.Groups);
            SyncCollection(Layouts, snapshot.Layouts);
            RefreshAppsSnapshot();
            RebuildGroups();
        }
        finally { _suppressSettingsNotifications = false; }
        await ApplySettingsStateAsync(showNotifications: false);
    }
}
