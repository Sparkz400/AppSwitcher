namespace AppSwitcher.Tests;

public class KioskForegroundPolicyTests
{
    private readonly KioskForegroundPolicy _policy = new();

    [Fact]
    public void Evaluate_Allows_AppSwitcher_Owned_Window()
    {
        var snapshot = CreateSnapshot("AppSwitcher", "HwndWrapper[AppSwitcher;;]", "AppSwitcher");

        var decision = _policy.Evaluate(snapshot, isOwnedWindow: true, isConfiguredAppWindow: false);

        Assert.Equal(KioskForegroundDisposition.Allow, decision.Disposition);
        Assert.Equal("AppSwitcher", decision.SurfaceName);
    }

    [Fact]
    public void Evaluate_Allows_Configured_App_Window()
    {
        var snapshot = CreateSnapshot("Editor", "Chrome_WidgetWin_1", "editor");

        var decision = _policy.Evaluate(snapshot, isOwnedWindow: false, isConfiguredAppWindow: true);

        Assert.Equal(KioskForegroundDisposition.Allow, decision.Disposition);
        Assert.Equal("Configured app", decision.SurfaceName);
    }

    [Fact]
    public void Evaluate_Blocks_Taskbar_Surface()
    {
        var snapshot = CreateSnapshot(string.Empty, "Shell_TrayWnd", "explorer");

        var decision = _policy.Evaluate(snapshot, isOwnedWindow: false, isConfiguredAppWindow: false);

        Assert.Equal(KioskForegroundDisposition.Block, decision.Disposition);
        Assert.Equal("Taskbar", decision.SurfaceName);
    }

    [Fact]
    public void Evaluate_Blocks_Start_Menu_Surface()
    {
        var snapshot = CreateSnapshot("Start", "Windows.UI.Core.CoreWindow", "StartMenuExperienceHost");

        var decision = _policy.Evaluate(snapshot, isOwnedWindow: false, isConfiguredAppWindow: false);

        Assert.Equal(KioskForegroundDisposition.Block, decision.Disposition);
        Assert.Equal("Shell surface", decision.SurfaceName);
    }

    [Fact]
    public void Evaluate_Ignores_Unknown_Third_Party_Window()
    {
        var snapshot = CreateSnapshot("Google Chrome", "Chrome_WidgetWin_1", "chrome");

        var decision = _policy.Evaluate(snapshot, isOwnedWindow: false, isConfiguredAppWindow: false);

        Assert.Equal(KioskForegroundDisposition.Ignore, decision.Disposition);
    }

    private static KioskWindowSnapshot CreateSnapshot(string title, string className, string processName) =>
        new((IntPtr)123, className, title, processName, $@"C:\Apps\{processName}.exe");
}
