using System.ComponentModel;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
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
            if (result == null)
            {
                return KioskMachineLockdownResult.Failed(
                    "The elevated kiosk helper returned an unreadable result payload.");
            }

            _logger.LogInformation(
                "Kiosk machine lockdown {Operation} returned {State}: {Message} ({Details})",
                operation, result.State, result.Message, result.TechnicalDetails);
            return result;
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

    // Set when the record existed but could not be read: ownership is unknown, so nothing may be removed.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Unreadable { get; init; }
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
            var currentState = LoadTrustedMachineState(out var recoveryNote);
            var scope = CreateManagementScope();
            EnsureAdministratorsExcluded(scope);

            var details = AppendNote(editionLabel, recoveryNote);
            var ownedKeys = new HashSet<string>(currentState.OwnedPredefinedKeys, StringComparer.OrdinalIgnoreCase);
            using var loadedKeys = LoadPredefinedKeys(scope);

            var changed = false;
            foreach (var targetKey in TargetPredefinedKeys)
            {
                if (!loadedKeys.TryGetValue(targetKey, out var keyObject))
                {
                    return KioskMachineLockdownResult.Failed(
                        "A required Keyboard Filter predefined key could not be resolved.",
                        AppendNote(targetKey, recoveryNote));
                }

                if (IsPredefinedKeyEnabled(keyObject))
                    continue;

                keyObject["Enabled"] = true;
                keyObject.Put();
                ownedKeys.Add(targetKey);
                // Record ownership right away so an early return or a later Put failure cannot orphan this key.
                SaveMachineState(ownedKeys);
                changed = true;
            }

            SaveMachineState(ownedKeys);

            if (changed)
            {
                return KioskMachineLockdownResult.Applied(
                    "Keyboard Filter secure-key blocking was applied.",
                    details);
            }

            return currentState.OwnedPredefinedKeys.Count > 0
                ? KioskMachineLockdownResult.AlreadyApplied(
                    "Keyboard Filter secure-key blocking was already configured by AppSwitcher.",
                    details)
                : KioskMachineLockdownResult.ExternallyManaged(
                    "Keyboard Filter secure-key blocking is already configured outside AppSwitcher.",
                    configured: true,
                    technicalDetails: details);
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
        KioskMachineState currentState;
        string recoveryNote;
        try
        {
            currentState = LoadTrustedMachineState(out recoveryNote);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return KioskMachineLockdownResult.Failed("The kiosk machine state could not be read safely.", ex.Message);
        }

        editionLabel = AppendNote(editionLabel, recoveryNote);
        if (currentState.Unreadable)
        {
            // Fail closed: leave every rule in place. The damaged record is preserved; drop it so kiosk can still be
            // turned off in the app without another elevation prompt.
            DeleteMachineState();
            return KioskMachineLockdownResult.ExternallyManaged(
                "The record of AppSwitcher's Keyboard Filter rules was unreadable, so no rule was removed. Review Keyboard Filter manually.",
                configured: true,
                technicalDetails: editionLabel);
        }

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
            var state = LoadTrustedMachineState(out _);
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

    internal static bool TryGetSupportedEdition(out string editionLabel)
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

    // Elevated entry point: secure the folder first; a record found in an untrusted folder may have been planted.
    private static KioskMachineState LoadTrustedMachineState(out string recoveryNote)
    {
        var quarantined = SecureMachineDataDirectory(AppPaths.MachineDataDirectory);
        if (quarantined != null && File.Exists(Path.Combine(quarantined, Path.GetFileName(AppPaths.KioskMachineStateFilePath))))
        {
            recoveryNote = $"An earlier Keyboard Filter record was in a folder standard users could change; it was moved to '{quarantined}' and not trusted. No Keyboard Filter rule was removed; an administrator must review them.";
            return new KioskMachineState { Unreadable = true };
        }

        // The folder is admin-controlled now, but a file could predate that (e.g. an ACL tightened by hand). Check it too.
        var statePath = AppPaths.KioskMachineStateFilePath;
        if (File.Exists(statePath) && !IsTrustedFile(statePath))
        {
            var untrustedCopy = $"{statePath}.untrusted-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(statePath, untrustedCopy); // Renames a link itself, never its target.
            recoveryNote = $"The Keyboard Filter record was not exclusively controlled by administrators; it was moved to '{untrustedCopy}' and not trusted. No Keyboard Filter rule was removed; an administrator must review them.";
            return new KioskMachineState { Unreadable = true };
        }

        var state = LoadMachineState(statePath, out recoveryNote);
        if (quarantined != null)
            recoveryNote = AppendNote(recoveryNote, $"An untrusted machine data folder was moved to '{quarantined}'.");
        return state;
    }

    internal static KioskMachineState LoadMachineState(string path, out string recoveryNote)
    {
        recoveryNote = string.Empty;
        if (!File.Exists(path))
            return new KioskMachineState();

        try
        {
            var state = JsonSerializer.Deserialize<KioskMachineState>(File.ReadAllText(path));
            if (state?.OwnedPredefinedKeys != null)
                return state;
        }
        catch (JsonException)
        {
        }

        // Ownership is unknown. Fail closed: keep the record for review and remove nothing, so keys another
        // administrator enabled are never switched off on the strength of a damaged (or planted) file.
        var backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        File.Copy(path, backupPath, overwrite: false);
        recoveryNote = $"Kiosk machine state file was unreadable and was preserved as '{backupPath}'. No Keyboard Filter rule was removed; an administrator must review them.";
        return new KioskMachineState { Unreadable = true };
    }

    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);

    private const FileSystemRights ModifyingRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// Runs elevated, before any read or write of the machine state. Earlier versions let the non-elevated app (or an
    /// inherited ProgramData ACL) leave this folder writable by standard users, who could plant files or links in it.
    /// An existing folder is never modified in place: unless it is already trusted it is moved aside, and a new one is
    /// created with an administrators-only ACL and checked again. Returns the quarantined folder, or null.
    /// </summary>
    internal static string? SecureMachineDataDirectory(string directory)
    {
        var info = new DirectoryInfo(directory);
        string? quarantined = null;
        if (info.Exists && !IsTrustedDirectory(info))
        {
            // Directory.Move renames a link itself, never its target.
            quarantined = $"{directory}.untrusted-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            Directory.Move(directory, quarantined);
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(AdministratorsSid);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new(AdministratorsSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(SystemSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        info.Create(security); // The descriptor is applied at creation; an existing folder is left untouched here.

        // Something may have re-created the folder (or a link) between the move and the creation: fail closed.
        info.Refresh();
        if (!IsTrustedDirectory(info))
            throw new IOException($"Refusing to use '{directory}': it is not exclusively controlled by administrators.");
        return quarantined;
    }

    // Trusted: a real folder (not a link), owned by Administrators or SYSTEM, with a protected ACL that lets nobody
    // else modify it. Files inside such a folder can only have been written by administrators.
    internal static bool IsTrustedDirectory(DirectoryInfo info)
    {
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        var acl = info.GetAccessControl();
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || (owner != AdministratorsSid && owner != SystemSid))
            return false;
        return acl.AreAccessRulesProtected && OnlyAdministratorsCanModify(acl);
    }

    // Trusted: a regular file (no link, exactly one hard link), owned by Administrators or SYSTEM, that nobody else can modify.
    internal static bool IsTrustedFile(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var information) || information.NumberOfLinks != 1) return false;
        var acl = stream.GetAccessControl();
        return acl.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
            && (owner == AdministratorsSid || owner == SystemSid)
            && OnlyAdministratorsCanModify(acl);
    }

    private static bool OnlyAdministratorsCanModify(FileSystemSecurity acl)
    {
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            // Raw ACEs can carry generic rights, which FileSystemRights does not name.
            var mask = (int)rule.FileSystemRights;
            var modifies = (rule.FileSystemRights & ModifyingRights) != 0 || (mask & (GenericWrite | GenericAll)) != 0;
            if (rule.AccessControlType == AccessControlType.Allow && modifies
                && rule.IdentityReference != AdministratorsSid && rule.IdentityReference != SystemSid)
                return false;
        }
        return true;
    }

    private const int GenericWrite = 0x40000000, GenericAll = 0x10000000;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out ByHandleFileInformation information);

    private static void SaveMachineState(IEnumerable<string> ownedKeys) =>
        SaveMachineState(AppPaths.KioskMachineStateFilePath, ownedKeys);

    internal static void SaveMachineState(string path, IEnumerable<string> ownedKeys)
    {
        var state = new KioskMachineState
        {
            OwnedPredefinedKeys = ownedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList()
        };

        if (state.OwnedPredefinedKeys.Count == 0)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        // A fresh, unpredictable temp name opened with CreateNew never follows or truncates a planted file.
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string AppendNote(string details, string note) =>
        string.IsNullOrEmpty(note) ? details : $"{details}; {note}";

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
