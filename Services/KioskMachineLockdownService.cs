using System.ComponentModel;
using System.Management;
using Microsoft.Win32;

namespace AppSwitcher.Services;

internal enum KioskMachineLockdownOperation
{
    Apply,
    Remove,
    Status
}

internal enum KioskMachineLockdownState
{
    Unknown,
    Applied,
    AlreadyApplied,
    Removed,
    AlreadyRemoved,
    NotConfigured,
    ExternallyManaged,
    Unsupported,
    RestartRequired,
    Cancelled,
    Failed
}

internal sealed class KioskMachineLockdownResult
{
    public KioskMachineLockdownState State { get; init; } = KioskMachineLockdownState.Unknown;
    public bool Supported { get; init; }
    public bool Configured { get; init; }
    public bool RestartRequired { get; init; }
    public string Message { get; init; } = string.Empty;
    public string TechnicalDetails { get; init; } = string.Empty;

    public bool HasActivationLimitation =>
        State is KioskMachineLockdownState.Unsupported
            or KioskMachineLockdownState.RestartRequired
            or KioskMachineLockdownState.Cancelled
            or KioskMachineLockdownState.Failed;

    public bool BlocksDeactivation =>
        State is KioskMachineLockdownState.Cancelled or KioskMachineLockdownState.Failed;

