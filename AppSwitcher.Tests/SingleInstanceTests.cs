namespace AppSwitcher.Tests;

public class SingleInstanceTests
{
    [Fact]
    public async Task SecondaryActivationIsDeliveredWithoutStartingAnotherInstance()
    {
        var name = "AppSwitcher.Tests." + Guid.NewGuid().ToString("N");
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activated = new ManualResetEventSlim();
        using var clientFinished = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            try
            {
                using var primary = new SingleInstanceCoordinator(name);
                Assert.True(primary.IsPrimary);
                primary.Listen(activated.Set);
                listening.SetResult();
                Assert.True(activated.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(clientFinished.Wait(TimeSpan.FromSeconds(10)));
                completed.SetResult();
            }
            catch (Exception ex) { listening.TrySetException(ex); completed.TrySetException(ex); }
        });
        owner.Start();
        await listening.Task;
        using var secondary = new SingleInstanceCoordinator(name);
        Assert.False(secondary.IsPrimary);
        var result = await secondary.ActivateExistingAsync();
        clientFinished.Set();
        Assert.True(result);
        await completed.Task;
    }

    [Fact]
    public async Task RestartShutdownPreservesManagedApplications()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        { Apps = [new() { Name = "Visualization", Path = "visual.exe", AutoLaunch = true, WatchdogEnabled = true }] });
        await fixture.ViewModel.ShutdownAsync(stopManagedApps: false);
        // The normal managed-app shutdown disables recovery before stopping apps.
        Assert.True(fixture.ViewModel.Apps[0].WatchdogEnabled);
    }
}
