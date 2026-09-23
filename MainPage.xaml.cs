using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Pages;

namespace TaikoDiveLauncher;

public sealed partial class MainPage : Page
{
    private NavigationViewItem? _currentNavigationItem;
    private bool _suppressSelectionChanged;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).Context.Updates.StateChanged -= Updates_StateChanged;
        ((App)Application.Current).Context.Updates.StateChanged += Updates_StateChanged;
        _suppressSelectionChanged = true;
        ShellNavigation.SelectedItem = HomeItem;
        _suppressSelectionChanged = false;
        _currentNavigationItem = HomeItem;
        Navigate("home");
        UpdateUpdateBanner();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).Context.Updates.StateChanged -= Updates_StateChanged;
    }

    private async void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelectionChanged || args.SelectedItemContainer is not NavigationViewItem target)
        {
            return;
        }

        await RequestNavigationAsync(target);
    }

    private async Task RequestNavigationAsync(NavigationViewItem target)
    {
        if (ReferenceEquals(target, _currentNavigationItem) || target.Tag is not string tag)
        {
            return;
        }

        if (ContentFrame.Content is IUnsavedChangesAware { HasUnsavedChanges: true } page)
        {
            ShellNavigation.IsEnabled = false;
            ContentDialog dialog = new()
            {
                Title = "変更を保存していません",
                Content = $"{page.UnsavedChangesName}の変更は保存されていません。破棄して別のページへ移動しますか？",
                PrimaryButtonText = "破棄して移動",
                CloseButtonText = "このページに戻る",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            ContentDialogResult result;
            try
            {
                result = await dialog.ShowAsync();
            }
            finally
            {
                ShellNavigation.IsEnabled = true;
            }

            if (result != ContentDialogResult.Primary)
            {
                RestoreCurrentSelection();
                return;
            }
        }

        Navigate(tag);
        _currentNavigationItem = target;
    }

    private void RestoreCurrentSelection()
    {
        _suppressSelectionChanged = true;
        ShellNavigation.SelectedItem = _currentNavigationItem;
        _suppressSelectionChanged = false;
    }

    private void Navigate(string tag)
    {
        Type pageType = tag switch
        {
            "profiles" => typeof(ProfilesPage),
            "songs" => typeof(SongsPage),
            "input-settings" => typeof(InputSettingsPage),
            "settings" => typeof(LaunchSettingsPage),
            "launcher-settings" => typeof(LauncherSettingsPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void Updates_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUpdateBanner);
    }

    private void UpdateUpdateBanner()
    {
        Services.LauncherUpdateService updates = ((App)Application.Current).Context.Updates;
        UpdateBanner.Message = updates.StatusMessage;
        UpdateBanner.IsOpen = updates.State is
            LauncherUpdateState.Available or
            LauncherUpdateState.Downloading or
            LauncherUpdateState.StartingInstaller;
    }

    private void OpenUpdateSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShellNavigation.SelectedItem = LauncherSettingsItem;
    }
}
