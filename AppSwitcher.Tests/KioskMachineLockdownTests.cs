namespace AppSwitcher.Tests;

public class KioskMachineLockdownTests
{
    [Theory]
    [InlineData("State : Enabled", 1)]
    [InlineData("State : Enable Pending", 3)]
    [InlineData("State : Disabled", 2)]
    [InlineData("State : Disabled with Payload Removed", 2)]
    public void ParseFeatureState_Recognizes_Dism_Output(string stateLine, int expectedValue)
    {
        var output = $"""

            Deployment Image Servicing and Management tool

            {stateLine}

            """;

        var result = KioskMachineLockdownManager.ParseFeatureState(output);

        Assert.Equal((KeyboardFilterFeatureState)expectedValue, result);
    }

    [Fact]
    public void ResultSerialization_RoundTrips_KeyboardFilter_Status()
    {
        var result = new KioskMachineLockdownResult
        {
            State = KioskMachineLockdownState.RestartRequired,
            Supported = true,
            Configured = false,
            RestartRequired = true,
            Message = "restart",
            TechnicalDetails = "details"
        };

        var json = KioskMachineLockdownSerialization.SerializeResult(result);
        var restored = KioskMachineLockdownSerialization.DeserializeResult(json);

        Assert.NotNull(restored);
        Assert.Equal(result.State, restored!.State);
        Assert.Equal(result.Supported, restored.Supported);
        Assert.Equal(result.Configured, restored.Configured);
        Assert.Equal(result.RestartRequired, restored.RestartRequired);
        Assert.Equal(result.Message, restored.Message);
        Assert.Equal(result.TechnicalDetails, restored.TechnicalDetails);
    }

    [Fact]
    public void Links_And_User_Writable_Folders_Are_Not_Trusted_For_Machine_State()
    {
        var root = Path.Combine(Path.GetTempPath(), $"appswitcher-link-test-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Symbolic links need Developer Mode or admin rights; fall back to a junction, which any user can create.
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                { CreateNoWindow = true, UseShellExecute = false })!;
            mklink.WaitForExit();
        }

        try
        {
            Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.False(KioskMachineLockdownManager.IsTrustedDirectory(new DirectoryInfo(link)));
            // Created by this (standard) user, with an inherited ACL: anything inside it could have been planted.
            Assert.False(KioskMachineLockdownManager.IsTrustedDirectory(new DirectoryInfo(target)));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MachineState_Saves_Atomically_And_Preserves_Corrupt_File()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"appswitcher-kiosk-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "kiosk-machine-state.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path + ".tmp", "planted"); // A predictable name must never be followed or truncated.
            KioskMachineLockdownManager.SaveMachineState(path, ["Win+L"]);
            var saved = KioskMachineLockdownManager.LoadMachineState(path, out var note);
            Assert.Equal(["Win+L"], saved.OwnedPredefinedKeys);
            Assert.False(saved.Unreadable);
            Assert.Empty(note);
            Assert.Equal("planted", File.ReadAllText(path + ".tmp"));
            Assert.Single(Directory.GetFiles(directory, "*.tmp"));

            File.WriteAllText(path, "{\"OwnedPredefinedKeys\": [\"Win");
            var recovered = KioskMachineLockdownManager.LoadMachineState(path, out note);

            // Unknown ownership fails closed: nothing is claimed, so nothing would be removed.
            Assert.True(recovered.Unreadable);
            Assert.Empty(recovered.OwnedPredefinedKeys);
            Assert.NotEmpty(note);
            Assert.True(File.Exists(path));
            Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MachineCommandDetection_Recognizes_Helper_Args()
    {
        var args = new[] { "--kiosk-machine", "apply", "--result-file", @"C:\temp\result.json" };

        var isMachineCommand = KioskMachineLockdownCommand.IsMachineCommand(args);

        Assert.True(isMachineCommand);
    }
}
