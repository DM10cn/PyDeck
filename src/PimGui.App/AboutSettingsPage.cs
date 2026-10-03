using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private StackPanel BuildAboutSettings()
    {
        var body = new StackPanel { Spacing = 28 };
        var identity = new StackPanel { Spacing = 8, Margin = new(16, 8, 16, 0) };
        var name = palette.Label("PyDeck", 44, true);
        name.Foreground = Palette.Brush(palette.Accent);
        identity.Children.Add(name);
        identity.Children.Add(palette.Label(AppVersionLabel, 14, muted: true));
        identity.Children.Add(palette.Label("An open-source desktop companion for Python Install Manager.", 16));
        var checkUpdate = palette.Action("Check for updates", "\uE72C", role: ActionRole.Primary);
        BindAvailability(checkUpdate, () => CanWork(WorkKind.AppUpdate));
        checkUpdate.Click += async (_, _) => await CheckAppUpdateAsync();
        var releases = palette.Action("Release notes", role: ActionRole.Quiet);
        releases.Click += (_, _) => OpenUrl("https://github.com/DM10cn/PyDeck/releases");
        var actions = Toolbar(checkUpdate, releases); actions.Margin = new(0, 12, 0, 0);
        actions.SizeChanged += (_, args) => actions.Orientation = args.NewSize.Width < 390
            ? Orientation.Vertical : Orientation.Horizontal;
        identity.Children.Add(actions);
        body.Children.Add(identity);

        var project = AboutLinkGroup("Project",
            ("Source code", "\uE943", "https://github.com/DM10cn/PyDeck"),
            ("Report an issue", "\uE90A", "https://github.com/DM10cn/PyDeck/issues"));
        var openSource = AboutLinkGroup("Open source",
            ("MIT license", "\uE8A5", "https://github.com/DM10cn/PyDeck/blob/main/LICENSE"),
            ("Third-party notices", "\uE8A5", "https://github.com/DM10cn/PyDeck/blob/main/THIRD-PARTY-NOTICES.md"));
        var links = new Grid { ColumnSpacing = 20, RowSpacing = 24 };
        links.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        links.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        links.RowDefinitions.Add(new() { Height = GridLength.Auto });
        links.RowDefinitions.Add(new() { Height = GridLength.Auto });
        links.Children.Add(project); links.Children.Add(openSource); Grid.SetColumn(openSource, 1);
        links.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 760;
            links.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
            links.ColumnSpacing = narrow ? 0 : 20;
            Grid.SetColumn(openSource, narrow ? 0 : 1);
            Grid.SetRow(openSource, narrow ? 1 : 0);
        };
        body.Children.Add(links);
        var exit = palette.Action("Exit PyDeck", "\uE7E8", compact: true, role: ActionRole.Quiet);
        exit.HorizontalAlignment = HorizontalAlignment.Left;
        exit.Click += (_, _) => ExitFromTray();
        body.Children.Add(exit);
        return body;
    }

    private StackPanel AboutLinkGroup(string title, params (string Label, string Icon, string Url)[] links)
    {
        var section = palette.Section(title);
        for (var index = 0; index < links.Length; index++)
        {
            var link = links[index];
            var button = palette.Action(link.Label, role: ActionRole.Secondary);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new(20, 18, 20, 18);
            button.MinHeight = 68;
            button.Background = Palette.Brush(palette.Card);
            button.Foreground = Palette.Brush(palette.Text);
            button.Resources["PyDeckActionStateLayer"] = Palette.Brush(palette.Text);
            button.CornerRadius = new(index == 0 ? palette.Tokens.CardRadius : 4,
                index == 0 ? palette.Tokens.CardRadius : 4,
                index == links.Length - 1 ? palette.Tokens.CardRadius : 4,
                index == links.Length - 1 ? palette.Tokens.CardRadius : 4);
            var content = new Grid { ColumnSpacing = 18 };
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.Children.Add(new FontIcon { Glyph = link.Icon, FontSize = 22, IsTextScaleFactorEnabled = false });
            var label = palette.Label(link.Label, 16); label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var external = new FontIcon { Glyph = "\uE8A7", FontSize = 18, IsTextScaleFactorEnabled = false };
            Grid.SetColumn(external, 2); content.Children.Add(external);
            button.Content = content;
            button.Click += (_, _) => OpenUrl(link.Url);
            section.Children.Add(button);
        }
        return section;
    }
}
