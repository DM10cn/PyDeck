using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Globalization;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private readonly List<(TextBlock Element, string Key)> shellLabels = [];
    private void CollectShellLabels(DependencyObject node)
    {
        if (node is TextBlock label && label != StatusText && label != StyleStatus && label != ConnectionText)
            shellLabels.Add((label, label.Text));
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) CollectShellLabels(VisualTreeHelper.GetChild(node, index));
    }
    private void ApplyLanguage()
    {
        Strings.Language = preferences.Language;
        var culture = CultureInfo.GetCultureInfo(preferences.Language);
        CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
        Root.Language = preferences.Language;
        foreach (var (element, key) in shellLabels) element.Text = T(key);
        UpdateConnection();
        if (!busy) StatusText.Text = T(connected ? "Connected on this computer" : "Not connected");
        if (MessageBar.IsOpen) MessageBar.IsOpen = false;
    }
    private static string RuntimeTitle(PythonRuntime runtime) => runtime.DisplayName
        .Replace("free-threaded", T("free-threaded"), StringComparison.OrdinalIgnoreCase)
        .Replace("embeddable", T("embeddable"), StringComparison.OrdinalIgnoreCase)
        .Replace("with tests", T("with tests"), StringComparison.OrdinalIgnoreCase)
        .Replace("32-bit", T("32-bit"), StringComparison.OrdinalIgnoreCase);
}
