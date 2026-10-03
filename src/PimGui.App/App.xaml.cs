using Microsoft.UI.Xaml;
using System.Globalization;

namespace PimGui.App;

public partial class App : Application
{
    public static MainWindow? Main { get; private set; }
    public App()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        UnhandledException += (_, e) =>
        {
            var arguments = Environment.GetCommandLineArgs();
            var smokeIndex = Array.IndexOf(arguments, "--smoke-test");
            var logDirectory = smokeIndex >= 0 && smokeIndex + 1 < arguments.Length
                ? Path.GetFullPath(arguments[smokeIndex + 1])
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyDeck");
            var path = Path.Combine(logDirectory, "crash.log");
            try
            {
                PimGui.Core.SafeFiles.RequireNoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{e.Message}\n{e.Exception}\n");
            }
            catch { }
        };
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        if (Main.PerformanceProbe || Main.DesignProbe || Main.WorkspaceProbe || Main.RestartProbe)
        {
            // Opt-in fixture-only checks run offscreen without taking keyboard focus.
            Main.AppWindow.IsShownInSwitchers = false;
            Main.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
            Main.AppWindow.Show(false);
        }
        else Main.Activate();
    }
}
