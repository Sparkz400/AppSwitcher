using AppSwitcher.Models; // Wichtig für AppItem
using AppSwitcher.ViewModels; // Wichtig für SettingsViewModel

namespace AppSwitcher;

public partial class SettingsWindow : Window
{
    private readonly HashSet<ValidationError> _inputErrors = [];
    public bool ConfigurationSavedOnClose { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(OnValidationError));
        Closing += OnClosing;
        Closed += OnClosed;
        Loaded += (_, _) => { if (DataContext is SettingsViewModel vm) vm.NavigateToSetting += NavigateToSetting; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SettingsSearchBox.Focus(); SettingsSearchBox.SelectAll(); e.Handled = true; }
        };
    }

    private bool _closing;
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ConfigurationSavedOnClose || DataContext is not SettingsViewModel vm) return;
        if (!vm.IsAdmin) { ConfigurationSavedOnClose = true; return; }
        if (!vm.HasInputErrors && !vm.HasUnsavedChanges && !vm.SaveCommand.IsRunning)
        { ConfigurationSavedOnClose = true; return; }
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            if (vm.SaveCommand.IsRunning) await vm.SaveCommand.ExecutionTask!;
            if (vm.Runtime.CurrentAccessLevel != AccessLevel.None && (vm.HasUnsavedChanges || vm.HasInputErrors))
            {
                var discard = vm.Runtime.ConfirmOperation(Services.LocalizationService.T("UI_DiscardConfirm"));
                // Expiry may close the confirmation while its modal loop runs.
                if (!discard && vm.Runtime.CurrentAccessLevel != AccessLevel.None) return;
            }
            await vm.DiscardChangesAsync();
            ConfigurationSavedOnClose = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception)
        {
            vm.SaveStatus = Services.LocalizationService.T("UI_SaveFailed");
        }
        finally { _closing = false; }
    }

    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added) _inputErrors.Add(e.Error);
        else _inputErrors.Remove(e.Error);
        if (DataContext is SettingsViewModel vm)
        {
            vm.HasInputErrors = _inputErrors.Count > 0;
        }
        if (e.Action == ValidationErrorEventAction.Added && e.OriginalSource is FrameworkElement field)
            field.BringIntoView();
    }

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AppListColumn is null) return;
        var compact = ActualWidth < 1000;
        NavigationColumn.Width = new GridLength(ActualWidth < 800 ? 156 : 184);
        ConfigurationHeading.Visibility = ActualWidth < 800 ? Visibility.Collapsed : Visibility.Visible;
        AppListColumn.Width = new GridLength(compact ? 0 : 190);
        AppListPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactAppHeader.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        AppEditor.EditorHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CompactActions_Click(object sender, RoutedEventArgs e)
    {
        CompactAppControls.ContextMenu.PlacementTarget = CompactAppControls;
        CompactAppControls.ContextMenu.IsOpen = true;
    }

    private void AddAppMenu_Click(object sender, RoutedEventArgs e)
    {
        AddAppMenuButton.ContextMenu.PlacementTarget = AddAppMenuButton;
        AddAppMenuButton.ContextMenu.IsOpen = true;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.NavigateToSetting -= NavigateToSetting;
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }

    private void SettingsSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (e.Key == Key.Escape) { vm.SettingsSearch = ""; e.Handled = true; }
        if (e.Key == Key.Enter)
        {
            SettingsSearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            vm.OpenSearchResultCommand.Execute(vm.SearchResults.FirstOrDefault());
            e.Handled = true;
        }
    }

    private void SearchResult_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && e.AddedItems.OfType<SettingsSearchResult>().FirstOrDefault() is { } result)
            vm.OpenSearchResultCommand.Execute(result);
    }

    private async void NavigateToSetting(SettingsSearchResult result)
    {
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!IsLoaded) return;
        if (result.Section == "Browsers")
        {
            VisualChildren<Expander>(this).FirstOrDefault(expander => expander.IsVisible && expander.IsExpanded)?.BringIntoView();
            return;
        }
        var tabs = VisualChildren<TabControl>(this).FirstOrDefault(control => control.IsVisible);
        if (tabs != null && result.Tab < tabs.Items.Count) tabs.SelectedIndex = result.Tab;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!IsLoaded) return;
        // Collapsed content is in the logical tree even before WPF creates its visuals.
        // Search opens the exact disclosure containing the field, then focuses it.
        var content = (tabs?.SelectedItem as TabItem)?.Content as DependencyObject;
        foreach (var control in LogicalDescendants<Control>(content ?? this).Where(control => control.IsEnabled))
        {
            var property = control switch
            {
                TextBox => TextBox.TextProperty,
                System.Windows.Controls.Primitives.ToggleButton toggle when toggle.GetBindingExpression(ContentControl.ContentProperty) != null => ContentControl.ContentProperty,
                System.Windows.Controls.Primitives.ToggleButton => System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                ComboBox combo when combo.IsEditable && combo.GetBindingExpression(ComboBox.TextProperty) != null => ComboBox.TextProperty,
                ComboBox combo when combo.GetBindingExpression(ComboBox.SelectedValueProperty) != null => ComboBox.SelectedValueProperty,
                ComboBox combo when combo.GetBindingExpression(ComboBox.SelectedIndexProperty) != null => ComboBox.SelectedIndexProperty,
                ComboBox => ComboBox.SelectedItemProperty,
                Slider => Slider.ValueProperty,
                Button => Button.CommandProperty,
                _ => null
            };
            if (property == null || control.GetBindingExpression(property)?.ParentBinding.Path?.Path != result.PropertyPath) continue;
            for (DependencyObject? ancestor = LogicalTreeHelper.GetParent(control); ancestor != null; ancestor = LogicalTreeHelper.GetParent(ancestor))
                if (ancestor is Expander disclosure) disclosure.SetCurrentValue(Expander.IsExpandedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (!IsLoaded || !control.IsVisible) continue;
            control.BringIntoView();
            control.Focus();
            return;
        }
        tabs?.Focus();
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }


    private Point _startPoint;
    private object? _draggedItem;

    private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SettingsViewModel { CanEditApps: true })
            return;

        _startPoint = e.GetPosition(null);
        
        if (sender is ListBoxItem item)
        {
            _draggedItem = item.DataContext; // Wir merken uns das AppItem
        }
    }

    private void ListBoxItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not SettingsViewModel { CanEditApps: true })
            return;

        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
            return;

        Point mousePos = e.GetPosition(null);
        Vector diff = _startPoint - mousePos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var listBoxItem = sender as ListBoxItem;
            if (listBoxItem == null) return;

            DragDrop.DoDragDrop(listBoxItem, _draggedItem, DragDropEffects.Move);
            _draggedItem = null; // Reset nach Drop
        }
    }

    private void ListBoxItem_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not SettingsViewModel { CanEditApps: true })
            return;

        var targetItem = (sender as ListBoxItem)?.DataContext as AppItem;
        
        var droppedData = e.Data.GetData(typeof(AppItem)) as AppItem;

        if (targetItem != null && droppedData != null && targetItem != droppedData)
        {
            if (this.DataContext is SettingsViewModel vm)
            {
                int oldIndex = vm.Apps.IndexOf(droppedData);
                int newIndex = vm.Apps.IndexOf(targetItem);

                if (oldIndex != -1 && newIndex != -1)
                {
                    vm.Apps.Move(oldIndex, newIndex);
                }
            }
        }
    }
}