    public static KioskMachineLockdownResult Applied(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.Applied,
            Supported = true,
            Configured = true,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult AlreadyApplied(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.AlreadyApplied,
            Supported = true,
            Configured = true,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult Removed(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.Removed,
            Supported = true,
            Configured = false,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult AlreadyRemoved(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.AlreadyRemoved,
            Supported = true,
            Configured = false,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult NotConfigured(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.NotConfigured,
            Supported = true,
            Configured = false,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult ExternallyManaged(string message, bool configured, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.ExternallyManaged,
            Supported = true,
            Configured = configured,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult Unsupported(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.Unsupported,
            Supported = false,
            Configured = false,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult RestartPending(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.RestartRequired,
            Supported = true,
            Configured = false,
            RestartRequired = true,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult Cancelled(string message, string technicalDetails = "") =>
        new()
        {
            State = KioskMachineLockdownState.Cancelled,
            Supported = true,
            Configured = false,
            Message = message,
            TechnicalDetails = technicalDetails
        };

    public static KioskMachineLockdownResult Failed(string message, string technicalDetails = "", bool supported = true, bool configured = false) =>
        new()
        {
            State = KioskMachineLockdownState.Failed,
            Supported = supported,
            Configured = configured,
            Message = message,
            TechnicalDetails = technicalDetails
        };
}

internal interface IKioskMachineLockdownService
{
    Task<KioskMachineLockdownResult> ApplyAsync(CancellationToken cancellationToken = default);
    Task<KioskMachineLockdownResult> RemoveAsync(CancellationToken cancellationToken = default);
    Task<KioskMachineLockdownResult> QueryAsync(CancellationToken cancellationToken = default);
}

internal sealed class ElevatedKioskMachineLockdownService : IKioskMachineLockdownService
{
    private readonly ILogger<ElevatedKioskMachineLockdownService> _logger;

    public ElevatedKioskMachineLockdownService(ILogger<ElevatedKioskMachineLockdownService> logger)
    {
        _logger = logger;
    }

    public Task<KioskMachineLockdownResult> ApplyAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(KioskMachineLockdownOperation.Apply, cancellationToken);

    public Task<KioskMachineLockdownResult> RemoveAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(KioskMachineLockdownOperation.Remove, cancellationToken);

    public Task<KioskMachineLockdownResult> QueryAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(KioskMachineLockdownOperation.Status, cancellationToken);

    private async Task<KioskMachineLockdownResult> ExecuteAsync(
        KioskMachineLockdownOperation operation,
        CancellationToken cancellationToken)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return KioskMachineLockdownResult.Failed("AppSwitcher executable path could not be resolved.");
        }

        var resultPath = Path.Combine(Path.GetTempPath(), $"appswitcher-kiosk-{Guid.NewGuid():N}.json");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--kiosk-machine {KioskMachineLockdownCommand.ToArgument(operation)} --result-file \"{resultPath}\""
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return KioskMachineLockdownResult.Failed(
                    $"Failed to launch the elevated kiosk helper for '{operation}'.");
            }

            await process.WaitForExitAsync(cancellationToken);

            if (!File.Exists(resultPath))
            {
                return KioskMachineLockdownResult.Failed(
                    "The elevated kiosk helper did not produce a result file.");
            }

            var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
            var result = KioskMachineLockdownSerialization.DeserializeResult(json);
            return result ?? KioskMachineLockdownResult.Failed(
                "The elevated kiosk helper returned an unreadable result payload.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogInformation("Kiosk machine lockdown operation {Operation} was cancelled at the UAC prompt", operation);
            return KioskMachineLockdownResult.Cancelled(
                "The privileged kiosk operation was cancelled.",
                ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kiosk machine lockdown operation {Operation} failed", operation);
            return KioskMachineLockdownResult.Failed(
                $"The privileged kiosk operation '{operation}' failed.",
                ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(resultPath))
                    File.Delete(resultPath);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Failed to clean up kiosk helper result file {Path}", resultPath);
            }
        }
    }
}

internal static class KioskMachineLockdownCommand
{
    public static bool IsMachineCommand(string[] args) =>
        TryParseArguments(args, out _, out _);

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParseArguments(args, out var operation, out var resultFile))
            return 2;

        KioskMachineLockdownResult result;
        try
        {
            result = await KioskMachineLockdownManager.ExecuteAsync(operation);
        }
        catch (Exception ex)
        {
            result = KioskMachineLockdownResult.Failed(
                "The kiosk helper encountered an unexpected error.",
                ex.ToString());
        }

        if (!string.IsNullOrWhiteSpace(resultFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resultFile) ?? Path.GetTempPath());
            await File.WriteAllTextAsync(resultFile, KioskMachineLockdownSerialization.SerializeResult(result));
        }

        return result.State == KioskMachineLockdownState.Failed ? 1 : 0;
    }

    public static string ToArgument(KioskMachineLockdownOperation operation) =>
        operation switch
        {
            KioskMachineLockdownOperation.Apply => "apply",
            KioskMachineLockdownOperation.Remove => "remove",
            _ => "status"
        };

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out KioskMachineLockdownOperation operation,
        out string? resultFile)
    {
        operation = KioskMachineLockdownOperation.Status;
        resultFile = null;
        var hasMachineFlag = false;

        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], "--kiosk-machine", StringComparison.OrdinalIgnoreCase))
                continue;

            hasMachineFlag = true;
            if (i + 1 >= args.Count)
                return false;

            var parsed = args[i + 1].ToLowerInvariant() switch
            {
                "apply" => KioskMachineLockdownOperation.Apply,
                "remove" => KioskMachineLockdownOperation.Remove,
                "status" => KioskMachineLockdownOperation.Status,
                _ => (KioskMachineLockdownOperation?)null
            };

            if (parsed == null)
                return false;

            operation = parsed.Value;
            break;
        }

        if (!hasMachineFlag)
            return false;

        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--result-file", StringComparison.OrdinalIgnoreCase))
            {
                resultFile = args[i + 1];
                break;
            }
        }

        return !string.IsNullOrWhiteSpace(resultFile);
    }
}

internal static class KioskMachineLockdownSerialization
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string SerializeResult(KioskMachineLockdownResult result) =>
        JsonSerializer.Serialize(result, Options);

    public static KioskMachineLockdownResult? DeserializeResult(string json) =>
        JsonSerializer.Deserialize<KioskMachineLockdownResult>(json, Options);
}

internal enum KeyboardFilterFeatureState
{
    Unknown,
    Enabled,
    Disabled,
    EnablePending
}

internal sealed class KioskMachineState
{
    public List<string> OwnedPredefinedKeys { get; set; } = [];
}

internal static class KioskMachineLockdownManager
{
    private const string KeyboardFilterNamespace = @"root\standardcimv2\embedded";
    private const string KeyboardFilterFeatureName = "Client-KeyboardFilter";
    private static readonly string[] TargetPredefinedKeys = ["Ctrl+Alt+Del", "Shift+Ctrl+Esc", "Win+L"];

    public static async Task<KioskMachineLockdownResult> ExecuteAsync(KioskMachineLockdownOperation operation)
    {
        if (!TryGetSupportedEdition(out var editionLabel))
        {
            return KioskMachineLockdownResult.Unsupported(
                "Keyboard Filter is not supported on this Windows edition.",
                editionLabel);
        }

        return operation switch
        {
            KioskMachineLockdownOperation.Apply => await ApplyAsync(editionLabel),
            KioskMachineLockdownOperation.Remove => await RemoveAsync(editionLabel),
            _ => await QueryAsync(editionLabel)
        };
    }

