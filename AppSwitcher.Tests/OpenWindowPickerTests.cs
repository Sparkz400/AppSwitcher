namespace AppSwitcher.Tests;

public class OpenWindowPickerTests
{
    private static OpenWindowCandidate Candidate => new((nint)42, 123, 456, @"C:\Apps\panel.exe", "Document — Operator panel", "panel", OpenWindowKind.Native);

    [Theory]
    [InlineData(@"C:\Apps\panel.exe", (int)OpenWindowKind.Native, null)]
    [InlineData(@"C:\Apps\notepad++.exe", (int)OpenWindowKind.Native, null)]
    [InlineData(@"C:\Chrome\chrome.exe", (int)OpenWindowKind.Website, BrowserKind.Chrome)]
    [InlineData(@"C:\Edge\MSEDGE.EXE", (int)OpenWindowKind.Website, BrowserKind.Edge)]
    [InlineData(@"C:\Firefox\firefox.exe", (int)OpenWindowKind.Website, BrowserKind.Firefox)]
    [InlineData(@"C:\Windows\ApplicationFrameHost.exe", (int)OpenWindowKind.Unsupported, null)]
    [InlineData(@"C:\Program Files\WindowsApps\package\app.exe", (int)OpenWindowKind.Unsupported, null)]
    [InlineData(@"C:\Runtime\javaw.exe", (int)OpenWindowKind.Unsupported, null)]
    [InlineData(@"C:\Runtime\dotnet.exe", (int)OpenWindowKind.Unsupported, null)]
    [InlineData(@"C:\Apps\brave.exe", (int)OpenWindowKind.Unsupported, null)]
    [InlineData("panel.exe", (int)OpenWindowKind.Unavailable, null)]
    [InlineData("", (int)OpenWindowKind.Unavailable, null)]
    public void Classifies_Applications_Without_Importing_Browsers_Or_Runtime_Hosts(string path, int expected, BrowserKind? browser)
    {
        Assert.Equal(((OpenWindowKind)expected, browser), OpenWindowCatalog.Classify(path));
    }

    [Fact]
    public void Revalidation_Requires_Window_Process_Birth_And_Exact_Path_But_Allows_Title_Changes()
    {
        var original = Candidate;
        Assert.True(OpenWindowCatalog.SameIdentity(original, original with { Title = "Another document", ExecutablePath = @"c:\apps\PANEL.exe" }));
        foreach (var changed in new[] { original with { Handle = 43 }, original with { ProcessId = 124 },
            original with { ProcessCreated = 999 }, original with { ProcessCreated = 0 },
            original with { ExecutablePath = @"D:\Other\panel.exe" }, original with { Kind = OpenWindowKind.Unavailable } })
            Assert.False(OpenWindowCatalog.SameIdentity(original, changed));
        Assert.False(OpenWindowCatalog.SameExecutable("panel.exe", original.ExecutablePath));
    }

