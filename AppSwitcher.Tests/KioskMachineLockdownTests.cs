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
    public void MachineCommandDetection_Recognizes_Helper_Args()
    {
        var args = new[] { "--kiosk-machine", "apply", "--result-file", @"C:\temp\result.json" };

        var isMachineCommand = KioskMachineLockdownCommand.IsMachineCommand(args);

        Assert.True(isMachineCommand);
    }
}