    private static async Task<KioskMachineLockdownResult> ApplyAsync(string editionLabel)
    {
        var featureState = await GetKeyboardFilterFeatureStateAsync();
        if (featureState == KeyboardFilterFeatureState.EnablePending)
        {
            return KioskMachineLockdownResult.RestartPending(
                "Keyboard Filter is installed but pending a restart.",
                editionLabel);
        }

        if (featureState == KeyboardFilterFeatureState.Disabled)
        {
            var enableResult = await EnableKeyboardFilterFeatureAsync();
            if (!enableResult.Success)
                return enableResult.Result;

            return KioskMachineLockdownResult.RestartPending(
                "Keyboard Filter was enabled and requires a restart before secure-key blocking becomes active.",
                editionLabel);
        }

        if (featureState != KeyboardFilterFeatureState.Enabled)
        {
            return KioskMachineLockdownResult.Failed(
                "Keyboard Filter feature state could not be determined.",
                editionLabel);
        }

        try
        {
            var scope = CreateManagementScope();
            EnsureAdministratorsExcluded(scope);

            var currentState = LoadMachineState();
            var ownedKeys = new HashSet<string>(currentState.OwnedPredefinedKeys, StringComparer.OrdinalIgnoreCase);
            using var loadedKeys = LoadPredefinedKeys(scope);

            var changed = false;
            foreach (var targetKey in TargetPredefinedKeys)
            {
                if (!loadedKeys.TryGetValue(targetKey, out var keyObject))
                {
                    return KioskMachineLockdownResult.Failed(
                        "A required Keyboard Filter predefined key could not be resolved.",
                        targetKey);
                }

                if (IsPredefinedKeyEnabled(keyObject))
                    continue;

                keyObject["Enabled"] = true;
                keyObject.Put();
                ownedKeys.Add(targetKey);
                changed = true;
            }

            SaveMachineState(new KioskMachineState
            {
                OwnedPredefinedKeys = ownedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList()
            });

            if (changed)
            {
                return KioskMachineLockdownResult.Applied(
                    "Keyboard Filter secure-key blocking was applied.",
                    editionLabel);
            }

            return currentState.OwnedPredefinedKeys.Count > 0
                ? KioskMachineLockdownResult.AlreadyApplied(
                    "Keyboard Filter secure-key blocking was already configured by AppSwitcher.",
                    editionLabel)
                : KioskMachineLockdownResult.ExternallyManaged(
                    "Keyboard Filter secure-key blocking is already configured outside AppSwitcher.",
                    configured: true,
                    technicalDetails: editionLabel);
        }
        catch (Exception ex)
        {
            return KioskMachineLockdownResult.Failed(
                "Keyboard Filter secure-key blocking could not be configured.",
                ex.Message);
        }
    }

    private static async Task<KioskMachineLockdownResult> RemoveAsync(string editionLabel)
    {
        var currentState = LoadMachineState();
        if (currentState.OwnedPredefinedKeys.Count == 0)
        {
            var queryResult = await QueryAsync(editionLabel);
            return queryResult.State == KioskMachineLockdownState.ExternallyManaged
                ? queryResult
                : KioskMachineLockdownResult.AlreadyRemoved(
                    "No AppSwitcher-owned Keyboard Filter rules were present.",
                    editionLabel);
        }

        var featureState = await GetKeyboardFilterFeatureStateAsync();
        if (featureState == KeyboardFilterFeatureState.EnablePending)
        {
            return KioskMachineLockdownResult.RestartPending(
                "Keyboard Filter changes are pending a restart.",
                editionLabel);
        }

        if (featureState == KeyboardFilterFeatureState.Disabled)
        {
            DeleteMachineState();
            return KioskMachineLockdownResult.AlreadyRemoved(
                "Keyboard Filter is disabled, so no AppSwitcher-owned rules remain.",
                editionLabel);
        }

        try
        {
            var scope = CreateManagementScope();
            using var loadedKeys = LoadPredefinedKeys(scope);

            var removedAny = false;
            foreach (var ownedKey in currentState.OwnedPredefinedKeys)
            {
                if (!loadedKeys.TryGetValue(ownedKey, out var keyObject))
                    continue;

                if (!IsPredefinedKeyEnabled(keyObject))
                    continue;

                keyObject["Enabled"] = false;
                keyObject.Put();
                removedAny = true;
            }

            DeleteMachineState();

            var stillConfigured = TargetPredefinedKeys.Any(targetKey =>
                loadedKeys.TryGetValue(targetKey, out var keyObject) && IsPredefinedKeyEnabled(keyObject));

            if (stillConfigured)
            {
                return KioskMachineLockdownResult.ExternallyManaged(
                    "Keyboard Filter remains configured outside AppSwitcher.",
                    configured: true,
                    technicalDetails: editionLabel);
            }

            return removedAny
                ? KioskMachineLockdownResult.Removed(
                    "AppSwitcher-owned Keyboard Filter rules were removed.",
                    editionLabel)
                : KioskMachineLockdownResult.AlreadyRemoved(
                    "AppSwitcher-owned Keyboard Filter rules were already absent.",
                    editionLabel);
        }
        catch (Exception ex)
        {
            return KioskMachineLockdownResult.Failed(
                "AppSwitcher-owned Keyboard Filter rules could not be removed.",
                ex.Message,
                supported: true,
                configured: true);
        }
    }

