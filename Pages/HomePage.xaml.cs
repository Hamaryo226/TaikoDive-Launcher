using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using TaikoDiveLauncher.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.UI.ViewManagement;

namespace TaikoDiveLauncher.Pages;

public sealed partial class HomePage : Page
{
    private readonly UserProfileStore _profileStore = new();
    private readonly Character3DStore _characterStore = new();
    private readonly Character3DPreviewService _characterPreview = new();
    private CancellationTokenSource? _previewCancellation;
    private ImageSource[] _crownImages = [];
    private SongScoreDialog? _activeScoreDialog;
    private bool _loadingSongDetails;

    private App AppInstance => (App)Application.Current;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += (_, _) =>
        {
            _previewCancellation?.Cancel();
            _activeScoreDialog?.Hide();
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
        DonPreviewStage.Height = narrow ? 300 : 260;
        HomeNamePlatePreview.MaxWidth = narrow ? 320 : double.PositiveInfinity;
        HomeNamePlatePreview.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
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
        _previewCancellation = new CancellationTokenSource();
        CancellationToken token = _previewCancellation.Token;
        DonPreviewBusy.IsActive = false;
        DonPreviewBusy.Visibility = Visibility.Collapsed;
        SetDonPreviewStatus(string.Empty);
        RecentSongsList.ItemsSource = null;
        _crownImages = [];
        RecentSongsEmptyText.Visibility = Visibility.Collapsed;
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        LaunchButton.IsEnabled = installation is not null;

        if (installation is null)
        {
            DonPreviewImage.Source = null;
            AppInstance.HomeDonPreview = null;
            _characterPreview.ClearStaticCache();
            HeroStatusBadge.Visibility = Visibility.Collapsed;
            StatusBar.IsOpen = false;
            return;
        }

        HeroStatusBadge.Visibility = Visibility.Visible;
        HeroStatusText.Text = "準備完了";
        HeroStatusIcon.Glyph = "\uE73E";
        _ = LoadDonPreviewAsync(installation, token);

        try
        {
            UserProfile profile = (await _profileStore.LoadAsync(installation))[0];
            token.ThrowIfCancellationRequested();
            await HomeNamePlatePreview.ShowNamePlateAsync(installation, profile.NamePlateType);
            token.ThrowIfCancellationRequested();
            await HomeNamePlatePreview.SetTextAsync(installation, profile.Name, profile.Title);
            token.ThrowIfCancellationRequested();
            _ = LoadRecentSongsAsync(installation, profile.Name, token);
            StatusBar.IsOpen = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async Task LoadRecentSongsAsync(TaikoDiveInstallation installation, string userName, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<RecentSong> songs = await _profileStore.GetRecentSongsAsync(installation, userName, 5, cancellationToken);
            ImageSource[] crownImages = songs.Count == 0 ? [] : await LoadCrownImagesAsync(installation, cancellationToken);
            IReadOnlyList<SongBestResult>[] bestResults = await Task.WhenAll(songs.Select(song =>
                _profileStore.GetSongBestResultsAsync(song.ScorePath, cancellationToken)));
            if (!cancellationToken.IsCancellationRequested)
            {
                _crownImages = crownImages;
                RecentSongsList.ItemsSource = songs.Select((song, index) =>
                    new RecentSongItem(song, bestResults[index], crownImages)).ToList();
                RecentSongsEmptyText.Text = "まだプレイ履歴がありません。";
                RecentSongsEmptyText.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private static async Task<ImageSource[]> LoadCrownImagesAsync(TaikoDiveInstallation installation, CancellationToken cancellationToken)
    {
        string path = Path.Combine(installation.BuildDirectory, "Texture", "Result", "Crown.png");
        if (!File.Exists(path))
        {
            path = Path.Combine(installation.BuildDirectory, "Texture", "Result", "Crown_S.png");
        }
        if (!File.Exists(path)) return [];

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            uint width = decoder.PixelWidth / 3;
            if (width == 0 || decoder.PixelHeight == 0) return [];
            ImageSource[] images = new ImageSource[3];
            for (uint index = 0; index < 3; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { Bounds = new BitmapBounds { X = index * width, Width = width, Height = decoder.PixelHeight } },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                WriteableBitmap bitmap = new((int)width, (int)decoder.PixelHeight);
                using (Stream target = bitmap.PixelBuffer.AsStream())
                {
                    await target.WriteAsync(pixels.DetachPixelData(), cancellationToken);
                }
                bitmap.Invalidate();
                images[index] = bitmap;
            }
            return images;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Missing or unreadable optional artwork still leaves the crown status available as text.
            return [];
        }
    }

    private async void RecentSong_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSongDetails || _activeScoreDialog is not null || sender is not Button { Tag: RecentSong song })
        {
            return;
        }

        _loadingSongDetails = true;
        try
        {
            CancellationToken token = _previewCancellation?.Token ?? CancellationToken.None;
            IReadOnlyList<SongBestResult> results = await _profileStore.GetSongBestResultsAsync(song.ScorePath, token);
            token.ThrowIfCancellationRequested();
            _activeScoreDialog = new SongScoreDialog(song, results, _crownImages)
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            await _activeScoreDialog.ShowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"自己ベストを読み取れません: {ex.Message}");
        }
        finally
        {
            _activeScoreDialog = null;
            _loadingSongDetails = false;
        }
    }

