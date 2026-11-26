namespace AppSwitcher.ViewModels;

public partial class MainViewModel
{
    internal string EditorSnapshot => CreateConfigSignature(BuildCurrentConfigRoot());
    internal string PersistedEditorSnapshot => _persistedConfigSignature;

    internal async Task RestoreEditorSnapshotAsync(string json)
    {
        if (!CanAdminister) return;
        await RestoreSnapshotCoreAsync(json);
    }

    // On expiry only our persisted configuration may be restored, never a caller's input.
    internal Task DiscardEditorChangesAsync() => RestoreSnapshotCoreAsync(PersistedEditorSnapshot);

    private async Task RestoreSnapshotCoreAsync(string json)
    {
        var snapshot = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
            ?? throw new InvalidDataException("Invalid editor snapshot.");
        var kioskBefore = Settings.KioskMode;
        _suppressSettingsNotifications = true;
        try
        {
            CopySettings(snapshot.Settings, Settings);
            // Undo never launches or stops an app.
            SyncCollection(Apps, MergeAppsById(snapshot.Apps));
            SyncCollection(Groups, snapshot.Groups);
            SyncCollection(Layouts, snapshot.Layouts);
            RefreshAppsSnapshot();
            RebuildGroups();
        }
        finally { _suppressSettingsNotifications = false; }
        // Undo/discard can switch kiosk on or off; that must be announced and audited like a direct toggle.
        await ApplySettingsStateAsync(showNotifications: Settings.KioskMode != kioskBefore);
    }

    private static readonly System.Reflection.PropertyInfo[] PersistedAppProperties = typeof(AppItem).GetProperties()
        .Where(property => property.CanRead && property.CanWrite && !Attribute.IsDefined(property, typeof(JsonIgnoreAttribute)))
        .ToArray();

    // Keep the live AppItem (process, health, uptime state) for every app whose AppId survives; copy only saved fields.
    private List<AppItem> MergeAppsById(IEnumerable<AppItem> incoming)
    {
        var existing = Apps.GroupBy(app => app.AppId).ToDictionary(group => group.Key, group => group.First());
        var merged = new List<AppItem>();
        foreach (var source in incoming)
        {
            if (!existing.Remove(source.AppId, out var target)) target = source;
            else
                foreach (var property in PersistedAppProperties)
                    property.SetValue(target, property.GetValue(source));
            merged.Add(target);
        }
        return merged;
    }
}
