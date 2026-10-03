using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using Windows.UI;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckSearchControlsAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("Isolated search test profile required");
        var originalPreferences = preferences;
        var originalCatalog = catalog;
        var originalInstalled = installed;
        var originalPage = page;
        var originalSearch = search;
        try
        {
            catalog = Enumerable.Range(0, 30).Select(i => new PythonRuntime("search-fixture-" + i,
                "PythonCore", "3.14-64", "3.14." + i, "Python 3.14." + i, "", "", false)).ToArray();
            installed = [];
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var context = $"Search/{design}/{theme}";
                ApplySmokePreferences(preferences with
                {
                    Design = design, Theme = theme, Language = "zh-CN", Transparency = "Off",
                    CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard",
                    ShowPreviewReleases = false, MaterialColorSource = "Custom", MaterialSeed = 0xFF287C60u
                });
                Navigate("catalog"); Root.UpdateLayout();
                var pageContent = PageHost.Children.Single();
                var snapshot = runtimeSnapshot;
                var control = Descendants(PageHost).OfType<AutoSuggestBox>().Single();
                control.ApplyTemplate(); Root.UpdateLayout();
                var editor = Descendants(control).OfType<TextBox>().Single(box => box.Name == "TextBox");
                editor.ApplyTemplate(); Root.UpdateLayout();
                var border = Descendants(editor).OfType<Border>().Single(part => part.Name == "BorderElement");
                var placeholder = Descendants(editor).OfType<ContentControl>().Single(part => part.Name == "PlaceholderTextContentPresenter");
                var viewport = Descendants(editor).OfType<ScrollViewer>().Single(part => part.Name == "ContentElement");
                var query = Descendants(editor).OfType<Button>().Single(part => part.Name == "QueryButton");
                var clear = Descendants(editor).OfType<Button>().Single(part => part.Name == "DeleteButton");
                var dictionary = Application.Current.Resources.MergedDictionaries.Single(item => item.ContainsKey("PyDeckDesignDictionary"));
                void Require(bool condition, string detail)
                {
                    if (!condition) throw new IOException(context + ": " + detail);
                }
                void RequireColor(Brush brush, Color expected, string detail) => Require(
                    brush is SolidColorBrush solid && solid.Color == expected,
                    detail + $"; expected {expected}, actual {(brush is SolidColorBrush actual ? actual.Color.ToString() : brush?.GetType().Name)}");
                void RequireStableEditor() => Require(ReferenceEquals(pageContent, PageHost.Children.Single()) &&
                    ReferenceEquals(snapshot, runtimeSnapshot) && control.IsLoaded && editor.IsLoaded &&
                    ReferenceEquals(control, Descendants(PageHost).OfType<AutoSuggestBox>().Single()) &&
                    ReferenceEquals(editor, Descendants(control).OfType<TextBox>().Single(box => box.Name == "TextBox")),
                    "Filtering replaced the page, search editor or catalog index");

                Require(editor.Style is not null && control.TextBoxStyle is not null && query.Style is not null && clear.Style is not null,
                    "Native AutoSuggestBox editor/helper styles are missing");
                Require(query.Tag as string != "ActionButton" && clear.Tag as string != "ActionButton",
                    "Application action styling replaced a native search helper");
                Require(editor.DesiredCandidateWindowAlignment.ToString() == "BottomEdge", "Native IME candidate alignment changed");
                Require(editor.TextAlignment == TextAlignment.Left && editor.HorizontalContentAlignment == HorizontalAlignment.Stretch &&
                    viewport.HorizontalAlignment == HorizontalAlignment.Stretch && placeholder.HorizontalContentAlignment == HorizontalAlignment.Stretch,
                    $"Text/placeholder viewport is not left aligned ({editor.TextAlignment}/{editor.HorizontalContentAlignment}/{viewport.HorizontalAlignment}/{placeholder.HorizontalContentAlignment})");
                Require(border.CornerRadius == new CornerRadius(palette.Tokens.InputRadius),
                    $"Rendered editor corner radius is {border.CornerRadius}, expected {palette.Tokens.InputRadius}");

                // Move keyboard focus out of the search before examining its resting state.
                Descendants(PageHost).OfType<ToggleSwitch>().First().Focus(FocusState.Programmatic);
                Require(VisualStateManager.GoToState(editor, "Normal", false), "Native normal input state is missing");
                await Task.Delay(35); Root.UpdateLayout();
                var restingFill = border.Background;
                if (design == "Fluent")
                {
                    Require(!dictionary.Keys.OfType<string>().Any(key => key.StartsWith("TextControl", StringComparison.Ordinal)) &&
                        !control.Resources.Keys.OfType<string>().Any(key => key.StartsWith("TextControl", StringComparison.Ordinal)),
                        "Fluent native text fill/focus resources were overridden");
                    if (!palette.Tokens.HighContrast)
                        Require(restingFill is SolidColorBrush fill && (fill.Color.A < 255 || fill.Opacity < 1),
                            "Fluent resting input lost its native translucent fill");
                }
                else
                {
                    Require(border.CornerRadius == new CornerRadius(12), "Material search corners are not 12 dip");
                    RequireColor(restingFill, palette.Tokens.ControlFill, "Material resting fill is not the paired Monet container");
                    RequireColor(editor.Foreground, palette.Tokens.Text, "Material text does not use Monet on-surface");
                    Require(border.BorderThickness == new Thickness(palette.Tokens.HighContrast ? 1 : 0),
                        "Material search retained a decorative resting outline");
                }
                await CaptureAsync(Path.Combine(directory, $"search-{design}-{theme}-normal.png"));

                Require(VisualStateManager.GoToState(editor, "PointerOver", false), "Native hover input state is missing");
                await Task.Delay(35); Root.UpdateLayout();
                if (design == "Material") RequireColor(border.Background,
                    DesignComponents.Mix(palette.Tokens.ControlFill, palette.Tokens.Text, palette.Tokens.HoverStateOpacity),
                    "Material hover fill lacks its state layer");
                control.IsEnabled = false; await Task.Delay(35); Root.UpdateLayout();
                Require(!editor.IsEnabled && !query.IsEnabled && !clear.IsEnabled, "Disabled search left a native part enabled");
                if (design == "Material") RequireColor(border.Background,
                    DesignComponents.Mix(palette.Tokens.Surface, palette.Tokens.Text, .04), "Material disabled fill is incorrect");
                control.IsEnabled = true;
                Require(editor.Focus(FocusState.Keyboard), "Native editor could not receive keyboard focus");
                await Task.Delay(35); Root.UpdateLayout();
                Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), editor), "Search focus did not reach the native editor");
                if (design == "Material")
                {
                    RequireColor(border.Background, palette.Tokens.SurfaceHighest, "Material focused fill is incorrect");
                    RequireColor(border.BorderBrush, palette.Tokens.Accent, "Material focus border is not the primary role");
                    Require(border.BorderThickness == new Thickness(2), "Material focus outline is not 2 dip");
                    var renderedFill = (SolidColorBrush)border.Background;
                    var renderedFocus = (SolidColorBrush)border.BorderBrush;
                    var fillColor = CompositeColor(renderedFill.Color, renderedFill.Opacity, palette.Surface);
                    var focusColor = CompositeColor(renderedFocus.Color, renderedFocus.Opacity, fillColor);
                    Require(ColorContrast(focusColor, fillColor) >= 3 && ColorContrast(focusColor, palette.Surface) >= 3,
                        "Rendered Material keyboard focus does not reach 3:1 against both adjacent surfaces");
                    var renderedText = (SolidColorBrush)editor.Foreground;
                    Require(ColorContrast(CompositeColor(renderedText.Color, renderedText.Opacity, fillColor), fillColor) >= 4.5,
                        "Rendered focused search text does not reach 4.5:1 against its actual fill");
                }
                else if (!palette.Tokens.HighContrast)
                    Require(border.BorderBrush is LinearGradientBrush, "Fluent native focused elevation/underline brush is missing");
                await CaptureAsync(Path.Combine(directory, $"search-{design}-{theme}-focused.png"));

                foreach (var text in new[] { "3", "3.", "3.1", "3.14", "3.14.29" }) editor.Text = text;
                await WaitForSmokeConditionAsync(() => control.Text == "3.14.29" && VisibleRuntimeCount == 1,
                    context + ": Native editor text did not reach the debounced filter");
                RequireStableEditor();
                var submitted = 0;
                control.QuerySubmitted += (_, _) => submitted++;
                InvokeButton(query);
                await WaitForSmokeConditionAsync(() => submitted == 1, context + ": Native query button did not submit");
                RequireStableEditor();

                // This verifies native text/selection lifetime with CJK text. OS IME composition
                // itself requires a real keyboard/input-method session and is not synthesized here.
                editor.Text = "测试 Python 日本語";
                await WaitForSmokeConditionAsync(() => control.Text == editor.Text && VisibleRuntimeCount == 0,
                    context + ": CJK text did not survive native text propagation");
                editor.Select(3, 6);
                Require(editor.SelectedText == "Python", "Native text selection was lost");
                RequireStableEditor();
                editor.Focus(FocusState.Keyboard); Root.UpdateLayout();
                Require(clear.Visibility == Visibility.Visible, "Native clear button did not appear for nonempty focused text");
                InvokeButton(clear);
                await WaitForSmokeConditionAsync(() => editor.Text.Length == 0 && control.Text.Length == 0 && VisibleRuntimeCount == 30,
                    context + ": Native clear button did not restore unfiltered results");
                RequireStableEditor();
                if (design == "Material")
                {
                    Navigate("runtimes"); Root.UpdateLayout(); await Task.Delay(60); Root.UpdateLayout();
                    var environmentSearchControl = Descendants(PageHost).OfType<AutoSuggestBox>().Single();
                    var environmentEditor = Descendants(environmentSearchControl).OfType<TextBox>().Single(box => box.Name == "TextBox");
                    var environmentBorder = Descendants(environmentEditor).OfType<Border>().Single(part => part.Name == "BorderElement");
                    Require(VisualStateManager.GoToState(environmentEditor, "Normal", false), "Installed-runtime search lacks normal state");
                    Require(environmentBorder.BorderThickness == new Thickness(palette.Tokens.HighContrast ? 1 : 0),
                        "Installed-runtime search retained a decorative resting outline");
                    await CaptureAsync(Path.Combine(directory, $"search-runtimes-{design}-{theme}-normal.png"));
                    Require(environmentEditor.Focus(FocusState.Keyboard), "Installed-runtime search cannot receive keyboard focus");
                    await Task.Delay(35); Root.UpdateLayout();
                    RequireColor(environmentBorder.BorderBrush, palette.Tokens.Accent, "Installed-runtime search lost keyboard focus indication");
                    Require(environmentBorder.BorderThickness == new Thickness(2), "Installed-runtime search focus outline is not 2 dip");
                    await CaptureAsync(Path.Combine(directory, $"search-runtimes-{design}-{theme}-focused.png"));
                }
            }
        }
        finally
        {
            CancelSearchRefresh(); catalog = originalCatalog; installed = originalInstalled;
            ApplySmokePreferences(originalPreferences); Navigate(originalPage); search = originalSearch;
        }
    }
}
