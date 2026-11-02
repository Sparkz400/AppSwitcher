using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace AppSwitcher.Services;

internal enum BrowserExtensionInstall { Store, Unpacked, FirefoxFile, Unavailable }
internal sealed record BrowserSetupPackage(BrowserKind Browser, BrowserExtensionInstall InstallKind,
    string InstallTarget, string ExtensionDirectory);
internal interface IBrowserSetupService
{
    Task<BrowserSetupPackage> PrepareAsync(BrowserKind browser, CancellationToken cancellation);
    void Open(BrowserKind browser, string target);
    void CopyPath(string path);
}

/// <summary>Explicit, per-user setup only. Never edits browser profiles or machine policy.</summary>
internal sealed class BrowserSetupService : IBrowserSetupService
{
    internal static readonly string[] RequiredHostFiles = ["AppSwitcher.BrowserHost.exe", "AppSwitcher.BrowserHost.dll",
        "AppSwitcher.BrowserHost.runtimeconfig.json", "AppSwitcher.BrowserHost.deps.json", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll"];
    private readonly string _bundle;
    private readonly string _destination;
    private readonly Action<BrowserKind, string> _register;
    private readonly Func<BrowserKind, string[]> _registeredIds;
    internal BrowserSetupService(string? bundle = null, string? destination = null, Action<BrowserKind, string>? register = null,
        Func<BrowserKind, string[]>? registeredIds = null)
    {
        _bundle = bundle ?? Path.Combine(AppContext.BaseDirectory, "browser-setup");
        _destination = destination ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AppSwitcherBrowserSetup");
        _register = register ?? Register;
        _registeredIds = registeredIds ?? (register == null ? RegisteredIds : _ => []);
    }

    public Task<BrowserSetupPackage> PrepareAsync(BrowserKind browser, CancellationToken cancellation) =>
        Task.Run(() => Prepare(browser, cancellation), cancellation);

    private BrowserSetupPackage Prepare(BrowserKind browser, CancellationToken ct)
    {
        if (!Enum.IsDefined(browser)) throw new ArgumentOutOfRangeException(nameof(browser));
        var hostSource = Path.Combine(_bundle, "host");
        if (RequiredHostFiles.Any(file => !File.Exists(Path.Combine(hostSource, file)))) throw new InvalidOperationException("Setup_PackageMissing");
        using var distribution = JsonDocument.Parse(File.ReadAllText(Path.Combine(_bundle, "distribution.json")));
        // Offline is the default. A store URL alone must never turn setup into an internet dependency.
        var onlineAllowed = distribution.RootElement.TryGetProperty("allowOnlineInstall", out var online) && online.GetBoolean();
        var entry = distribution.RootElement.GetProperty(browser.ToString().ToLowerInvariant());
        var id = entry.GetProperty("extensionId").GetString() ?? "";
        ValidateId(browser, id);
        var url = entry.TryGetProperty("installUrl", out var property) ? property.GetString() : null;
        if (!string.IsNullOrEmpty(url) && !ValidStoreUrl(browser, id, url)) throw new InvalidDataException("Setup_InvalidPackage");
        var kind = onlineAllowed && !string.IsNullOrEmpty(url) ? BrowserExtensionInstall.Store
            : browser == BrowserKind.Firefox ? BrowserExtensionInstall.Unavailable : BrowserExtensionInstall.Unpacked;
        var extension = Path.Combine(_bundle, browser == BrowserKind.Firefox ? "firefox" : "chromium");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(extension, "manifest.json")));
        if (kind == BrowserExtensionInstall.Unpacked && ChromiumId(manifest.RootElement.GetProperty("key").GetString()!) != id)
            throw new InvalidDataException("Setup_InvalidPackage");
        if (browser == BrowserKind.Firefox && manifest.RootElement.GetProperty("browser_specific_settings").GetProperty("gecko").GetProperty("id").GetString() != id)
            throw new InvalidDataException("Setup_InvalidPackage");

