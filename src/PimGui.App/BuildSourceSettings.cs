using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private StackPanel BuildSourceSection()
    {
        var section = palette.Section("Source");
        buildSourceLabel = palette.Label(buildOptions.SourceArchive.Length == 0 ? BuildSources.Url(buildOptions.Version) : buildOptions.SourceArchive, 12, muted: true);
        section.Children.Add(buildSourceLabel);
        section.Children.Add(palette.Label("Import a Python-version.tgz source archive; its version must match your selection", 12, muted: true));
        var import = palette.Action("Import source archive…", "\uE8B7"); import.IsEnabled = !busy;
        import.Click += async (_, _) =>
        {
            if (busy || confirmationOpen) return; confirmationOpen = true;
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".tgz"); picker.FileTypeFilter.Add(".gz");
                if (await picker.PickSingleFileAsync() is { } file)
                {
                    var name = System.Text.RegularExpressions.Regex.Match(file.Name, @"^Python-([23]\.\d{1,2}\.\d{1,3}(?:(?:a|b|rc)\d{1,2})?)\.(?:tgz|tar\.gz)$");
                    var changed = buildOptions with { SourceArchive = BuildRecipe.BatchPath(file.Path), Version = name.Success ? name.Groups[1].Value : buildOptions.Version };
                    changed.Validate(); buildOptions = changed;
                }
            }
            catch (Exception ex) { ShowError(ex); }
            finally { confirmationOpen = false; RenderPage(); }
        };
        var official = palette.Action("Use official source", "\uE777"); official.IsEnabled = !busy && buildOptions.SourceArchive.Length > 0;
        official.Click += (_, _) => { buildOptions = buildOptions with { SourceArchive = "" }; RenderPage(); };
        section.Children.Add(Toolbar(import, official));
        section.Children.Add(palette.Label("Compatibility depends on the selected source and installed compiler; older releases may need another build adapter", 12, muted: true));
        return section;
    }
    private async Task ChooseBuildOutputAsync()
    {
        if (busy || confirmationOpen) return; confirmationOpen = true;
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFolderAsync() is { } folder)
                buildOptions = buildOptions with { OutputParent = BuildRecipe.BatchPath(folder.Path) };
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; RenderPage(); }
    }
}