    private static async Task<KioskMachineLockdownResult> QueryAsync(string editionLabel)
    {
        var featureState = await GetKeyboardFilterFeatureStateAsync();
        if (featureState == KeyboardFilterFeatureState.EnablePending)
        {
            return KioskMachineLockdownResult.RestartPending(
                "Keyboard Filter is pending a restart.",
                editionLabel);
        }

        if (featureState == KeyboardFilterFeatureState.Disabled)
        {
            return KioskMachineLockdownResult.NotConfigured(
                "Keyboard Filter is not enabled on this device.",
                editionLabel);
        }

        if (featureState != KeyboardFilterFeatureState.Enabled)
        {
            return KioskMachineLockdownResult.Failed(
                "Keyboard Filter feature state could not be determined.",
                editionLabel);
        }

        try
        {
            var scope = CreateManagementScope();
            using var loadedKeys = LoadPredefinedKeys(scope);
            var state = LoadMachineState();
            var allEnabled = TargetPredefinedKeys.All(targetKey =>
                loadedKeys.TryGetValue(targetKey, out var keyObject) && IsPredefinedKeyEnabled(keyObject));

            if (!allEnabled)
            {
                return KioskMachineLockdownResult.NotConfigured(
                    "Keyboard Filter is enabled, but the secure-key block list is incomplete.",
                    editionLabel);
            }

            return state.OwnedPredefinedKeys.Count > 0
                ? KioskMachineLockdownResult.AlreadyApplied(
                    "Keyboard Filter secure-key blocking is active.",
                    editionLabel)
                : KioskMachineLockdownResult.ExternallyManaged(
                    "Keyboard Filter secure-key blocking is active outside AppSwitcher.",
                    configured: true,
                    technicalDetails: editionLabel);
        }
        catch (Exception ex)
        {
            return KioskMachineLockdownResult.Failed(
                "Keyboard Filter status could not be queried.",
                ex.Message);
        }
    }

    private static bool TryGetSupportedEdition(out string editionLabel)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
        var editionId = key?.GetValue("EditionID") as string ?? string.Empty;
        var productName = key?.GetValue("ProductName") as string ?? string.Empty;
        editionLabel = string.IsNullOrWhiteSpace(productName) ? editionId : $"{productName} ({editionId})";

        return editionId.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
               || editionId.Contains("Education", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
               || productName.Contains("Education", StringComparison.OrdinalIgnoreCase);
    }

    private static ManagementScope CreateManagementScope()
    {
        var scope = new ManagementScope($@"\\.\{KeyboardFilterNamespace}");
        scope.Connect();
        return scope;
    }

    private static void EnsureAdministratorsExcluded(ManagementScope scope)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM WEKF_Settings WHERE Name='DisableKeyboardFilterForAdministrators'"));
        using var setting = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

        if (setting == null)
            throw new InvalidOperationException("WEKF_Settings.DisableKeyboardFilterForAdministrators was not found.");