        var xpi = Path.Combine(_bundle, "appswitcher-firefox.xpi");
        if (browser == BrowserKind.Firefox && File.Exists(xpi))
        {
            // Firefox itself verifies Mozilla's signature. Reject obviously wrong submission ZIPs here.
            using var archive = ZipFile.OpenRead(xpi);
            using var content = archive.GetEntry("manifest.json")?.Open() ?? throw new InvalidDataException("Setup_InvalidPackage");
            using var xpiManifest = JsonDocument.Parse(content);
            if (xpiManifest.RootElement.GetProperty("browser_specific_settings").GetProperty("gecko").GetProperty("id").GetString() != id
                || !archive.Entries.Any(e => e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Setup_InvalidPackage");
            kind = BrowserExtensionInstall.FirefoxFile;
        }
        if (kind != BrowserExtensionInstall.Store) url = null;

        // Immutable host directories allow repair while a browser is still using the old executable.
        // Keep old versions: another browser's registration or running process may still use one.
        // Preserve previously approved store IDs when repairing a distribution with a different ID.
        var ids = _registeredIds(browser).Append(id).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var existingId in ids) ValidateId(browser, existingId);
        var allowed = browser == BrowserKind.Firefox ? ids : ids.Select(value => $"chrome-extension://{value}/").ToArray();
        var origins = JsonSerializer.Serialize(allowed);
        var hostFiles = Directory.GetFiles(hostSource).Order(StringComparer.Ordinal).ToArray();
        var version = Fingerprint(hostFiles, origins, ct);
        var hostDirectory = Path.Combine(_destination, "hosts", browser.ToString(), version);
        if (!FilesMatch(hostFiles, hostDirectory, ct)) hostDirectory += "-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(hostDirectory);
        foreach (var source in hostFiles) CopyIfDifferent(source, Path.Combine(hostDirectory, Path.GetFileName(source)), ct);
        WriteAtomic(Path.Combine(hostDirectory, "allowed-origins.json"), origins, ct);
        var hostManifest = new Dictionary<string, object>
        {
            ["name"] = "com.appswitcher.browser", ["description"] = "AppSwitcher browser bridge",
            ["path"] = Path.Combine(hostDirectory, "AppSwitcher.BrowserHost.exe"), ["type"] = "stdio",
            [browser == BrowserKind.Firefox ? "allowed_extensions" : "allowed_origins"] = allowed
        };
        var manifestPath = Path.Combine(hostDirectory, "host.json");
        WriteAtomic(manifestPath, JsonSerializer.Serialize(hostManifest), ct);

        var extensionDirectory = Path.Combine(_destination, "extensions", browser.ToString(), id);
        if (kind == BrowserExtensionInstall.Unpacked)
        {
            Directory.CreateDirectory(extensionDirectory);
            // Manifest last. The stable public key and location preserve the extension's identity.
            foreach (var file in Directory.GetFiles(extension).OrderBy(p => Path.GetFileName(p) == "manifest.json"))
                CopyIfDifferent(file, Path.Combine(extensionDirectory, Path.GetFileName(file)), ct);
        }
        if (kind == BrowserExtensionInstall.FirefoxFile)
        {
            Directory.CreateDirectory(extensionDirectory);
            url = Path.Combine(extensionDirectory, "appswitcher-firefox.xpi");
            CopyIfDifferent(xpi, url, ct);
        }
        ct.ThrowIfCancellationRequested();
        _register(browser, manifestPath); // Last: only point the browser at a fully prepared installation.
        return new(browser, kind, url ?? ExtensionsPage(browser), extensionDirectory);
    }

