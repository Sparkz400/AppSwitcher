namespace AppSwitcher.Tests;

public class BrowserSetupTests
{
    private const string Id = "afmmehaldghpcccgholfklbeemokekoj";
    private static BrowserProfileChoice Profile(BrowserKind browser = BrowserKind.Edge) => new(Guid.NewGuid().ToString(), browser, "Work", "1.1.0");

    [Theory]
    [InlineData(BrowserKind.Chrome, false)]
    [InlineData(BrowserKind.Edge, false)]
    [InlineData(BrowserKind.Firefox, false)]
    [InlineData(BrowserKind.Chrome, true)]
    public async Task Offline_Default_Does_Not_Open_A_Store_Even_If_A_Link_Is_Configured(BrowserKind browser, bool online)
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        var distributionFile = Path.Combine(bundle, "distribution.json");
        var distribution = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(distributionFile))!;
        var url = browser switch
        {
            BrowserKind.Chrome => $"https://chromewebstore.google.com/detail/appswitcher/{Id}",
            BrowserKind.Edge => $"https://microsoftedge.microsoft.com/addons/detail/appswitcher/{Id}",
            _ => "https://addons.mozilla.org/en-US/firefox/addon/appswitcher/"
        };
        distribution[browser.ToString().ToLowerInvariant()]!["installUrl"] = url;
        distribution.AsObject().Remove("allowOnlineInstall"); // Omitted is offline, too.
        if (online) distribution["allowOnlineInstall"] = true;
        File.WriteAllText(distributionFile, distribution.ToJsonString());
        var package = await new BrowserSetupService(bundle, destination, (_, _) => { }).PrepareAsync(browser, default);
        Assert.Equal(online ? BrowserExtensionInstall.Store : browser == BrowserKind.Firefox ? BrowserExtensionInstall.Unavailable : BrowserExtensionInstall.Unpacked, package.InstallKind);
        Assert.Equal(online ? url : BrowserSetupService.ExtensionsPage(browser), package.InstallTarget);
    }

    [Fact]
    public async Task Bundled_Firefox_File_Takes_Precedence_Over_Store_And_Is_Passed_To_Firefox_For_Signature_Verification()
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        var distributionFile = Path.Combine(bundle, "distribution.json");
        var distribution = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(distributionFile))!;
        distribution["allowOnlineInstall"] = true;
        distribution["firefox"]!["installUrl"] = "https://addons.mozilla.org/en-US/firefox/addon/appswitcher/";
        File.WriteAllText(distributionFile, distribution.ToJsonString());
        using (var archive = System.IO.Compression.ZipFile.Open(Path.Combine(bundle, "appswitcher-firefox.xpi"), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var content = new StreamWriter(archive.CreateEntry("manifest.json").Open())) content.Write(File.ReadAllText(Path.Combine(bundle, "firefox", "manifest.json")));
            using (var metadata = new StreamWriter(archive.CreateEntry("META-INF/mozilla.rsa").Open())) metadata.Write("Signature metadata fixture; only Firefox can verify the real signature.");
        }
        var package = await new BrowserSetupService(bundle, destination, (_, _) => { }).PrepareAsync(BrowserKind.Firefox, default);
        Assert.Equal(BrowserExtensionInstall.FirefoxFile, package.InstallKind);
        Assert.True(File.Exists(package.InstallTarget));
        Assert.StartsWith(destination, package.InstallTarget);
    }

    [Fact]
    public async Task Installation_Copies_The_Path_Before_Opening_The_Browser_And_Handles_A_Busy_Clipboard()
    {
        var service = new SetupFixtureService();
        using var model = new BrowserSetupViewModel(BrowserKind.Edge, "", service, () => [], () => true);
        model.OpenStartupCommand.Execute(null);
        Assert.Empty(service.Opened);
        await model.PrepareCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Copy", "Open" }, service.Actions);
        Assert.Equal(model.ExtensionDirectory, Assert.Single(service.Copied));
        Assert.Equal("Setup_PathCopied", model.PathStatus);
        Assert.False(model.OpenStartupCommand.CanExecute(null));
        service.ClipboardBusy = true;
        model.OpenInstallCommand.Execute(null);
        Assert.Equal("Setup_CopyFailed", model.PathStatus);
        Assert.Equal(2, service.Opened.Count);
    }

    [Fact]
    public async Task Missing_Firefox_File_Is_A_Blocked_Package_Not_A_Waiting_Installation()
    {
        IReadOnlyList<BrowserProfileChoice> profiles = [];
        var service = new SetupFixtureService();
        using var model = new BrowserSetupViewModel(BrowserKind.Firefox, "", service, () => profiles, () => true);
        await model.PrepareCommand.ExecuteAsync(null);
        model.RefreshConnection();
        Assert.True(model.PackageBlocked);
        Assert.Equal("Setup_FirefoxBlocked", model.Status);
        Assert.False(model.ShowInstallActions); Assert.False(model.ShowInstallSteps); Assert.False(model.ShowFinish);
        Assert.Empty(service.Copied); Assert.Empty(service.Opened);
        profiles = [Profile(BrowserKind.Firefox)]; model.RefreshConnection();
        Assert.True(model.ConnectionReady); Assert.False(model.PackageBlocked); Assert.True(model.CanFinish);
    }

    [Fact]
    public async Task Installs_Only_Selected_Browser_And_Repairs_Without_Overwriting_A_Running_Host()
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        var registrations = new List<(BrowserKind Browser, string Manifest)>();
        var service = new BrowserSetupService(bundle, destination, (browser, manifest) => registrations.Add((browser, manifest)));
        var result = await service.PrepareAsync(BrowserKind.Edge, default);
        Assert.Equal(BrowserExtensionInstall.Unpacked, result.InstallKind);
        Assert.Equal("edge://extensions", result.InstallTarget);
        Assert.True(File.Exists(Path.Combine(result.ExtensionDirectory, "manifest.json")));
        var first = Assert.Single(registrations);
        Assert.Equal(BrowserKind.Edge, first.Browser);
        using var manifest = JsonDocument.Parse(File.ReadAllText(first.Manifest));
        Assert.Equal(new[] { $"chrome-extension://{Id}/" }, manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(x => x.GetString()));
        var hostPath = manifest.RootElement.GetProperty("path").GetString()!;
        using (var inUse = File.Open(hostPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await service.PrepareAsync(BrowserKind.Edge, default);
            Assert.Equal(first.Manifest, registrations[^1].Manifest);
            File.WriteAllText(Path.Combine(bundle, "host", "AppSwitcher.BrowserHost.exe"), "new host version");
            await service.PrepareAsync(BrowserKind.Edge, default);
            Assert.NotEqual(first.Manifest, registrations[^1].Manifest);
            Assert.Equal("host fixture", File.ReadAllText(hostPath));
        }
        Assert.All(registrations, item => Assert.Equal(BrowserKind.Edge, item.Browser));
    }

    [Fact]
    public async Task Repair_Preserves_An_Existing_Store_Extension_Origin()
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        var storeId = new string('b', 32);
        string? registered = null;
        var service = new BrowserSetupService(bundle, destination, (_, path) => registered = path, _ => [storeId]);
        await service.PrepareAsync(BrowserKind.Chrome, default);
        using var manifest = JsonDocument.Parse(File.ReadAllText(registered!));
        var origins = manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains($"chrome-extension://{storeId}/", origins);
        Assert.Contains($"chrome-extension://{Id}/", origins);
        Assert.Equal(origins, JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(registered)!, "allowed-origins.json"))));
    }

    [Fact]
    public async Task Bad_Or_Cancelled_Packages_Do_Not_Change_Registration()
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        var registrations = 0;
        var service = new BrowserSetupService(bundle, destination, (_, _) => registrations++);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(BrowserKind.Chrome, new CancellationToken(true)));
        Assert.False(Directory.Exists(destination));
        File.WriteAllText(Path.Combine(bundle, "distribution.json"), JsonSerializer.Serialize(new { chrome = new { extensionId = new string('a', 32), installUrl = (string?)null } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(BrowserKind.Chrome, default));
        Assert.Equal(0, registrations);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Firefox_Prepares_Host_But_Does_Not_Offer_Temporary_Installation_As_Permanent()
    {
        using var files = new TempDirectory();
        var (bundle, destination) = Bundle(files.Path);
        string? registered = null;
        var package = await new BrowserSetupService(bundle, destination, (_, path) => registered = path).PrepareAsync(BrowserKind.Firefox, default);
        Assert.Equal(BrowserExtensionInstall.Unavailable, package.InstallKind);
        using var manifest = JsonDocument.Parse(File.ReadAllText(registered!));
        Assert.Equal("browser@appswitcher.local", manifest.RootElement.GetProperty("allowed_extensions")[0].GetString());
        Assert.False(manifest.RootElement.TryGetProperty("allowed_origins", out _));
    }

    [Theory]
    [InlineData(BrowserKind.Chrome, "https://chromewebstore.google.com/detail/appswitcher/afmmehaldghpcccgholfklbeemokekoj", true)]
    [InlineData(BrowserKind.Edge, "https://microsoftedge.microsoft.com/addons/detail/appswitcher/afmmehaldghpcccgholfklbeemokekoj", true)]
    [InlineData(BrowserKind.Firefox, "https://addons.mozilla.org/en-US/firefox/addon/appswitcher/", true)]
    [InlineData(BrowserKind.Chrome, "https://chromewebstore.google.com.evil.invalid/detail/afmmehaldghpcccgholfklbeemokekoj", false)]
    [InlineData(BrowserKind.Chrome, "https://chromewebstore.google.com/detail/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData(BrowserKind.Edge, "file:///C:/script.exe", false)]
    [InlineData(BrowserKind.Firefox, "https://addons.mozilla.org/en-US/firefox/addon/x/?next=evil", false)]
    public void Store_Links_Are_Restricted_To_Official_Install_Pages(BrowserKind browser, string url, bool valid) =>
        Assert.Equal(valid, BrowserSetupService.ValidStoreUrl(browser, Id, url));

    [Fact]
    public async Task Existing_Pairing_Is_Never_Replaced_By_A_Different_Online_Profile()
    {
        var saved = Profile(); var other = Profile();
        IReadOnlyList<BrowserProfileChoice> live = [other, Profile(BrowserKind.Chrome)];
        var service = new SetupFixtureService();
        using var model = new BrowserSetupViewModel(BrowserKind.Edge, saved.ProfileId, service, () => live, () => true);
        await model.PrepareCommand.ExecuteAsync(null);
        Assert.False(model.CanFinish); Assert.Empty(model.Profiles);
        Assert.Single(service.Opened);
        live = [other, saved]; model.RefreshConnection();
        Assert.Equal(saved, model.SelectedProfile);
        BrowserProfileChoice? accepted = null; model.Completed += value => accepted = value;
        model.FinishCommand.Execute(null);
        Assert.Equal(saved, accepted);
    }

    [Fact]
    public async Task Multiple_Profiles_Require_Selection_And_A_Last_Second_Disconnect_Rejects_Acceptance()
    {
        var first = Profile(); var second = Profile();
        IReadOnlyList<BrowserProfileChoice> live = [first, second];
        var service = new SetupFixtureService();
        using var model = new BrowserSetupViewModel(BrowserKind.Edge, "", service, () => live, () => true);
        await model.PrepareCommand.ExecuteAsync(null);
        Assert.True(model.ShowProfiles); Assert.Null(model.SelectedProfile); Assert.False(model.CanFinish);
        Assert.Empty(service.Opened);
        model.SelectedProfile = second;
        Assert.True(model.CanFinish);
        BrowserProfileChoice? accepted = null; model.Completed += value => accepted = value;
        live = [first]; model.FinishCommand.Execute(null);
        Assert.Null(accepted);
        model.RefreshConnection(); model.FinishCommand.Execute(null);
        Assert.Null(accepted); Assert.Null(model.SelectedProfile); Assert.True(model.ShowProfiles);
        live = [first, first]; model.RefreshConnection();
        Assert.Empty(model.Profiles); Assert.False(model.CanFinish);
    }

    [Fact]
    public async Task Connected_Profile_Skips_Opening_Installation_And_Access_Expiry_Blocks_Actions()
    {
        var service = new SetupFixtureService(); var authorized = true;
        var profile = Profile();
        using var model = new BrowserSetupViewModel(BrowserKind.Edge, "", service, () => [profile], () => authorized);
        await model.PrepareCommand.ExecuteAsync(null);
        Assert.True(model.ConnectionReady); Assert.Empty(service.Opened);
        Assert.Empty(service.Copied);
        authorized = false;
        BrowserProfileChoice? accepted = null; model.Completed += value => accepted = value;
        model.FinishCommand.Execute(null); model.OpenStartupCommand.Execute(null);
        Assert.Null(accepted); Assert.Empty(service.Opened); Assert.False(model.CanFinish);
    }

    [Fact]
    public async Task Closing_During_Preparation_Cancels_Work_And_Does_Not_Open_A_Browser()
    {
        var cancelled = false;
        var service = new SetupFixtureService { Prepare = async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { cancelled = true; throw; }
            throw new InvalidOperationException();
        } };
        var model = new BrowserSetupViewModel(BrowserKind.Edge, "", service, () => [], () => true);
        var preparation = model.PrepareCommand.ExecuteAsync(null);
        model.Dispose();
        await preparation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(cancelled); Assert.Empty(service.Opened); Assert.False(model.CanFinish);
    }

    internal sealed class SetupFixtureService : IBrowserSetupService
    {
        public List<string> Opened { get; } = [];
        public List<string> Copied { get; } = [];
        public List<string> Actions { get; } = [];
        public bool ClipboardBusy { get; set; }
        public void CopyPath(string path) { if (ClipboardBusy) throw new InvalidOperationException(); Copied.Add(path); Actions.Add("Copy"); }
        public Func<CancellationToken, Task<BrowserSetupPackage>>? Prepare { get; set; }
        public Task<BrowserSetupPackage> PrepareAsync(BrowserKind browser, CancellationToken token) => Prepare?.Invoke(token)
            ?? Task.FromResult(new BrowserSetupPackage(browser, browser == BrowserKind.Firefox ? BrowserExtensionInstall.Unavailable : BrowserExtensionInstall.Unpacked,
                BrowserSetupService.ExtensionsPage(browser), @"C:\Users\Operator\AppData\Local\Programs\AppSwitcherBrowserSetup\extensions\Edge\afmmehaldghpcccgholfklbeemokekoj"));
        public void Open(BrowserKind browser, string target) { Opened.Add(target); Actions.Add("Open"); }
    }
    private static (string Bundle, string Destination) Bundle(string root)
    {
        var bundle = Path.Combine(root, "bundle");
        foreach (var folder in new[] { "host", "chromium", "firefox" }) Directory.CreateDirectory(Path.Combine(bundle, folder));
        foreach (var file in BrowserSetupService.RequiredHostFiles) File.WriteAllText(Path.Combine(bundle, "host", file), "host fixture");
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        File.Copy(Path.Combine(repo, "browser-extension", "distribution.json"), Path.Combine(bundle, "distribution.json"));
        foreach (var browser in new[] { "chromium", "firefox" })
            File.Copy(Path.Combine(repo, "browser-extension", $"manifest.{browser}.json"), Path.Combine(bundle, browser, "manifest.json"));
        return (bundle, Path.Combine(root, "installed"));
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AppSwitcher-SetupTests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