        var currentValue = Convert.ToString(setting["Value"]) ?? string.Empty;
        if (string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase))
            return;

        setting["Value"] = "true";
        setting.Put();
    }

    private static DisposableDictionary LoadPredefinedKeys(ManagementScope scope)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WEKF_PredefinedKey"));
        var map = new Dictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);
        foreach (ManagementObject keyObject in searcher.Get())
        {
            var id = Convert.ToString(keyObject["Id"]);
            if (string.IsNullOrWhiteSpace(id))
            {
                keyObject.Dispose();
                continue;
            }

            map[id] = keyObject;
        }

        return new DisposableDictionary(map);
    }

    private static bool IsPredefinedKeyEnabled(ManagementObject managementObject) =>
        managementObject["Enabled"] is bool enabled && enabled;

    private static KioskMachineState LoadMachineState()
    {
        try
        {
            if (!File.Exists(AppPaths.KioskMachineStateFilePath))
                return new KioskMachineState();

            var json = File.ReadAllText(AppPaths.KioskMachineStateFilePath);
            return JsonSerializer.Deserialize<KioskMachineState>(json) ?? new KioskMachineState();
        }
        catch
        {
            return new KioskMachineState();
        }
    }

    private static void SaveMachineState(KioskMachineState state)
    {
        if (state.OwnedPredefinedKeys.Count == 0)
        {
            DeleteMachineState();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.KioskMachineStateFilePath) ?? AppPaths.MachineDataDirectory);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.KioskMachineStateFilePath, json);
    }

    private static void DeleteMachineState()
    {
        if (File.Exists(AppPaths.KioskMachineStateFilePath))
            File.Delete(AppPaths.KioskMachineStateFilePath);
    }

    private static async Task<(bool Success, KioskMachineLockdownResult Result)> EnableKeyboardFilterFeatureAsync()
    {
        var enableResult = await RunProcessAsync(
            "dism.exe",
            $"/online /Enable-Feature /FeatureName:{KeyboardFilterFeatureName} /NoRestart /English");

        if (enableResult.ExitCode == 0 || enableResult.ExitCode == 3010)
        {
            return (true, KioskMachineLockdownResult.RestartPending(
                "Keyboard Filter was enabled and requires a restart before it can be used.",
                enableResult.Output));
        }

        return (false, KioskMachineLockdownResult.Failed(
            "Keyboard Filter could not be enabled.",
            enableResult.Output));
    }

    private static async Task<KeyboardFilterFeatureState> GetKeyboardFilterFeatureStateAsync()
    {
        var result = await RunProcessAsync(
            "dism.exe",
            $"/online /Get-FeatureInfo /FeatureName:{KeyboardFilterFeatureName} /English");

        return result.ExitCode == 0
            ? ParseFeatureState(result.Output)
            : KeyboardFilterFeatureState.Unknown;
    }

    internal static KeyboardFilterFeatureState ParseFeatureState(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return KeyboardFilterFeatureState.Unknown;

        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase))
                continue;

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
                continue;

            var stateValue = trimmed[(separatorIndex + 1)..].Trim();
            if (stateValue.Contains("Enable Pending", StringComparison.OrdinalIgnoreCase)
                || (stateValue.Contains("Pending", StringComparison.OrdinalIgnoreCase)
                    && stateValue.Contains("Enable", StringComparison.OrdinalIgnoreCase)))
            {
                return KeyboardFilterFeatureState.EnablePending;
            }

            if (stateValue.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
                return KeyboardFilterFeatureState.Disabled;

            if (stateValue.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
                return KeyboardFilterFeatureState.Enabled;
        }

        return KeyboardFilterFeatureState.Unknown;
    }

    private static async Task<ProcessExecutionResult> RunProcessAsync(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combinedOutput = string.IsNullOrWhiteSpace(standardError)
            ? standardOutput
            : $"{standardOutput}{Environment.NewLine}{standardError}".Trim();

        return new ProcessExecutionResult(process.ExitCode, combinedOutput);
    }

    internal readonly record struct ProcessExecutionResult(int ExitCode, string Output);

    private sealed class DisposableDictionary : IDisposable
    {
        private readonly Dictionary<string, ManagementObject> _inner;

        public DisposableDictionary(Dictionary<string, ManagementObject> inner)
        {
            _inner = inner;
        }

        public bool TryGetValue(string key, out ManagementObject managementObject) =>
            _inner.TryGetValue(key, out managementObject!);

        public void Dispose()
        {
            foreach (var item in _inner.Values)
                item.Dispose();
        }
    }
}
