using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckCatalogFilterPreferencesAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("Catalog preference checks require an isolated smoke profile");
        var originalPreferences = preferences;
        var originalCatalog = catalog;
        var originalInstalled = installed;
        var originalPage = page;
        var originalExpanded = expandedSeries.ToArray();
        var stable = new PythonRuntime("pythoncore-3.14-64", "PythonCore", "3.14-64", "3.14.7", "Python 3.14.7", "", "", true, true);
        var previewRuntime = stable with { Id = "pythoncore-3.15-64", Tag = "3.15-64", Version = "3.15.0rc1", DisplayName = "Python 3.15.0rc1", IsDefault = false };
        var arm = stable with { Id = "pythoncore-3.14-arm64", Tag = "3.14-arm64", IsDefault = false };
        var embedded = stable with { Id = "pythonembed-3.14-64", Company = "PythonEmbed", DisplayName = "Python 3.14.7 (embeddable)", IsDefault = false };
        var embeddedPreview = previewRuntime with { Id = "pythonembed-3.15-arm64", Company = "PythonEmbed", Tag = "3.15-arm64", DisplayName = "Python 3.15.0rc1 (embeddable)" };
        ComboBox ChoiceNamed(string name) => Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T(name));
        static void Select(ComboBox box, string value) => box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == value);
        void RequirePersisted(string expectedArchitecture, string expectedType, bool expectedPreview)
        {
            var reloaded = new SettingsStore(store.DirectoryPath).Load();
            if (reloaded.DefaultArchitecture != expectedArchitecture || reloaded.CatalogPackageType != expectedType || reloaded.ShowPreviewReleases != expectedPreview)
                throw new IOException("Catalog UI changes were not persisted to the isolated profile");
        }
        try
        {
            installed = [stable, arm]; catalog = [stable, previewRuntime, arm, embedded, embeddedPreview];
            expandedSeries.Clear();
            foreach (var language in Strings.Languages)
            {
                SavePreferences(preferences with { Language = language, CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
                Navigate("settings"); Root.UpdateLayout(); RequireCatalogFiltersAbsentFromSettings();
                Navigate("catalog"); Root.UpdateLayout();
                var originalContent = PageHost.Children.Single();
                var architectures = ChoiceNamed("Architecture");
                var types = ChoiceNamed("Package type");
                var preview = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Show preview releases"));
                await WaitForSmokeConditionAsync(() => preview.IsLoaded && architectures.IsLoaded && types.IsLoaded, "Catalog filter controls did not load");
                if (VisibleRuntimeCount != 1 || preview.IsOn) throw new IOException("Catalog default filter did not hide previews and special packages");
                Select(architectures, "All architectures"); Root.UpdateLayout();
                if (VisibleRuntimeCount != 2) throw new IOException("All architectures did not include both stable architectures");
                Select(types, "All"); Root.UpdateLayout();
                if (VisibleRuntimeCount != 3) throw new IOException("All package types did not include stable specialized packages");
                if (!preview.Focus(FocusState.Keyboard)) throw new IOException("Preview switch could not receive keyboard focus");
                preview.IsOn = true;
                await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 5, "Preview switch did not refresh the catalog immediately");
                var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
                if (!ReferenceEquals(PageHost.Children.Single(), originalContent) || !ReferenceEquals(ChoiceNamed("Architecture"), architectures) ||
                    !ReferenceEquals(ChoiceNamed("Package type"), types) || !preview.IsLoaded ||
                    !ReferenceEquals(focused, preview) && !Descendants(preview).Any(element => ReferenceEquals(element, focused)))
                    throw new IOException("Changing a catalog filter rebuilt controls or lost keyboard focus");
                RequirePersisted("All architectures", "All", true);

                // Reload a fresh store instance, then leave and return through My Python's separate filter.
                preferences = new SettingsStore(store.DirectoryPath).Load();
                Navigate("runtimes"); Root.UpdateLayout();
                Select(ChoiceNamed("Architecture"), "x86"); Root.UpdateLayout();
                if (VisibleRuntimeCount != 0) throw new IOException("My Python architecture filter did not apply");
                RequirePersisted("All architectures", "All", true);
                Navigate("catalog"); Root.UpdateLayout();
                preview = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Show preview releases"));
                if (architecture != "All architectures" || distributionFilter != "All" || !preview.IsOn || VisibleRuntimeCount != 5 ||
                    ((ComboBoxItem)ChoiceNamed("Architecture").SelectedItem).Tag as string != "All architectures" ||
                    ((ComboBoxItem)ChoiceNamed("Package type").SelectedItem).Tag as string != "All")
                    throw new IOException("Catalog filters were lost across navigation or profile reload");
                Select(ChoiceNamed("Package type"), "Embedded");
                Select(ChoiceNamed("Architecture"), "ARM64"); Root.UpdateLayout();
                if (VisibleRuntimeCount != 1) throw new IOException("Architecture, package type and preview filters did not combine");
                preview.IsOn = false; Root.UpdateLayout();
                if (VisibleRuntimeCount != 0) throw new IOException("Disabling previews did not remove the selected preview package");
                RequirePersisted("ARM64", "Embedded", false);
                preferences = new SettingsStore(store.DirectoryPath).Load();
                Navigate("settings"); Root.UpdateLayout(); RequireCatalogFiltersAbsentFromSettings();
                Navigate("catalog"); Root.UpdateLayout();
                if (architecture != "ARM64" || distributionFilter != "Embedded" || VisibleRuntimeCount != 0)
                    throw new IOException("A specific package-type choice was not restored from the profile");
                if (language == "zh-CN") await CaptureAsync(Path.Combine(directory, "25-persistent-catalog-filters.png"));
            }
        }
        finally
        {
            installed = originalInstalled; catalog = originalCatalog;
            expandedSeries.Clear(); foreach (var series in originalExpanded) expandedSeries.Add(series);
            SavePreferences(originalPreferences); Navigate(originalPage); Root.UpdateLayout();
        }
    }

    private void RequireCatalogFiltersAbsentFromSettings()
    {
        foreach (var moved in new[] { "Default architecture", "Show preview releases", "Show specialized packages" })
            if (Descendants(PageHost).OfType<Control>().Any(control => AutomationProperties.GetName(control) == T(moved)) ||
                Descendants(PageHost).OfType<TextBlock>().Any(label => label.Text == T(moved)))
                throw new IOException("Catalog filter remains duplicated in Settings: " + moved);
    }
}
