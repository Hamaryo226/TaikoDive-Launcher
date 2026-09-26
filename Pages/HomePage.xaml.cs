using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Storage.Streams;
using Windows.UI.ViewManagement;

namespace TaikoDiveLauncher.Pages;

public sealed partial class HomePage : Page
{
    private readonly UserProfileStore _profileStore = new();
    private readonly Character3DStore _characterStore = new();
    private readonly Character3DPreviewService _characterPreview = new();
    private CancellationTokenSource? _previewCancellation;

    private App AppInstance => (App)Application.Current;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += (_, _) =>
        {
            _previewCancellation?.Cancel();
            HeroGradientAnimation.Stop();
        };
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyBannerStyle();
        if (new UISettings().AnimationsEnabled)
        {
            HeroGradientAnimation.Begin();
        }
        await RefreshAsync();
    }

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 820;
        LayoutRoot.Padding = narrow ? new Thickness(16, 20, 16, 32) : new Thickness(32, 24, 32, 40);
        HeroContent.Margin = narrow ? new Thickness(20) : new Thickness(36, 32, 36, 32);
        SummaryPrimaryColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(380);
        SummarySecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ActivitySurface, narrow ? 0 : 1);
        Grid.SetRow(ActivitySurface, narrow ? 1 : 0);
    }

    private void ApplyBannerStyle()
    {
        string name = AppInstance.Context.Preferences.HomeBannerStyle switch
        {
            "Violet" => "HeroGradientViolet",
            "Sunset" => "HeroGradientSunset",
            _ => "HeroGradient",
        };
        ((ImageBrush)HeroBaseBorder.Background).ImageSource =
            new BitmapImage(new Uri($"ms-appx:///Assets/{name}A.jpg"));
        ((ImageBrush)HeroGradientOverlay.Background).ImageSource =
            new BitmapImage(new Uri($"ms-appx:///Assets/{name}B.jpg"));
    }

    private async Task RefreshAsync()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        DonPreviewImage.Source = null;
        SetDonPreviewStatus(string.Empty);
        RecentSongsList.ItemsSource = null;
        RecentSongsEmptyText.Visibility = Visibility.Collapsed;
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        LaunchButton.IsEnabled = installation is not null;

        if (installation is null)
        {
            HeroStatusBadge.Visibility = Visibility.Collapsed;
            StatusBar.IsOpen = false;
            return;
        }

        HeroStatusBadge.Visibility = Visibility.Visible;
        HeroStatusText.Text = "準備完了";
        HeroStatusIcon.Glyph = "\uE73E";

        try
        {
            UserProfile profile = (await _profileStore.LoadAsync(installation))[0];
            await HomeNamePlatePreview.ShowNamePlateAsync(installation, profile.NamePlateType);
            await HomeNamePlatePreview.SetTextAsync(installation, profile.Name, profile.Title);
            _previewCancellation = new CancellationTokenSource();
            _ = LoadDonPreviewAsync(installation, _previewCancellation.Token);
            _ = LoadRecentSongsAsync(installation, profile.Name, _previewCancellation.Token);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async Task LoadRecentSongsAsync(TaikoDiveInstallation installation, string userName, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<RecentSong> songs = await _profileStore.GetRecentSongsAsync(installation, userName, 5, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                ShowRecentSongs(songs);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RecentSongsEmptyText.Text = $"プレイ履歴を読み取れません: {ex.Message}";
                RecentSongsEmptyText.Visibility = Visibility.Visible;
            }
        }
    }

    private void ShowRecentSongs(IReadOnlyList<RecentSong> songs)
    {
        RecentSongsList.ItemsSource = songs.Select((song, index) => new RecentSongItem(index + 1, song.Title)).ToList();
        RecentSongsEmptyText.Text = "まだプレイ履歴がありません。";
        RecentSongsEmptyText.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetDonPreviewStatus(string text)
    {
        DonPreviewStatus.Text = text;
        DonPreviewStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadDonPreviewAsync(TaikoDiveInstallation installation, CancellationToken cancellationToken)
    {
        DonPreviewBusy.IsActive = true;
        DonPreviewBusy.Visibility = Visibility.Visible;
        SetDonPreviewStatus("プレビューを作成しています…");
        try
        {
            Character3DSettings saved = await _characterStore.LoadAsync(installation, 1);
            byte[] png = await _characterPreview.RenderAsync(installation, saved, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using InMemoryRandomAccessStream stream = new();
            using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            BitmapImage image = new();
            await image.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            DonPreviewImage.Source = image;
            SetDonPreviewStatus(string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                SetDonPreviewStatus($"どんちゃんを表示できません: {ex.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                DonPreviewBusy.IsActive = false;
                DonPreviewBusy.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        OperationResult result = AppInstance.Context.LaunchGame();
        if (!result.Succeeded)
        {
            ContentDialog dialog = new()
            {
                Title = "起動できませんでした",
                Content = result.Message,
                CloseButtonText = "閉じる",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        if (AppInstance.Context.Preferences.CloseAfterLaunch)
        {
            AppInstance.Exit();
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}

public sealed partial class RecentSongItem(int rank, string title)
{
    public string RankText { get; } = rank.ToString();

    public string Title { get; } = title;

    public Visibility DonBadgeVisibility { get; } = rank % 2 == 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KaBadgeVisibility { get; } = rank % 2 == 1 ? Visibility.Collapsed : Visibility.Visible;
}
