using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private BuildStore Builds => new(store.DirectoryPath);
    private IReadOnlyList<PythonRuntime> localRuntimes = [];
    private BuildOptions buildOptions = new();
    private string buildBootstrap = "";
    private BuildToolchain? buildTools;
    private IReadOnlyList<BuildToolchain> buildToolChoices = [];
    private string buildPreset = "Standard";
    private string[] buildVersions = [BuildRecipe.Version];
    private ComboBox? buildVersionPicker;
    private TextBlock? buildSourceLabel;
    private BuildRecord? currentBuild;
    private bool buildingPython;
    private TextBlock? buildLiveText;
    private ScrollViewer? buildScroll;
    private double buildOffset;
    private readonly Queue<string> buildTail = new();
    private readonly object buildTailGate = new();
    private int buildTailQueued;

    private IReadOnlyList<PythonRuntime> MergeLocal(IEnumerable<PythonRuntime> versions) => versions.Where(r => !r.IsLocalBuild &&
        !localRuntimes.Any(l => string.Equals(r.Executable, l.Executable, StringComparison.OrdinalIgnoreCase))).Concat(localRuntimes).ToArray();
    private async Task<IReadOnlyList<PythonRuntime>> ListInstalledAsync() => MergeLocal(connected ? await client.ListAsync() : []);
    private void ReloadLocalRuntimes()
    {
        localRuntimes = Builds.Runtimes(); installed = MergeLocal(installed);
    }

    private UIElement BuildPythonPage()
    {
        var layout = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        var start = palette.Action("Build runtime", "\uE90F", true); start.IsEnabled = !busy && buildTools is not null;
        start.Click += async (_, _) => await StartPythonBuildAsync();
        At(layout, Header("ADVANCED", "Build Python", "Build your own CPython runtime from official source", start), 0);
        var body = new StackPanel { Spacing = palette.Tokens.SectionSpacing };
        buildScroll = PageScroll(body);
        var scroll = buildScroll; scroll.Loaded += (_, _) => scroll.ChangeView(null, buildOffset, null, true);
        At(layout, scroll, 1);
        var profile = palette.Section("Build configuration");
        profile.Spacing = 12;
        var version = new ComboBox { IsEditable = true, ItemsSource = buildVersions.Append(buildOptions.Version).Distinct().ToArray(),
            SelectedItem = buildOptions.Version, Text = buildOptions.Version,
            MinWidth = 210, MinHeight = palette.Tokens.ControlHeight, Padding = new(12, 4, 32, 4),
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = !busy, FontSize = palette.Tokens.ControlFontSize };
        palette.ApplySurfaceResources(version);
        buildVersionPicker = version;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(version, T("Python version"));
        void SelectVersion()
        {
            try
            {
                var changed = buildOptions with { Version = version.Text.Trim() }; changed.Validate(); buildOptions = changed;
                if (buildSourceLabel is not null && changed.SourceArchive.Length == 0) buildSourceLabel.Text = BuildSources.Url(changed.Version);
            }
            catch (Exception ex) { version.Text = buildOptions.Version; ShowError(ex); }
        }
        version.LostFocus += (_, _) => SelectVersion();
        version.SelectionChanged += (_, _) => { if (version.SelectedItem is string selected) { version.Text = selected; SelectVersion(); } };
        var fields = new List<FrameworkElement> { BuildField("Python version", version) };
        ToolTipService.SetToolTip(version, T("Choose a release or enter an exact version, including previews"));
        var refresh = palette.Action("Refresh source versions", "\uE72C"); refresh.IsEnabled = !busy;
        refresh.Click += async (_, _) =>
        {
            if (busy) return;
            SetBusy(true);
            try { buildVersions = await BuildSources.ListAsync(preferences.Network, ProxyPassword()); }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        };
        fields.Add(BuildField("Build profile", Choice([("Standard", "Standard"), ("Performance", "Performance"), ("Debug", "Debug"), ("Minimal", "Minimal"), ("Custom", "Custom")], buildPreset,
            value => { buildPreset = value; buildOptions = BuildOptions.Preset(value, buildOptions); RenderPage(); }, "Build profile")));
        fields.Add(BuildField("Build type", Choice([("Release", "Release"), ("Debug", "Debug")], buildOptions.Configuration,
            value => { buildOptions = buildOptions with { Configuration = value, Pgo = value == "Release" && buildOptions.Pgo }; buildPreset = "Custom"; RenderPage(); }, "Build type")));
        profile.Children.Add(BuildCompactGrid(fields, 230));
        profile.Children.Add(Toolbar(refresh, palette.Chip("x64")));
        profile.Children.Add(palette.Label("Release uses CPython's link-time optimization; Debug requires the Visual C++ debug runtime", 12, muted: true));
        var components = new List<FrameworkElement>();
        foreach (var (label, value, change) in new (string, bool, Action<bool>)[] {
            ("Profile-guided optimization", buildOptions.Pgo, v => buildOptions = buildOptions with { Pgo = v, Configuration = v ? "Release" : buildOptions.Configuration }),
            ("pip", buildOptions.IncludePip, v => buildOptions = buildOptions with { IncludePip = v, IncludeSsl = v || buildOptions.IncludeSsl }),
            ("SSL", buildOptions.IncludeSsl, v => buildOptions = buildOptions with { IncludeSsl = v, IncludePip = v && buildOptions.IncludePip }),
            ("SQLite", buildOptions.IncludeSqlite, v => buildOptions = buildOptions with { IncludeSqlite = v }),
            ("ctypes", buildOptions.IncludeCtypes, v => buildOptions = buildOptions with { IncludeCtypes = v }),
            ("Tcl/Tk and IDLE", buildOptions.IncludeTk, v => buildOptions = buildOptions with { IncludeTk = v }),
            ("Test suite", buildOptions.IncludeTests, v => buildOptions = buildOptions with { IncludeTests = v }),
            ("Debug symbols", buildOptions.IncludeSymbols, v => buildOptions = buildOptions with { IncludeSymbols = v }) })
        {
            var toggle = Toggle(value, v => { change(v); buildPreset = "Custom"; RenderPage(); }, label); toggle.IsEnabled = !busy;
            var item = new Grid { ColumnSpacing = 12, MinHeight = 44 };
            item.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            item.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = palette.Label(label, palette.Tokens.ControlFontSize);
            name.VerticalAlignment = VerticalAlignment.Center; item.Children.Add(name);
            Grid.SetColumn(toggle, 1); toggle.VerticalAlignment = VerticalAlignment.Center; item.Children.Add(toggle);
            components.Add(item);
        }
        profile.Children.Add(BuildCompactGrid(components, 330));
        body.Children.Add(profile);
        body.Children.Add(BuildSourceSection());
        var dependencies = palette.Section("Build tools");
        var candidates = installed.Where(r => !r.IsEmbeddable && !r.IsPrerelease && !r.IsFreeThreaded && File.Exists(r.Executable)).ToArray();
        if (buildBootstrap.Length == 0) buildBootstrap = candidates.FirstOrDefault()?.Executable ?? "";
        var choices = candidates.Select(r => (r.Executable, RuntimeTitle(r) + " · " + r.Architecture)).DistinctBy(r => r.Executable).ToList();
        if (buildBootstrap.Length > 0 && !choices.Any(c => c.Executable == buildBootstrap)) choices.Add((buildBootstrap, Path.GetFileName(Path.GetDirectoryName(buildBootstrap)) ?? "Python"));
        if (choices.Count > 0)
            dependencies.Children.Add(SettingRow("Python for build tools", "An existing Python 3.10 or newer", Choice(choices.ToArray(), buildBootstrap,
                value => { buildBootstrap = value; buildTools = null; RenderPage(); }, "Python for build tools")));
        var browse = palette.Action("Choose Python…", "\uE8B7"); browse.IsEnabled = !busy;
        browse.Click += async (_, _) =>
        {
            if (busy || confirmationOpen) return; confirmationOpen = true;
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".exe");
                if (await picker.PickSingleFileAsync() is { } file) { buildBootstrap = BuildRecipe.BatchPath(file.Path); buildTools = null; }
            }
            catch (Exception ex) { ShowError(ex); }
            finally { confirmationOpen = false; RenderPage(); }
        };
        var check = palette.Action("Check build tools", "\uE73E"); check.IsEnabled = !busy && buildBootstrap.Length > 0;
        check.Click += async (_, _) => await CheckBuildToolsAsync();
        dependencies.Children.Add(Toolbar(browse, check));
        if (buildBootstrap.Length > 0) { var path = palette.Label(buildBootstrap, 12, muted: true); path.IsTextSelectionEnabled = true; dependencies.Children.Add(path); }
        if (buildTools is { } tools)
        {
            if (buildToolChoices.Count > 1)
                dependencies.Children.Add(SettingRow("Compiler and SDK", null, Choice(buildToolChoices.Select((t, i) => (i.ToString(), "MSVC " + t.CompilerVersion + " · " + t.SdkVersion + " · " + Path.GetFileName(t.VisualStudio))).ToArray(),
                    buildToolChoices.ToList().IndexOf(tools).ToString(), value => { buildTools = buildToolChoices[int.Parse(value)]; RenderPage(); }, "Compiler and SDK")));
            dependencies.Children.Add(palette.Label("MSVC " + tools.CompilerVersion + " · " + tools.PlatformToolset + " · Windows SDK " + tools.SdkVersion, 14));
            dependencies.Children.Add(palette.Label(tools.VisualStudio, 12, muted: true));
            if (tools.PlatformToolset == "v145") dependencies.Children.Add(palette.Label("v145 differs from the compiler used for official CPython builds", 12, muted: true));
            var command = palette.Label("PCbuild\\build.bat " + BuildRecipe.CompilerArguments(buildOptions, tools) + (buildOptions.Pgo ? "\nPCbuild\\amd64\\instrumented\\python.exe " + string.Join(' ', BuildRecipe.TrainingArguments(Builds.Root)) + "\nPCbuild\\build.bat " + BuildRecipe.CompilerArguments(buildOptions, tools, "PGUpdate") : ""), 12, muted: true);
            command.IsTextSelectionEnabled = true;
            dependencies.Children.Add(new Expander { Header = T("Build command"), Content = command, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        else dependencies.Children.Add(palette.Label("Requires Visual Studio C++ desktop tools and a Windows SDK", 12, muted: true));
        var help = palette.Action("Build tools guide", "\uE8A7");
        help.Click += (_, _) => OpenUrl("https://learn.microsoft.com/cpp/build/vscpp-step-0-installation"); dependencies.Children.Add(help);
        body.Children.Add(dependencies);
        var output = palette.Section("Output");
        output.Children.Add(palette.Label(buildOptions.OutputParent.Length == 0 ? Path.Combine(Builds.Root, "Runtimes") : buildOptions.OutputParent, 12, muted: true));
        var chooseOutput = palette.Action("Choose output folder…", "\uE8B7"); chooseOutput.IsEnabled = !busy;
        chooseOutput.Click += async (_, _) => await ChooseBuildOutputAsync();
        var resetOutput = palette.Action("Use default folder", "\uE777"); resetOutput.IsEnabled = !busy && buildOptions.OutputParent.Length > 0;
        resetOutput.Click += (_, _) => { buildOptions = buildOptions with { OutputParent = "" }; RenderPage(); };
        output.Children.Add(Toolbar(chooseOutput, resetOutput));
        output.Children.Add(palette.Label("Each build gets its own folder. Existing runtimes stay untouched", 12, muted: true));
        body.Children.Add(output);
        if (currentBuild is { InProgress: true } || buildingPython)
        {
            var live = palette.Section("Build output");
            buildLiveText = palette.Label("", 12, muted: true); buildLiveText.IsTextSelectionEnabled = true;
            lock (buildTailGate) buildLiveText.Text = string.Join('\n', buildTail);
            live.Children.Add(buildLiveText); body.Children.Add(live);
        }
        else buildLiveText = null;
        var history = palette.Section("Build history"); body.Children.Add(history);
        try
        {
            var entries = Builds.Read();
            if (entries.Count == 0) history.Children.Add(palette.Label("No builds yet", 14, muted: true));
            foreach (var entry in entries.Take(30))
            {
                var details = new StackPanel { Spacing = 8 };
                details.Children.Add(palette.Label("CPython " + entry.Options.Version + " · x64 " + entry.Options.Configuration + (entry.Options.Pgo ? " · PGO" : "") + " · " + entry.Id[..8], 14, true));
                details.Children.Add(palette.Label(T(BuildStateLabel(entry.State)) + " · " + entry.Started.ToLocalTime().ToString("g"), 12, muted: true));
                if (entry.Error.Length > 0) details.Children.Add(palette.Label(entry.Error, 12));
                var log = palette.Action("Open build folder", "\uE8B7"); log.Click += (_, _) => OpenFolder(new("", "", "", "", "", "", Builds.JobDirectory(entry.Id), false));
                var actions = Toolbar(log);
                if (!entry.InProgress)
                {
                    var retry = palette.Action("Use these options", "\uE72C"); retry.IsEnabled = !busy;
                    retry.Click += (_, _) => { buildOptions = entry.Options; buildPreset = "Custom"; buildOffset = 0; RenderPage(); }; actions.Children.Add(retry);
                }
                var provenance = palette.Label(T(entry.SourceUrl) + "\n" + T(entry.SourceVerification) + "\nSHA-256: " + entry.SourceSha256, 12, muted: true);
                provenance.IsTextSelectionEnabled = true;
                details.Children.Add(new Expander { Header = T("Source details"), Content = provenance, HorizontalAlignment = HorizontalAlignment.Stretch });
                details.Children.Add(actions); history.Children.Add(palette.CardBox(details, 16));
            }
        }
        catch (Exception ex) { history.Children.Add(palette.Label(SensitiveText.Redact(ex.Message), 12)); }
        return layout;
    }

    private static string BuildStateLabel(BuildState state) => state switch
    {
        BuildState.Ready => "Runtime ready", BuildState.Training => "Training PGO", BuildState.Failed => "Build failed", BuildState.Cancelled => "Build stopped",
        BuildState.Interrupted => "Build interrupted", BuildState.Removed => "Removed from list", BuildState.Compiling => "Compiling CPython",
        BuildState.Assembling => "Assembling runtime", BuildState.Validating => "Testing runtime", BuildState.Downloading => "Downloading",
        BuildState.Extracting => "Extracting files", _ => "Preparing"
    };
    private async Task CheckBuildToolsAsync()
    {
        if (busy || confirmationOpen) return;
        confirmationOpen = true;
        try
        {
            SetBusy(true, "Checking build tools"); buildTools = null;
            buildToolChoices = await BuildToolchain.DetectAllAsync(buildBootstrap); buildTools = buildToolChoices[0];
            Notify("Build tools ready", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; SetBusy(false); }
    }
    private async Task StartPythonBuildAsync()
    {
        if (busy || confirmationOpen || buildTools is null) return;
        try
        {
            var selected = buildOptions with { Version = buildVersionPicker?.Text.Trim() ?? buildOptions.Version };
            selected.Validate(); buildOptions = selected;
        }
        catch (Exception ex) { ShowError(ex); return; }
        var tools = buildTools; var options = buildOptions;
        if (options.SourceArchive.Length > 0)
        {
            confirmationOpen = true;
            try { if (await Dialog("Build local source", T("Building runs scripts from this archive. Continue only if you trust its source") + "\n\n" + options.SourceArchive, "Build runtime").ShowAsync() != ContentDialogResult.Primary) return; }
            finally { confirmationOpen = false; }
        }
        lock (buildTailGate) buildTail.Clear();
        buildingPython = true;
        var operation = BeginOperation(T("Building CPython {0}", options.Version), download: true);
        SetBusy(true, "Compiling CPython");
        try
        {
            var builder = new CPythonBuilder(Builds, preferences.Network, ProxyPassword());
            // File extraction and persistence must not occupy the UI dispatcher.
            var result = await Task.Run(() => builder.BuildAsync(options, tools, operation, record => DispatcherQueue.TryEnqueue(() =>
            {
                currentBuild = record;
                if (page == "build") RenderPage();
            }), line =>
            {
                lock (buildTailGate) { buildTail.Enqueue(line.Length > 240 ? line[..240] + "…" : line); while (buildTail.Count > 8) buildTail.Dequeue(); }
                if (Interlocked.Exchange(ref buildTailQueued, 1) == 0) DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref buildTailQueued, 0);
                    if (page == "build" && buildLiveText is not null) lock (buildTailGate) buildLiveText.Text = string.Join('\n', buildTail);
                });
            }));
            currentBuild = result; ReloadLocalRuntimes();
            Notify(BuildStateLabel(result.State), result.State == BuildState.Ready ? InfoBarSeverity.Success : result.State == BuildState.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Warning);
            Log("Build " + result.Id + ": " + result.State + ". " + result.Error);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { buildingPython = false; FinishOperation(operation); SetBusy(false); }
    }
    private async Task RemoveLocalRuntimeAsync(PythonRuntime runtime)
    {
        if (busy || confirmationOpen || runtime.LocalBuildId is null) return;
        confirmationOpen = true;
        try
        {
            if (await Dialog("Remove from list", T("Files on disk will be kept") + "\n\n" + runtime.Prefix, "Remove").ShowAsync() != ContentDialogResult.Primary) return;
            Builds.Remove(runtime.LocalBuildId); ReloadLocalRuntimes();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; RenderPage(); }
    }
}