    internal static string ChromiumId(string key) => string.Concat(SHA256.HashData(Convert.FromBase64String(key)).Take(16)
        .SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 15)) }));
    internal static void ValidateId(BrowserKind browser, string id)
    {
        if (browser == BrowserKind.Firefox ? !Regex.IsMatch(id, @"\A[A-Za-z0-9@._{}-]{1,128}\z") : !Regex.IsMatch(id, @"\A[a-p]{32}\z"))
            throw new InvalidDataException("Setup_InvalidPackage");
    }
    internal static bool ValidStoreUrl(BrowserKind browser, string id, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        return browser switch
        {
            BrowserKind.Chrome => uri.Host == "chromewebstore.google.com" && segments.Length >= 2 && segments[0] == "detail" && segments[^1] == id,
            BrowserKind.Edge => uri.Host == "microsoftedge.microsoft.com" && segments.Length >= 4 && segments[0] == "addons" && segments[1] == "detail" && segments[^1] == id,
            BrowserKind.Firefox => uri.Host == "addons.mozilla.org" && segments.Contains("addon") && segments.Length >= 3,
            _ => false
        };
    }
    internal static string ExtensionsPage(BrowserKind browser) => browser switch { BrowserKind.Chrome => "chrome://extensions", BrowserKind.Edge => "edge://extensions", _ => "about:addons" };
    internal static string StartupPage(BrowserKind browser) => browser switch { BrowserKind.Chrome => "chrome://settings/onStartup", BrowserKind.Edge => "edge://settings/startHomeNTP", _ => "about:preferences#general" };
    internal static string RegistryPath(BrowserKind browser) => $@"Software\{browser switch { BrowserKind.Chrome => @"Google\Chrome", BrowserKind.Edge => @"Microsoft\Edge", _ => "Mozilla" }}\NativeMessagingHosts\com.appswitcher.browser";
    private static string[] RegisteredIds(BrowserKind browser)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(RegistryPath(browser));
                if (key?.GetValue(null) is not string path || !File.Exists(path)) continue;
                using var manifest = JsonDocument.Parse(File.ReadAllText(path));
                if (manifest.RootElement.GetProperty("name").GetString() != "com.appswitcher.browser") continue;
                foreach (var value in manifest.RootElement.GetProperty(browser == BrowserKind.Firefox ? "allowed_extensions" : "allowed_origins").EnumerateArray())
                {
                    var id = value.GetString() ?? "";
                    if (browser != BrowserKind.Firefox)
                    {
                        var match = Regex.Match(id, @"\Achrome-extension://([a-p]{32})/\z");
                        if (!match.Success) continue;
                        id = match.Groups[1].Value;
                    }
                    if (browser == BrowserKind.Firefox && !Regex.IsMatch(id, @"\A[A-Za-z0-9@._{}-]{1,128}\z")) continue;
                    ids.Add(id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException or ArgumentException or KeyNotFoundException or InvalidOperationException)
            { /* A broken registration is repairable using the bundled ID. */ }
        }
        return ids.ToArray();
    }
    private static void Register(BrowserKind browser, string manifest)
    {
        var previous = new List<(RegistryView View, object? Value)>();
        try
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 }.Distinct())
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = root.CreateSubKey(RegistryPath(browser));
                previous.Add((view, key.GetValue(null)));
                key.SetValue("", manifest, RegistryValueKind.String);
            }
        }
        catch
        {
            foreach (var (view, old) in previous.AsEnumerable().Reverse())
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = root.CreateSubKey(RegistryPath(browser));
                if (old is string value) key.SetValue("", value); else key.DeleteValue("", false);
            }
            throw;
        }
    }

    public void Open(BrowserKind browser, string target)
    {
        var exe = FindExecutable(browser) ?? throw new InvalidOperationException(LocalizationService.T("Setup_BrowserMissing", browser));
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add(target);
        using var process = Process.Start(start);
    }
    public void CopyPath(string path) => Clipboard.SetText(path);
    private static string? FindExecutable(BrowserKind browser)
    {
        var name = browser switch { BrowserKind.Chrome => "chrome.exe", BrowserKind.Edge => "msedge.exe", _ => "firefox.exe" };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        var relative = browser switch { BrowserKind.Chrome => @"Google\Chrome\Application\chrome.exe", BrowserKind.Edge => @"Microsoft\Edge\Application\msedge.exe", _ => @"Mozilla Firefox\firefox.exe" };
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), relative)).FirstOrDefault(File.Exists);
    }
    private static string Fingerprint(string[] files, string origins, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(origins));
        foreach (var file in files) { ct.ThrowIfCancellationRequested(); hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file))); using var stream = File.OpenRead(file); hash.AppendData(SHA256.HashData(stream)); }
        return Convert.ToHexString(hash.GetHashAndReset())[..24];
    }
    private static bool FilesMatch(string[] files, string destination, CancellationToken ct) => !Directory.Exists(destination)
        || files.All(file => SameFile(file, Path.Combine(destination, Path.GetFileName(file)), ct));
    private static bool SameFile(string source, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(destination) || new FileInfo(source).Length != new FileInfo(destination).Length) return false;
        using var first = File.OpenRead(source); using var second = File.OpenRead(destination);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }
    private static void CopyIfDifferent(string source, string destination, CancellationToken ct)
    {
        if (SameFile(source, destination, ct)) return;
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temp); ct.ThrowIfCancellationRequested(); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteAtomic(string path, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(path) && File.ReadAllText(path) == value) return;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value, new UTF8Encoding(false)); ct.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
