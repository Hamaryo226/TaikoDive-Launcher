using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Pages;

namespace TaikoDiveLauncher;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ShellNavigation.SelectedItem = HomeItem;
        Navigate("home");
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        Type pageType = tag switch
        {
            "profiles" => typeof(ProfilesPage),
            "songs" => typeof(SongsPage),
            "settings" => typeof(LaunchSettingsPage),
            "launcher-settings" => typeof(LauncherSettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