    private void SetDonPreviewStatus(string text)
    {
        DonPreviewStatus.Text = text;
        DonPreviewStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadDonPreviewAsync(TaikoDiveInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            Character3DSettings saved = await _characterStore.LoadAsync(installation, 1);
            cancellationToken.ThrowIfCancellationRequested();
            string key = Character3DPreviewService.CreateStaticCacheKey(installation, saved);
            if (AppInstance.HomeDonPreview is { } cached && cached.Key == key)
            {
                DonPreviewImage.Source = cached.Image;
                return;
            }

            DonPreviewImage.Source = null;
            DonPreviewBusy.IsActive = true;
            DonPreviewBusy.Visibility = Visibility.Visible;
            SetDonPreviewStatus("プレビューを作成しています…");
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
            AppInstance.HomeDonPreview = (key, image);
            SetDonPreviewStatus(string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                DonPreviewImage.Source = null;
                AppInstance.HomeDonPreview = null;
                SetDonPreviewStatus($"どんちゃんを表示できません: {ex.Message}");
            }
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

public sealed partial class RecentSongItem
{
    public RecentSongItem(RecentSong song, IReadOnlyList<SongBestResult> results, ImageSource[] images)
    {
        Song = song;
        string[] labels = ["簡", "普", "難", "鬼", "裏"];
        Crowns = results.Select((result, index) => new RecentCrownItem(labels[index], result, images)).ToList();
        AccessibilityLabel = song.Title + "、" + string.Join("、", Crowns.Select(crown => crown.Description));
    }

    public RecentSong Song { get; }

    public string Title => Song.Title;

    public IReadOnlyList<RecentCrownItem> Crowns { get; }

    public string AccessibilityLabel { get; }
}

public sealed partial class RecentCrownItem
{
    public RecentCrownItem(string difficultyLabel, SongBestResult result, ImageSource[] images)
    {
        DifficultyLabel = difficultyLabel;
        Description = $"{result.Difficulty}: {(result.HasRecord ? result.CrownLabel : "記録なし")}";
        int index = result.Crown switch { "Clear" => 0, "FullCombo" => 1, "DondaFullCombo" => 2, _ => -1 };
        Source = index >= 0 && index < images.Length ? images[index] : null;
        FallbackText = index switch { 0 => "銀", 1 => "金", 2 => "虹", _ => "—" };
    }

    public string DifficultyLabel { get; }
    public string Description { get; }
    public ImageSource? Source { get; }
    public string FallbackText { get; }
    public Visibility ImageVisibility => Source is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackVisibility => Source is null ? Visibility.Visible : Visibility.Collapsed;
}