    [Fact]
    public async Task Filter_And_Refresh_Preserve_A_Reviewed_Name_Only_For_The_Same_Window()
    {
        var catalog = new Catalog { Windows = [Candidate] };
        using var model = new OpenWindowPickerViewModel(catalog, () => true, () => []);
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = Assert.Single(model.Windows);
        model.Name = "Production";
        model.Search = "panel document";
        Assert.Equal("Production", model.Name);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Production", model.Name);
        model.Search = "absent";
        Assert.Empty(model.Windows);
        Assert.Null(model.SelectedWindow);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_Closed_Or_Reused_Window_Cannot_Be_Imported()
    {
        var catalog = new Catalog { Windows = [Candidate], Revalidate = (_, _) => Task.FromResult<OpenWindowCandidate?>(Candidate with { ProcessCreated = 999 }) };
        using var model = new OpenWindowPickerViewModel(catalog, () => true, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(result);
        Assert.Equal(LocalizationService.T("WindowPicker_Changed"), model.Status);
        catalog.Revalidate = (_, _) => Task.FromResult<OpenWindowCandidate?>(null);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(result);
    }

    [Fact]
    public async Task Access_Is_Rechecked_After_The_Native_Read()
    {
        var gate = new TaskCompletionSource<OpenWindowCandidate?>();
        var allowed = true;
        var catalog = new Catalog { Windows = [Candidate], Revalidate = (_, _) => gate.Task };
        using var model = new OpenWindowPickerViewModel(catalog, () => allowed, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        var confirming = model.ConfirmCommand.ExecuteAsync(null);
        allowed = false;
        gate.SetResult(Candidate);
        await confirming;
        Assert.Null(result);
        Assert.False(model.ManualCommand.CanExecute(null));
        Assert.Equal(LocalizationService.T("WindowPicker_AccessExpired"), model.Status);
    }

    [Fact]
    public async Task Closing_The_Picker_Discards_Late_Results()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<OpenWindowCandidate>>();
        var catalog = new Catalog { Scan = _ => gate.Task };
        var model = new OpenWindowPickerViewModel(catalog, () => true, () => []);
        var loading = model.RefreshCommand.ExecuteAsync(null);
        model.Dispose();
        gate.SetResult([Candidate]);
        await loading;
        Assert.Empty(model.Windows);
        Assert.False(model.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_Changed_Selection_Cannot_Accept_The_Previous_Window()
    {
        var gate = new TaskCompletionSource<OpenWindowCandidate?>();
        var other = Candidate with { Handle = 88, ProcessId = 999 };
        using var model = new OpenWindowPickerViewModel(new Catalog { Windows = [Candidate, other], Revalidate = (_, _) => gate.Task }, () => true, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        var confirming = model.ConfirmCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[1];
        gate.SetResult(Candidate);
        await confirming;
        Assert.Null(result);
    }

    [Fact]
    public async Task Closing_During_Acceptance_Cannot_Complete_An_Import()
    {
        var gate = new TaskCompletionSource<OpenWindowCandidate?>();
        var model = new OpenWindowPickerViewModel(new Catalog { Windows = [Candidate], Revalidate = (_, _) => gate.Task }, () => true, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        var confirming = model.ConfirmCommand.ExecuteAsync(null);
        model.Dispose();
        gate.SetResult(Candidate);
        await confirming;
        Assert.Null(result);
        Assert.Null(model.SelectedWindow);
    }

    [Fact]
    public async Task Duplicates_Are_Rechecked_At_Acceptance()
    {
        var apps = new List<AppItem>();
        var catalog = new Catalog { Windows = [Candidate] };
        using var model = new OpenWindowPickerViewModel(catalog, () => true, () => apps);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        var existing = new AppItem { Name = "Panel", Path = Candidate.ExecutablePath };
        apps.Add(existing);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(WindowPickAction.Existing, result?.Action);
        Assert.Equal(existing.AppId, result?.ExistingAppId);
        result = null;
        apps.Add(new AppItem { Name = "Other profile", Path = Candidate.ExecutablePath, Args = "--profile b" });
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(result);
        Assert.Equal(LocalizationService.T("WindowPicker_MultipleExisting"), model.Status);
    }

    [Fact]
    public async Task Browser_Selection_Only_Offers_Website_Setup()
    {
        var website = Candidate with { ExecutablePath = @"C:\Apps\chrome.exe", Kind = OpenWindowKind.Website, Browser = BrowserKind.Chrome };
        using var model = new OpenWindowPickerViewModel(new Catalog { Windows = [website] }, () => true, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = model.Windows[0];
        Assert.False(model.CanName);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(WindowPickAction.Website, result?.Action);
    }

    [Fact]
    public async Task Unreadable_Applications_Stay_Visible_With_A_Manual_Fallback()
    {
        using var model = new OpenWindowPickerViewModel(new Catalog { Windows = [Candidate with { Kind = OpenWindowKind.Unavailable }] }, () => true, () => []);
        OpenWindowPick? result = null;
        model.Completed += pick => result = pick;
        await model.RefreshCommand.ExecuteAsync(null);
        model.SelectedWindow = Assert.Single(model.Windows);
        Assert.False(model.CanConfirm);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(result);
        model.ManualCommand.Execute(null);
        Assert.Equal(WindowPickAction.Manual, result?.Action);
    }

    private sealed class Catalog : IOpenWindowCatalog
    {
        public IReadOnlyList<OpenWindowCandidate> Windows { get; set; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<OpenWindowCandidate>>>? Scan { get; set; }
        public Func<OpenWindowCandidate, CancellationToken, Task<OpenWindowCandidate?>> Revalidate { get; set; } = (candidate, _) => Task.FromResult<OpenWindowCandidate?>(candidate);
        public Task<IReadOnlyList<OpenWindowCandidate>> ScanAsync(CancellationToken token) => Scan?.Invoke(token) ?? Task.FromResult(Windows);
        public Task<OpenWindowCandidate?> RevalidateAsync(OpenWindowCandidate candidate, CancellationToken token) => Revalidate(candidate, token);
    }
}
