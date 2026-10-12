using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Storage.Streams;

namespace TaikoDiveLauncher.Pages;

public sealed partial class ProfilesPage : Page
{
    private readonly UserProfileStore _profileStore = new();
    private readonly Character3DStore _characterStore = new();
    private readonly Character3DPreviewService _characterPreview = new();
    private CancellationTokenSource? _previewCancellation;
    private UserProfile? _activeProfile;
    private int _requestedSlot = 1;
    private App AppInstance => (App)Application.Current;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += ProfilesPage_Loaded;
        Unloaded += (_, _) => { _previewCancellation?.Cancel(); };
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 820;
        PageRoot.Padding = (Thickness)Application.Current.Resources[narrow ? "PageNarrowPadding" : "PageWidePadding"];
        ActionColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(NamePlateNavigationButton, narrow ? 0 : 1);
        Grid.SetRow(NamePlateNavigationButton, narrow ? 1 : 0);
        ProfileColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(260);
        EditorColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ProfileRow.Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        EditorRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        ProfileRail.MaxHeight = narrow ? 220 : double.PositiveInfinity;
        ProfileList.MinHeight = narrow ? 140 : 220;
        PageScroller.VerticalScrollMode = narrow ? ScrollMode.Enabled : ScrollMode.Disabled;
        PageScroller.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        EditorSurface.VerticalScrollMode = narrow ? ScrollMode.Disabled : ScrollMode.Enabled;
        EditorSurface.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Grid.SetColumn(EditorSurface, narrow ? 0 : 1);
        Grid.SetRow(EditorSurface, narrow ? 1 : 0);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int slot && slot is >= 1 and <= 9) _requestedSlot = slot;
    }

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        CharacterNavigationButton.IsEnabled = NamePlateNavigationButton.IsEnabled = false;
        if (AppInstance.Context.Installation is not { } installation) return;
        BusyRing.IsActive = true;
        BusyRing.Visibility = Visibility.Visible;
        try
        {
            var profiles = await _profileStore.LoadAsync(installation);
            if (!IsLoaded) return;
            ProfileList.ItemsSource = profiles;
            ProfileList.SelectedItem = profiles.First(p => p.Slot == _requestedSlot);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { BusyRing.IsActive = false; BusyRing.Visibility = Visibility.Collapsed; }
    }

    private async void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        DonPreviewImage.Source = null;
        _activeProfile = ProfileList.SelectedItem as UserProfile;
        CharacterNavigationButton.IsEnabled = NamePlateNavigationButton.IsEnabled = _activeProfile is not null;
        if (_activeProfile is not { } profile || AppInstance.Context.Installation is not { } installation) return;
        StatusBar.IsOpen = false;
        PreviewBusy.IsActive = true;
        PreviewBusy.Visibility = Visibility.Visible;
        try
        {
            await Task.WhenAll(LoadCharacterAsync(installation, profile.Slot, token), LoadNamePlateAsync(installation, profile, token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) ShowError(ex); }
        finally
        {
            if (!token.IsCancellationRequested) { PreviewBusy.IsActive = false; PreviewBusy.Visibility = Visibility.Collapsed; }
        }
    }

    private async Task LoadNamePlateAsync(TaikoDiveInstallation installation, UserProfile profile, CancellationToken token)
    {
        await NamePlatePreview.ShowNamePlateAsync(installation, profile.NamePlateType);
        token.ThrowIfCancellationRequested();
        await NamePlatePreview.SetTextAsync(installation, profile.Name, profile.Title);
        token.ThrowIfCancellationRequested();
    }

    private async Task LoadCharacterAsync(TaikoDiveInstallation installation, int slot, CancellationToken token)
    {
        // Use the same saved-settings render and decoded-image cache as the home preview.
        var saved = await _characterStore.LoadAsync(installation, slot);
        token.ThrowIfCancellationRequested();
        string key = Character3DPreviewService.CreateStaticCacheKey(installation, saved);
        var cachedPreview = slot == 1 ? AppInstance.HomeDonPreview
            : AppInstance.ProfileDonPreviews.TryGetValue(slot, out var savedPreview) ? savedPreview : ((string Key, BitmapImage Image)?)null;
        if (cachedPreview is { } cached && cached.Key == key)
        {
            DonPreviewImage.Source = cached.Image;
            return;
        }
        byte[] png = await _characterPreview.RenderAsync(installation, saved, token);
        token.ThrowIfCancellationRequested();
        using InMemoryRandomAccessStream stream = new();
        using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
        }
        stream.Seek(0);
        BitmapImage image = new();
        await image.SetSourceAsync(stream);
        token.ThrowIfCancellationRequested();
        if (slot == 1) AppInstance.HomeDonPreview = (key, image);
        else AppInstance.ProfileDonPreviews[slot] = (key, image); // At most eight other user slots.
        DonPreviewImage.Source = image;
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProfile is { } profile) Frame.Navigate(typeof(Character3DPage), profile.Slot);
    }

    private void EditNamePlate_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProfile is { } profile) Frame.Navigate(typeof(NamePlatePage), profile.Slot);
    }

    private void ShowError(Exception ex)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Message = ex.Message;
        StatusBar.IsOpen = true;
    }
}
