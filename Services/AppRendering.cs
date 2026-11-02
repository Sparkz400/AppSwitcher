using System.Windows.Interop;

namespace AppSwitcher.Services;

internal static class AppRendering
{
    internal const string HardwareRenderingSwitch = "AppSwitcher.UseHardwareRendering";

    internal static void Initialize()
    {
        // This launcher and its settings are small, mostly static 2D surfaces.
        // Avoid a hardware rendering device and its driver workers/buffers per app.
        // The switch retains an opt-in hardware path for deployment-specific needs.
        RenderOptions.ProcessRenderMode = AppContext.TryGetSwitch(HardwareRenderingSwitch, out var enabled) && enabled
            ? RenderMode.Default : RenderMode.SoftwareOnly;
    }
}
