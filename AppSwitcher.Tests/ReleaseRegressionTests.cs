using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;

namespace AppSwitcher.Tests;

public class ReleaseRegressionTests
{
    [Fact]
    public async Task CancellingHookStopsOwnedScriptAndChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppSwitcher-hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "hook.ps1");
        var ids = Path.Combine(root, "ids.txt");
        // All paths and PIDs belong to this fixture. No user process is selected by name.
        await File.WriteAllTextAsync(script, "param([string]$OutputFile)\n$child = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 60' -WindowStyle Hidden -PassThru\n\"$PID,$($child.Id)\" | Set-Content -LiteralPath ($OutputFile + '.tmp')\nMove-Item -LiteralPath ($OutputFile + '.tmp') -Destination $OutputFile\nStart-Sleep -Seconds 60\n");
        var owned = new List<System.Diagnostics.Process>();
        using var cancellation = new CancellationTokenSource();
        var task = new ProcessHookRunner().RunAsync(new AppItem { Name = "Synthetic", WorkingDirectory = Path.GetTempPath(), PreLaunchHookPath = script, PreLaunchHookArgs = $"-OutputFile \"{ids}\"" }, AppLifecycleHookStage.PreLaunch, cancellation.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(ids) && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.True(File.Exists(ids), "Owned script did not start");
            foreach (var id in (await File.ReadAllTextAsync(ids)).Trim().Split(',')) owned.Add(System.Diagnostics.Process.GetProcessById(int.Parse(id)));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            foreach (var process in owned)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(process.HasExited);
            }
        }
        finally
        {
            cancellation.Cancel();
            try { await task; } catch (OperationCanceledException) { }
            foreach (var process in owned)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
                finally { process.Dispose(); }
            }
            // Delete only the two known fixture files, then the empty owned directory.
            File.Delete(script); File.Delete(ids); File.Delete(ids + ".tmp");
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root); break; }
                catch (IOException) when (attempt < 10) { await Task.Delay(100); }
            }
        }
    }
    [Fact]
    public void TimeoutOffOnAndShorterLongerIntervalsUseCurrentSetting()
    {
        var clock = new Clock();
        using var timer = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance, clock);
        timer.Start(5, () => { });
        clock.Advance(4);
        Assert.False(timer.IsExpired());
        clock.Advance(1);
        Assert.True(timer.IsExpired());
        timer.UpdateTimeout(0, () => { });
        Assert.False(timer.IsActive);
        Assert.Equal(0, timer.ConfiguredMinutes);
        timer.UpdateTimeout(5, () => { });
        Assert.True(timer.IsActive);
        Assert.False(timer.IsExpired());
        timer.UpdateTimeout(1, () => { });
        clock.Advance(1);
        Assert.True(timer.IsExpired());
        timer.UpdateTimeout(10, () => { });
        clock.Advance(9);
        Assert.False(timer.IsExpired());
        timer.ResetTimer();
        clock.Advance(2);
        Assert.False(timer.IsExpired());
    }

    [Fact]
    public void RemovingLastAllowedAppOrGroupNeverBroadensScope()
    {
        var scope = new OperatorScopeSettings();
        Assert.False(scope.HasRestrictions);
        scope.SetAllowedAppNames(["A"]);
        scope.RenameAppName("A", "");
        Assert.Equal(["A"], scope.AllowedAppNames);
        scope.RemoveAppName("A");
        Assert.True(scope.HasRestrictions);
        scope.SetAllowedGroupKeys(["G"]);
        scope.RemoveGroupKey("G");
        Assert.True(scope.HasRestrictions);
        var restored = System.Text.Json.JsonSerializer.Deserialize<OperatorScopeSettings>(System.Text.Json.JsonSerializer.Serialize(scope))!;
        Assert.True(restored.HasRestrictions);
    }

    [Fact]
    public async Task RenamesRepairReferencesAndDiscardRestoresWithoutSaving()
    {
        var original = new AppItem { Name = "A", Path = "a.exe" };
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Apps = [original, new() { Name = "B", Path = "b.exe", DependsOn = "A" }],
            Settings = new() { OperatorScope = new() { AllowedAppNames = ["A"] } },
            Layouts = [new() { Name = "Layout", AppIds = [original.AppId], Positions = [new() { AppName = "A", Width = 800, Height = 600 }] }]
        });
        using var editor = new SettingsViewModel(fixture.ViewModel, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance));
        editor.Apps[0].Name = "";
        editor.Apps[0].Name = "B";
        Assert.Equal(["A"], editor.Settings.OperatorScope.AllowedAppNames);
        editor.Apps[0].Name = "Renamed";
        Assert.Equal("Renamed", editor.Apps[1].DependsOn);
        Assert.Equal(["Renamed"], editor.Settings.OperatorScope.AllowedAppNames);
        Assert.Equal("Renamed", editor.Layouts[0].Positions[0].AppName);
        fixture.ViewModel.SaveConfigInBackground();
        Assert.Null(fixture.ConfigService.LastSavedConfig);
        await editor.DiscardChangesAsync();
        Assert.Equal("A", editor.Apps[0].Name);
        Assert.Equal("A", editor.Apps[1].DependsOn);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task ReloadAndUndoReconcileActualTimeoutAndScopeObserver()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Settings = new() { SessionTimeoutMinutes = 5 } });
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Admin;
        var timer = (SessionTimeoutService)typeof(MainViewModel).GetField("_sessionTimeoutService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        var original = vm.EditorSnapshot;
        await vm.ApplyConfigAsync(new ConfigRoot { Settings = new() { SessionTimeoutMinutes = 1 } });
        Assert.Equal(1, timer.ConfiguredMinutes);
        await vm.RestoreEditorSnapshotAsync(original);
        Assert.Equal(5, timer.ConfiguredMinutes);
        Assert.True(timer.IsActive);
        await vm.ApplyConfigAsync(new ConfigRoot { Settings = new() { SessionTimeoutMinutes = 0 } });
        Assert.False(timer.IsActive);
        await vm.RestoreEditorSnapshotAsync(original);
        Assert.True(timer.IsActive);
    }

    [Fact]
    public async Task OldHealthResponseCannotOverwriteChangedOrDisabledCheck()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new HealthCheckService(NullLogger<HealthCheckService>.Instance,
            new HttpClient(new Handler(response.Task)), action => action());
        var app = new AppItem { HealthCheckUrl = "https://old.invalid/", IsRunning = true, State = AppState.Unhealthy, IsResponding = false };
        var check = service.CheckAppHealth(app, CancellationToken.None);
        app.HealthCheckUrl = "";
        Assert.Equal(AppState.Running, app.State);
        Assert.True(app.NeedsAttention); // Responsiveness is independent of HTTP health.
        app.IsResponding = true;
        Assert.False(app.NeedsAttention);
        response.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await check;
        Assert.Equal("", app.HealthStatus);
        Assert.Equal(AppState.Running, app.State);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("opacity")]
    [InlineData("shape")]
    [InlineData("icon")]
    [InlineData("theme")]
    [InlineData("language")]
    [InlineData("color")]
    [InlineData("geometry")]
    [InlineData("future")]
    public void RejectsMalformedPersistedSchema(string field)
    {
        var config = new ConfigRoot();
        switch (field)
        {
            case "size": config.Settings.ButtonSize = -1; break;
            case "opacity": config.Settings.ButtonOpacity = 2; break;
            case "shape": config.Settings.Shape = "unknown"; break;
            case "icon": config.Settings.IconStyle = "unknown"; break;
            case "theme": config.Settings.Theme = "unknown"; break;
            case "language": config.Settings.Language = "unknown"; break;
            case "color": config.Settings.BgColor = "unknown"; break;
            case "geometry": config.Layouts.Add(new() { Positions = [new() { Width = -1, Height = 10 }] }); break;
            case "future": config.Version = 999; break;
        }
        Assert.True(new ConfigValidationService(NullLogger<ConfigValidationService>.Instance).Validate(config).HasErrors);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(int minutes) => _ticks += TimeSpan.FromMinutes(minutes).Ticks;
    }
    private sealed class Handler(Task<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response;
    }
}
