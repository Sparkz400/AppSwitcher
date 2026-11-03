using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;

internal static partial class Program
{
    private static async Task CheckSettingsCatalog(SettingsWindow window, SettingsViewModel editor)
    {
        var selected = editor.SelectedApp;
        var section = editor.ActiveSection;
        var website = new AppItem { Name = "Synthetic website search", TargetKind = AppTargetKind.Website,
            WebsiteUrl = "https://example.invalid", BrowserProfileId = Guid.NewGuid().ToString("N") };
        editor.Apps.Add(website);
        try
        {
            foreach (var entry in SettingsViewModel.SearchCatalog.Where(entry => entry.PropertyPath.Length > 0))
            {
                Check(LocalizationService.T(entry.LabelKey) != entry.LabelKey, $"Search label exists: {entry.LabelKey}");
                editor.OpenSearchResultCommand.Execute(entry);
                await Render();
                var found = false;
                for (DependencyObject? control = Keyboard.FocusedElement as DependencyObject; control != null; control = VisualTreeHelper.GetParent(control))
                {
                    var values = control.GetLocalValueEnumerator();
                    while (values.MoveNext())
                    {
                        if (BindingOperations.GetBindingExpression(control, values.Current.Property)?.ParentBinding.Path?.Path == entry.PropertyPath)
                            found = true;
                    }
                }
                Check(found, $"Search focuses visible setting: {entry.PropertyPath}");
            }
        }
        finally
        {
            editor.Apps.Remove(website);
            editor.SelectedApp = selected;
            editor.ActiveSection = section;
        }
    }
}
