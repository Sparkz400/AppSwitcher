using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using AppSwitcher;
using AppSwitcher.Converters;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.Tests;
using AppSwitcher.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class Program
{
    private static readonly string Output = Path.GetFullPath("artifacts/ui-checks");
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--probe-window") return ProcessChecks.RunProbe(args);
        if (args.FirstOrDefault() == "--recovery-host" && args.Length == 2) return RunRecoveryHost(args[1]);
        if (args.Contains("--recovery-process-check")) return CheckRecoveryProcess().GetAwaiter().GetResult();
        if (Environment.GetEnvironmentVariable("APPSWITCHER_UI_TEST_DATA") is { } dataDirectory
            && args.FirstOrDefault() is "--app-host" or "--restart-after") return ProcessChecks.RunAppHost(dataDirectory);
        if (args.Contains("--process-checks")) return ProcessChecks.RunAsync().GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--soak-check" && args.Length == 2 && int.TryParse(args[1], out var minutes) && minutes is >= 1 and <= 1440)
            return ProcessChecks.RunSoakAsync(minutes).GetAwaiter().GetResult();
        if (args.Contains("--startup-memory")) return StartupMemoryChecks.RunAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--benchmark")) { PerformanceChecks.RunAsync().GetAwaiter().GetResult(); return 0; }
        Directory.CreateDirectory(Output);
        AppRendering.Initialize();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var uri in new[] { "Themes/DarkTheme.xaml", "Languages/en.xaml", "/AppSwitcher;component/Themes/Controls.xaml", "Themes/EditorTemplates.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) });
        app.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
        app.Resources["BoolToVisibilityConverter"] = new BooleanToVisibilityConverter();
        app.Resources["BoolToThickness"] = new BoolToThicknessConverter();
        app.Resources["PathToIconConv"] = new PathToIconConverter();
        app.Resources["StringNotEmptyToVis"] = new StringNotEmptyToVisibilityConverter();
        app.Resources["BoolToExpanderIcon"] = new BoolToExpanderIconConverter();
        app.Resources["SectionVisibility"] = new SectionVisibilityConverter();
        app.Resources["AppStateText"] = new AppStateTextConverter();
        app.Resources["CountToVis"] = new CountToVisibilityConverter();
        app.Resources["InvertBool"] = new InvertBoolConverter();
        var trace = new TextWriterTraceListener(Path.Combine(Output, "bindings.log"));
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args.FirstOrDefault() == "--setup-package-check" && args.Length == 2)
                    await CheckBrowserSetupPackage(args[1]);
                else if (args.Contains("--editor-benchmark")) await CheckEditorPerformance();
                else if (args.Contains("--accessibility-check")) await CheckHighContrast();
                else if (args.Contains("--picker-expiry-check"))
                {
                    using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
                    using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
                    await CheckWindowPickerSessionExpiry(fixture, session);
                    await CheckBrowserSetupSessionExpiry(fixture, session);
                }
                else await RunChecks();
                trace.Flush();
                Check(new FileInfo(Path.Combine(Output, "bindings.log")).Length == 0, "No WPF binding warnings or errors");
                Console.WriteLine($"Native UI checks passed: {_checks} assertions.");
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                app.Shutdown(1);
            }
            finally { trace.Flush(); trace.Close(); }
        };
        return app.Run();
    }

    private static async Task RunChecks()
    {
        Check(RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly, "AppSwitcher uses the low-memory rendering path");
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var originalTheme = dictionaries.First(dictionary => dictionary.Source.OriginalString == "Themes/DarkTheme.xaml");
        var originalLanguage = dictionaries.First(dictionary => dictionary.Source.OriginalString == "Languages/en.xaml");
        new ThemeService(NullLogger<ThemeService>.Instance).ApplyTheme("Dark");
        new LocalizationService(NullLogger<LocalizationService>.Instance).ApplyLanguage("en");
        Check(dictionaries.Contains(originalTheme) && dictionaries.Contains(originalLanguage), "Reapplying the same theme/language reuses the loaded resources");
        var configBefore = ConfigHash();
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Settings = new AppSettings { Language = "en", Theme = "Light", Shape = "RoundedSquare", BgColor = "#202429", FgColor = "#FFFFFF", MenuWidth = 390, PositionLocked = true },
            Groups = [new GroupDefinition { Key = "production", Names = new() { ["en"] = "Production", ["de"] = "Produktion" } }],
            Apps = [new AppItem { Name = "Production overview", Path = "ui-check-only.exe", Group = "production", IsPinned = true }, new AppItem { Name = "Quality inspection", Path = "ui-check-quality.exe", Group = "production" }, new AppItem { Name = "Maintenance & diagnostics", Path = "ui-check-maintenance.exe", Group = "production" }]
        });
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Admin;
        vm.Apps[0].State = AppState.Running;
        vm.Apps[0].IsRunning = true;
        vm.Apps[0].CpuPercent = 12.4;
        vm.Apps[0].MemoryMB = 384;
        vm.Apps[1].State = AppState.Unhealthy;
        vm.Apps[1].HealthStatus = "Health check timed out. Review the application connection.";
        var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        var launcher = new MainWindow(session) { DataContext = vm, Left = 24, Top = 24 };
        var launcherContent = (FrameworkElement)launcher.Content;
        launcherContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Check(launcherContent.DesiredSize.Width > 0 && launcherContent.DesiredSize.Height > 0, "Launcher can be measured before positioning");
        var workArea = MonitorHelper.GetMonitorWorkAreas().First();
        vm.Settings.PosX = workArea.Right - 1;
        vm.Settings.PosY = workArea.Bottom - 1;
        typeof(App).GetMethod("PositionWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [launcher, vm.Settings]);
        Check(launcher.Left + launcherContent.DesiredSize.Width <= workArea.Right && launcher.Top + launcherContent.DesiredSize.Height <= workArea.Bottom, "Saved edge positions keep the full launcher on screen");
        launcher.Left = workArea.Left + 24;
        launcher.Top = workArea.Top + 24;
        launcher.Show();
        await Render();
        Check(launcher.ActualWidth == vm.Settings.ButtonSize && launcher.ActualHeight == vm.Settings.ButtonSize,
            "Launcher has only the configured shape's footprint, without a panel or label");
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(launcher) == null, "Launcher has no caption chrome");
        Check(!Descendants<TextBlock>(launcher).Any(text => text.IsVisible), "Closed launcher has no application label");
        Save(launcher, "launcher-button");
        vm.IsMenuOpen = true;
        await Render();
        var surface = (FrameworkElement)launcher.FindName("MenuSurface");
        var firstApp = vm.Apps[0];
        var launchButton = Descendants<Button>(surface).First(button => button.DataContext == firstApp && button.ContextMenu == null);
        firstApp.Mode = DisplayMode.IconOnly; await Render();
        Check(!Descendants<TextBlock>(launchButton).Any(text => text.IsVisible), "Icon-only hides both name and status text");
        Check(AutomationProperties.GetName(launchButton) == firstApp.Name, "Icon-only retains its accessible name");
        firstApp.Mode = DisplayMode.TextOnly; await Render();
        Check(!Descendants<Image>(launchButton).Any(image => image.IsVisible), "Text-only hides the icon");
        Check(Descendants<TextBlock>(launchButton).Any(text => text.IsVisible && text.Text == firstApp.Name), "Text-only retains its visible name");
        firstApp.Mode = DisplayMode.Combined; await Render();
        Check(Descendants<Image>(launchButton).Any(image => image.IsVisible), "Combined restores the icon");
        Save((FrameworkElement)launcher.FindName("MenuSurface"), "launcher-light");
        vm.QuickFilter = "quality";
        await Render();
        Check(vm.GroupedApps.SelectMany(g => g.Apps).Single().Name == "Quality inspection" && vm.PinnedApps.Count == 0,
            "Quick filter narrows the launcher without reordering applications");
        Check(vm.DashboardApps.Count == 3, "Quick filter does not hide monitoring entries");
        Save((FrameworkElement)launcher.FindName("MenuSurface"), "launcher-filtered");
        vm.QuickFilter = "";
        await Render();
        var actionButton = Descendants<Button>((DependencyObject)launcher.FindName("MenuSurface")).First(button => button.ContextMenu != null);
        Invoke(actionButton);
        await Render();
        Check(actionButton.ContextMenu!.IsOpen, "Secondary actions work without a right click");
        Check(actionButton.ContextMenu.DataContext is AppItem, "Secondary actions address the selected application");
        Save(actionButton.ContextMenu, "launcher-actions");
        actionButton.ContextMenu.IsOpen = false;
        vm.IsMenuOpen = false;
        // Flush the bound popup closure before timing the launcher's reopen debounce.
        await Render();
        Check(!((Popup)launcher.FindName("AppMenu")).IsOpen, "Launcher popup is closed before the reopen check");
        await Task.Delay(350);
        Invoke((Button)launcher.FindName("MainGrid")); await Render();
        Check(vm.IsMenuOpen, "Launcher button opens the menu through UI Automation");
        ((FrameworkElement)launcher.FindName("MenuSurface")).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(launcher), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(!vm.IsMenuOpen, "Escape closes the launcher from inside the popup");
        vm.Settings.GlobalShortcutEnabled = true;
        Check(vm.ShortcutStatus.Length == 0 || vm.ShortcutStatus == LocalizationService.T("Shortcut_Unavailable"), "Shortcut registration has an actionable conflict state");
        vm.Settings.GlobalShortcutEnabled = false;
        Check(vm.ShortcutStatus.Length == 0, "Disabling the shortcut unregisters it and clears conflicts");
        using (var realHelper = new ProcessHelper(NullLogger<ProcessHelper>.Instance))
        using (var metrics = new ResourceMonitorService(NullLogger<ResourceMonitorService>.Instance, realHelper, fixture.NotificationService))
        using (var process = Process.GetCurrentProcess())
        {
            var aliasA = new AppItem { Name = "Alias A", Path = process.MainModule!.FileName!, IsRunning = true };
            var aliasB = new AppItem { Name = "Alias B", Path = aliasA.Path, IsRunning = true };
            metrics.CollectMetrics([aliasA, aliasB]);
            await Render();
            metrics.CollectMetrics([aliasA, aliasB]);
            Check(aliasA.MemoryMB > 0 && aliasA.MemoryMB == aliasB.MemoryMB && aliasA.CpuPercent == aliasB.CpuPercent,
                "Entries sharing a process receive the same resource sample");
        }
        vm.Layouts.Add(new ScreenLayout { Name = "Production workspace", AppIds = [vm.Apps[0].AppId], GroupKeys = ["production"] });
        await CheckNativeWindowCatalog();
        foreach (var (language, theme, width, height) in new[] { ("en", "Light", 1180d, 800d), ("de", "Dark", 1024d, 768d), ("de", "Light", 640d, 560d), ("en", "Dark", 900d, 700d) })
        {
            vm.Settings.Language = language;
            vm.Settings.Theme = theme;
            var settings = new SettingsViewModel(vm, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
                new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Admin);
            var window = new SettingsWindow { DataContext = settings, Width = width, Height = height };
            window.Show();
            CheckContrast();
            foreach (var section in new[] { "Setup", "Applications", "Monitoring", "Layouts", "Access", "Audit", "System", "Browsers" })
            {
                settings.ActiveSection = section;
                await Render();
                Save(window, $"workspace-{language}-{theme.ToLowerInvariant()}-{section.ToLowerInvariant()}");
                CheckControls(window);
                if (section == "Setup") await CheckSetupNavigation(window, settings);
                if (section == "Monitoring") await CheckAttentionPresentation(window, vm, language, theme);
                foreach (var tabs in Descendants<TabControl>(window).Where(tabs => tabs.IsVisible).ToList())
                {
                    for (var index = 0; index < tabs.Items.Count; index++)
                    {
                        tabs.SelectedIndex = index;
                        await Render();
                        CheckControls(window);
                        Save(window, $"tabs-{language}-{theme.ToLowerInvariant()}-{section.ToLowerInvariant()}-{index}");
                        // Hidden settings still need layout/touch coverage in every language and size.
                        var disclosures = LogicalDescendants<Expander>((DependencyObject)((TabItem)tabs.SelectedItem).Content)
                            .Select(expander => (expander, expander.IsExpanded)).ToArray();
                        foreach (var (expander, _) in disclosures) expander.SetCurrentValue(Expander.IsExpandedProperty, true);
                        await Render();
                        CheckControls(window);
                        foreach (var header in Descendants<ToggleButton>(window).Where(header => header.IsVisible && header.Name == "HeaderSite"))
                            Check(header.ActualHeight >= 44, "Disclosure headers support touch");
                        if (disclosures.Length > 0)
                            Save(window, $"expanded-{language}-{theme.ToLowerInvariant()}-{section.ToLowerInvariant()}-{index}");
                        foreach (var (expander, expanded) in disclosures) expander.SetCurrentValue(Expander.IsExpandedProperty, expanded);
                        await Render();
                    }
                    tabs.SelectedIndex = 0;
                }
            }
            await CheckDisplaySelections(window, settings, language, theme);
            settings.ActiveSection = "Applications";
            settings.SelectedApp = settings.Apps[0];
            var website = settings.SelectedApp;
            website.TargetKind = AppTargetKind.Website;
            website.WebsiteUrl = "https://192.168.1.78";
            website.BrowserProfileId = Guid.NewGuid().ToString("N");
            await Render();
            Check(Descendants<TextBox>(window).Any(box => box.IsVisible && box.Text == website.WebsiteUrl), "Website URL editor is visible");
            Check(!Descendants<TextBox>(window).Any(box => box.IsVisible && box.Text == website.Path), "Website editor hides native executable controls");
            CheckControls(window);
            Save(window, $"website-{language}-{theme.ToLowerInvariant()}-{(int)width}");
            website.TargetKind = AppTargetKind.Native;
            await Render();
            if (language == "en" && theme == "Light")
            {
                await CheckSettingsCatalog(window, settings);
                await CheckEditorInteraction(window, settings);
                await CheckWindowPickImport(window, settings);
            }
            if (settings.HasUnsavedChanges) await settings.SaveCommand.ExecuteAsync(null);
            Check(!settings.HasUnsavedChanges, "Preview edits explicitly saved before closing");
            window.Close();
            await Render();
            Check(!window.IsVisible, "Editor closes after successful save");
            await CheckWindowPickerPresentation(language, theme, width < 700);
            await CheckBrowserSetupPresentation(language, theme, width < 700);
            await CheckConfigurationRecoveryPresentation(language, theme, width < 700);
            var support = new SupportReportWindow(vm.CreateSupportReport(), () => true);
            support.Show(); await Render(); CheckControls(support);
            Check(((TextBox)support.FindName("ReportText")).IsReadOnly, "Support report preview is read-only");
            Check(!((TextBox)support.FindName("ReportText")).Text.Contains("ui-check-only.exe"), "Support report preview excludes executable paths");
            Save(support, $"support-{language}-{theme.ToLowerInvariant()}");
            support.Close();
        }
        var pin = new PasswordDialog();
        pin.Show(); await Render(); Save(pin, "pin-dark");
        for (var digit = 0; digit < 9; digit++)
            pin.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pin), Environment.TickCount, Key.D1) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(pin.Password.Length == 8 && ((TextBlock)pin.FindName("PinDisplay")).Text == new string('●', 8), "Keyboard PIN is bounded and masked");
        pin.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pin), Environment.TickCount, Key.Back) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(pin.Password.Length == 7, "Keyboard backspace edits the PIN");
        pin.Close();
        var confirmation = new ConfirmationDialog("A long application name used for testing on smaller operator panels");
        confirmation.Show(); await Render(); Save(confirmation, "confirmation-de-dark"); confirmation.Close();
        var toast = new ToastWindow("The application could not be started. Verify its configured path and your access, then try again.", NotificationType.Error);
        toast.Show(); await Render(); Save(toast, "notification-de-dark"); toast.Close();
        await CheckReadOnlyAndEmpty(fixture, session);
        await CheckWindowPickerSessionExpiry(fixture, session);
        await CheckBrowserSetupSessionExpiry(fixture, session);
        await CheckSessionExpiry(fixture, session);
        await CheckPinDialogSessionExpiry();
        await CheckHighContrast();
        await CheckWindowChromeAndScrolling();
        await CheckCustomControls();
        launcher.Close();
        Check(ConfigHash() == configBefore, "Native checks leave the user's persisted configuration untouched");
    }

    private static async Task CheckSessionExpiry(MainViewModelAuditTests.TestHarness fixture, SessionTimeoutService session)
    {
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Admin;
        var app = new AppItem { Name = "Recovery test", Path = "ui-check-recovery.exe", RequiresWatchdogAcknowledgement = true, WatchdogStatusText = "Repeated restart failures" };
        vm.Apps.Add(app);
        using var settings = new SettingsViewModel(vm, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Admin);
        var window = new SettingsWindow { DataContext = settings };
        vm.PrepareWindow(window, privileged: true);
        window.Show(); await Render();
        settings.HasInputErrors = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // Exercise the timeout boundary without waiting for the minute-based idle timer.
            typeof(MainViewModel).GetMethod("OnSessionTimedOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, null);
        };
        timer.Start();
        vm.AcknowledgeWatchdogCommand.Execute(app);
        await Render();
        Check(vm.CurrentAccessLevel == AccessLevel.None && !window.IsVisible, "Session expiry closes editors even with invalid inputs");
        Check(app.RequiresWatchdogAcknowledgement, "Expired confirmation cannot resume recovery");
        foreach (var toast in Application.Current.Windows.OfType<ToastWindow>().ToList()) toast.Close();
    }

    private static async Task CheckEditorInteraction(SettingsWindow window, SettingsViewModel settings)
    {
        await settings.SaveCommand.ExecuteAsync(null);
        await Render();
        var save = (Button)window.FindName("SaveButton");
        Check(!save.IsVisible && !settings.HasUnsavedChanges, "Save action disappears after successful save");
        var selected = settings.SelectedApp!;
        var profileId = selected.BrowserProfileId;
        settings.RefreshBrowserProfilesCommand.Execute(null);
        Check(settings.SelectedBrowserProfile?.ProfileId == profileId && selected.BrowserProfileId == profileId
            && !settings.HasUnsavedChanges, "Refreshing an offline profile preserves and displays its saved identity without editing configuration");
        var originalLanguage = settings.Settings.Language;
        var beforeLanguage = System.Text.Json.JsonSerializer.SerializeToNode(new ConfigRoot { Settings = settings.Settings, Apps = settings.Apps.ToList(), Groups = settings.Groups.ToList(), Layouts = settings.Layouts.ToList() });
        settings.Settings.Language = "de";
        Check(settings.SelectedBrowserProfile?.Name == LocalizationService.T("Browser_SavedOffline"),
            "Offline profile labels follow a live language change");
        settings.Settings.Language = originalLanguage;
        var afterLanguage = System.Text.Json.JsonSerializer.SerializeToNode(new ConfigRoot { Settings = settings.Settings, Apps = settings.Apps.ToList(), Groups = settings.Groups.ToList(), Layouts = settings.Layouts.ToList() });
        Check(System.Text.Json.Nodes.JsonNode.DeepEquals(beforeLanguage, afterLanguage),
            "Changing language and back preserves every configuration value, including monitor selections");
        Check(!settings.SchedulesExpanded && !settings.StartupOptionsExpanded && !settings.HealthChecksExpanded,
            "Unused advanced sections start collapsed");
        selected.ScheduleTime = "08:00";
        selected.HealthCheckUrl = "https://localhost/health";
        settings.SelectedApp = settings.Apps[1];
        settings.SelectedApp = selected;
        Check(settings.SchedulesExpanded && settings.HealthChecksExpanded, "Configured schedules and health checks open when selecting the app");
        selected.ScheduleTime = "";
        selected.HealthCheckUrl = "";
        settings.SelectedApp = settings.Apps[1];
        settings.SelectedApp = selected;
        Check(!settings.SchedulesExpanded && !settings.HealthChecksExpanded && !settings.HasUnsavedChanges,
            "Selecting another app does not leak disclosure state or edit settings");
        settings.ActiveSection = "System";
        var systemTabs = Descendants<TabControl>(window).Single(tab => tab.IsVisible);
        systemTabs.SelectedIndex = 0;
        await Render();
        var preview = Descendants<AppSwitcher.Controls.LauncherVisual>(window).Single(control => control.IsVisible);
        var originalSize = settings.Settings.ButtonSize;
        settings.Settings.ButtonSize = 120;
        await Render();
        Check(ReferenceEquals(preview.DataContext, settings.Settings) && ((FrameworkElement)preview.Content).ActualWidth == 120,
            "Live preview immediately reflects the same launcher settings");
        Check(!preview.IsHitTestVisible, "Preview cannot launch apps or move the launcher");
        settings.Settings.ButtonSize = originalSize;
        settings.ActiveSection = "Applications";
        await Render();
        var originalWidth = settings.Settings.MenuWidth;
        settings.Settings.MenuWidth = originalWidth + 1;
        await Render();
        Check(save.IsVisible && save.IsEnabled, "Save action appears for pending edits");
        settings.Settings.MenuWidth = originalWidth;
        await Render();
        Check(!save.IsVisible, "Reverting edits hides Save again");
        settings.Settings.MenuWidth = originalWidth + 10;
        await Render();
        Invoke((Button)window.FindName("UndoButton")); await Render();
        Check(settings.Settings.MenuWidth == originalWidth && !save.IsVisible, "Undo restores the last edit and hides Save");
        settings.SettingsSearch = "theme"; await Render();
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.First()); await Render();
        Check(settings.ActiveSection == "System" && Descendants<TabControl>(window).Single(tab => tab.IsVisible).SelectedIndex == 1,
            "Settings search opens the matching section and tab");
        settings.SettingsSearch = "restart appswitcher";
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.Single()); await Render();
        Check(Keyboard.FocusedElement is Button restart && ReferenceEquals(restart.Command, settings.RestartLauncherCommand),
            "Search reveals the collapsed restart action without executing it");
        settings.SettingsSearch = "schedule daily";
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.Single()); await Render();
        Check(settings.SchedulesExpanded && Keyboard.FocusedElement is TextBox schedule
            && schedule.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "SelectedApp.ScheduleTime",
            "Search reveals and focuses a collapsed daily schedule");
        settings.SettingsSearch = "dependency order";
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.Single()); await Render();
        Check(settings.StartupOptionsExpanded && Keyboard.FocusedElement is TextBox dependency
            && Descendants<ComboBox>(window).Any(combo => combo.IsKeyboardFocusWithin && combo.GetBindingExpression(ComboBox.TextProperty)?.ParentBinding.Path.Path == "SelectedApp.DependsOn"),
            "Search reveals the editable dependency dropdown");
        settings.SettingsSearch = "health http"; await Render();
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.Single()); await Render();
        Check(Keyboard.FocusedElement is TextBox healthBox && healthBox.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "SelectedApp.HealthCheckUrl",
            "Health search selects and focuses the health URL field");
        Check(settings.HealthChecksExpanded, "Search expands the previously hidden health settings");
        var memory = Descendants<TextBox>(window).Single(box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "SelectedApp.MemoryThresholdMB");
        memory.Text = "invalid";
        memory.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        settings.HealthChecksExpanded = false;
        await Render();
        Check(settings.HasInputErrors && !settings.CanSave, "Collapsing a section cannot bypass input validation");
        settings.HealthChecksExpanded = true;
        memory.Text = "0";
        memory.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(!settings.HasInputErrors, "Reopening and correcting a hidden input clears its error");
        settings.SettingsSearch = "bildschirm"; await Render();
        settings.OpenSearchResultCommand.Execute(settings.SearchResults.Single()); await Render();
        Check(Keyboard.FocusedElement is ComboBox monitor && monitor.GetBindingExpression(ComboBox.SelectedValueProperty)?.ParentBinding.Path.Path == "AppMonitorSelection",
            "Monitor search focuses the matching dropdown");
        settings.ActiveSection = "Browsers";
        await settings.RefreshBrowserConnectionsCommand.ExecutionTask!;
        await Render();
        Check(settings.BrowserConnections.Count >= 3, "Browser overview includes Chrome, Edge and Firefox");
        var fault = new BrowserConnectionRow { HostAvailable = true, WebsiteCount = 1, AppId = "test", StateCode = "IdentityConflict", Status = "Resolve the duplicate identity before switching." };
        Check(fault.Summary == fault.Status, "Browser conflicts keep their concrete recovery message visible");
        fault.StateCode = "Ready";
        Check(fault.Summary == LocalizationService.T("Browser_SummaryReady"), "Healthy browser state uses a short label");
        settings.ActiveSection = "Applications";
        var tabs = Descendants<TabControl>(window).First(t => t.IsVisible);
        tabs.SelectedIndex = 4;
        await Render();
        var picker = Descendants<ToggleButton>(window).First(t => t.IsVisible && t.Content is string value && value.StartsWith('#'));
        picker.IsChecked = true;
        await Render();
        var popup = Descendants<Popup>(picker).Single();
        var swatch = Descendants<Button>(popup.Child).First();
        var launcherColour = settings.Settings.BgColor;
        Invoke(swatch); await Render();
        Check(settings.SelectedApp!.BgColor == (string)swatch.DataContext, "Colour picker updates the selected application");
        Check(settings.Settings.BgColor == launcherColour, "Application colour picker preserves launcher colour");
        Save((FrameworkElement)popup.Child, "colour-picker");
        picker.IsChecked = false;
        tabs.SelectedIndex = 2;
        settings.SelectedApp.WatchdogEnabled = true;
        await Render();
        var numeric = Descendants<TextBox>(window).First(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "SelectedApp.MaxRestarts");
        numeric.Text = "invalid";
        numeric.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        await Render();
        Check(settings.HasInputErrors && !settings.SaveCommand.CanExecute(null), "Invalid numeric input disables saving");
        Save(window, "validation-error");
        var cancelClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        cancelClose.Tick += (_, _) => { cancelClose.Stop(); Application.Current.Windows.OfType<ConfirmationDialog>().Single(dialog => dialog.IsVisible).Close(); };
        cancelClose.Start();
        Invoke(Descendants<AppSwitcher.Controls.WindowTitleBar>(window).Single().CloseButton);
        await Task.Delay(250); await Render();
        Check(window.IsVisible, "Cancelling discard preserves the editor and invalid input");
        numeric.Text = "3";
        numeric.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(!settings.HasInputErrors, "Correcting input clears the validation error");
        selected = settings.SelectedApp;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); Application.Current.Windows.OfType<ConfirmationDialog>().Single().Close(); };
        timer.Start();
        settings.DeleteAppCommand.Execute(null);
        Check(settings.Apps.Contains(selected), "Cancelling removal preserves the application");
        var button = Descendants<Button>(window).First(b => b.IsVisible && b.IsEnabled);
        button.Focus();
        Check(button.IsKeyboardFocused, "Buttons accept keyboard focus");
        button.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(!button.IsKeyboardFocused && Keyboard.FocusedElement != null, "Tab navigation advances focus");
    }

    private static async Task CheckCustomControls()
    {
        var checkBox = new CheckBox { Content = "Custom checkbox", IsThreeState = true };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 40, SmallChange = 5, Width = 300 };
        var menuButton = new Button { Content = "Custom menu" };
        var selected = new MenuItem { Header = "Checked item", IsCheckable = true };
        var submenu = new MenuItem { Header = "Submenu" };
        submenu.Items.Add(selected);
        menuButton.ContextMenu = new ContextMenu { Items = { submenu } };
        var window = new Window { Width = 400, Height = 260, Content = new StackPanel { Children = { checkBox, slider, menuButton } } };
        window.SetResourceReference(Window.BackgroundProperty, "PanelBg");
        window.Show(); await Render();
        var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkBox).GetPattern(PatternInterface.Toggle);
        toggle.Toggle(); await Render();
        Check(checkBox.IsChecked == true, "Custom checkbox toggles through its accessible action");
        toggle.Toggle(); await Render();
        Check(checkBox.IsChecked == null, "Custom checkbox supports indeterminate state");
        toggle.Toggle(); Check(checkBox.IsChecked == false, "Custom checkbox returns to unchecked");
        Slider.IncreaseSmall.Execute(null, slider);
        Check(slider.Value == 45, "Custom slider keeps keyboard increment command");
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(20, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(20, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Check(slider.Value > 45, "Custom slider thumb drag changes its value");
        menuButton.ContextMenu.PlacementTarget = menuButton;
        menuButton.ContextMenu.IsOpen = true; await Render();
        var expand = (IExpandCollapseProvider)new MenuItemAutomationPeer(submenu).GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand(); await Render();
        Check(submenu.IsSubmenuOpen && selected.IsVisible, "Custom context menu expands its submenu");
        ((IInvokeProvider)new MenuItemAutomationPeer(selected).GetPattern(PatternInterface.Invoke)).Invoke(); await Render();
        Check(selected.IsChecked, "Custom context menu preserves checkable item behavior");
        menuButton.ContextMenu.IsOpen = false;
        Save(window, "custom-controls");
        window.Close();
    }

    private static async Task CheckWindowChromeAndScrolling()
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 1400, Height = 1800, Background = Brushes.SlateGray }
        };
        var window = new Window { Title = "Chrome and scroll regression", Width = 640, Height = 480,
            Style = (Style)Application.Current.FindResource("AppWindowStyle"), Content = scroll };
        window.Show(); await Render();
        var caption = Descendants<AppSwitcher.Controls.WindowTitleBar>(window).Single();
        Check(System.Windows.Shell.WindowChrome.GetWindowChrome(window) is { CaptionHeight: 44, GlassFrameThickness.Left: 0 },
            "Themed caption retains the native drag and resize regions without a glass frame");
        Invoke(caption.MaximizeButton); await Render();
        Check(window.WindowState == WindowState.Maximized, "Custom maximize button maximizes the window");
        var area = MonitorHelper.GetMonitorWorkAreas().First(a => a.Contains(window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2))));
        var contentTopLeft = scroll.PointToScreen(new Point());
        var contentBottomRight = scroll.PointToScreen(new Point(scroll.ActualWidth, scroll.ActualHeight));
        Check(contentTopLeft.X >= area.Left - 1 && contentBottomRight.X <= area.Right + 1 && contentBottomRight.Y <= area.Bottom + 1,
            $"Maximized content stays inside the monitor work area ({contentTopLeft} to {contentBottomRight}, work area {area})");
        Invoke(caption.MaximizeButton); await Render();
        Check(window.WindowState == WindowState.Normal, "Custom restore button restores the window");
        Invoke(caption.MinimizeButton); await Render();
        Check(window.WindowState == WindowState.Minimized, "Custom minimize button minimizes the window");
        window.WindowState = WindowState.Normal; await Render();
        foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
        {
            var bar = Descendants<ScrollBar>(scroll).Single(bar => bar.Orientation == orientation);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            Check(bar.IsVisible && track.Thumb.Template.FindName("ThumbSurface", track.Thumb) != null,
                $"{orientation} scrollbar uses the themed thumb");
            var previous = orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
            ((RoutedCommand)track.IncreaseRepeatButton.Command).Execute(null, track.IncreaseRepeatButton);
            await Render();
            var paged = orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
            Check(paged > previous, $"{orientation} track click advances a page");
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(30, 30) { RoutedEvent = Thumb.DragDeltaEvent });
            await Render();
            Check((orientation == Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset) > paged,
                $"{orientation} thumb drag scrolls content");
        }
        scroll.ScrollToTop(); await Render();
        scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
        await Render();
        Check(scroll.VerticalOffset > 0, "Mouse wheel scrolls with the custom scrollbar");
        scroll.Content = new Border { Width = 10, Height = 10 };
        await Render();
        Check(Descendants<ScrollBar>(scroll).All(bar => !bar.IsVisible), "Auto scrollbars disappear when content fits");
        window.ResizeMode = ResizeMode.NoResize; await Render();
        Check(!caption.MinimizeButton.IsVisible && !caption.MaximizeButton.IsVisible, "Fixed-size dialogs show only the close control");
        Invoke(caption.CloseButton); await Render();
        Check(!window.IsVisible, "Custom close button closes the window");
    }

    private static async Task CheckReadOnlyAndEmpty(MainViewModelAuditTests.TestHarness fixture, SessionTimeoutService session)
    {
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Operator;
        using var settings = new SettingsViewModel(vm, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Operator);
        var window = new SettingsWindow { DataContext = settings, Width = 720, Height = 640 };
        window.Show(); await Render();
        Check(Descendants<TextBox>(window).Where(t => t.IsVisible && t.Name != "SettingsSearchBox").All(t => !t.IsEnabled), "Operator application fields are read-only");
        Check(((TextBox)window.FindName("SettingsSearchBox")).IsEnabled, "Operators can search settings without editing them");
        Check(Descendants<ListBoxItem>(window).Where(i => i.Tag as string is "Setup" or "Access" or "Audit" or "Browsers").All(i => !i.IsVisible), "Administrator navigation stays hidden from operators");
        Save(window, "operator-read-only");
        Save(window, "operator-scaled-150", 1.5);
        vm.Apps.Clear(); await Render(); Save(window, "empty-applications");
        settings.ActiveSection = "Monitoring"; await Render(); Save(window, "empty-monitoring");
        settings.ActiveSection = "Layouts"; await Render(); Save(window, "empty-layouts");
        window.Close(); await Render();
    }

    private static void CheckControls(Window window)
    {
        foreach (var control in Descendants<Control>(window).Where(c => c.IsVisible && c is Button or TextBox or ComboBox or CheckBox))
        {
            var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
            Check(bounds.Left >= -1 && bounds.Right <= window.ActualWidth + 1, $"Control fits horizontally: {control.GetType().Name} {control.Name}");
            if (control is Button or CheckBox)
                Check(control.ActualHeight >= 43.5 && control.ActualWidth >= 43.5, $"Touch target >= 44px: {control.GetType().Name} {control.Name}");
            if (control.TemplatedParent == null)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                Check(peer != null && !string.IsNullOrWhiteSpace(peer.GetName()), $"Accessible control name: {control.GetType().Name} {control.Name}");
                var pattern = control switch { Button => PatternInterface.Invoke, CheckBox => PatternInterface.Toggle, TextBox => PatternInterface.Value, _ => PatternInterface.Selection };
                Check(peer!.GetPattern(pattern) != null, $"Native accessibility pattern: {control.GetType().Name} {control.Name}");
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static string ConfigHash() => File.Exists(AppPaths.ConfigFilePath)
        ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(AppPaths.ConfigFilePath))) : "absent";

    private static void CheckContrast()
    {
        static double Luminance(Color color)
        {
            static double Linear(byte c) { var value = c / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        foreach (var (fg, bg, minimum) in new[] { ("TextPrimary", "PanelBg", 4.5), ("TextSecondary", "WindowBg", 4.5), ("TextMuted", "PanelBg", 4.5), ("DangerFg", "DangerBg", 4.5), ("WarningFg", "PanelBg", 4.5), ("SuccessFg", "PanelBg", 4.5), ("PrimaryFg", "PrimaryBg", 4.5), ("InputBorder", "InputBg", 3d) })
        {
            var a = Luminance(((SolidColorBrush)Application.Current.FindResource(fg)).Color);
            var b = Luminance(((SolidColorBrush)Application.Current.FindResource(bg)).Color);
            Check((Math.Max(a,b) + .05) / (Math.Min(a,b) + .05) >= minimum, $"Contrast: {fg}/{bg}");
        }
    }

    private static async Task Render()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(60);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    private static void Save(FrameworkElement element, string name, double scale = 1)
    {
        element.UpdateLayout();
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) throw new InvalidOperationException($"Empty layout: {name}");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Output, name + ".png"));
        encoder.Save(output);
        Console.WriteLine($"Rendered {name}: {element.ActualWidth:0} × {element.ActualHeight:0}");
    }
}
