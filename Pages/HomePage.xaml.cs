using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Storage.Streams;

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
        Unloaded += (_, _) => _previewCancellation?.Cancel();
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
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
            SetHeroStatus(ready: false, "配置を確認してください");
            ShowStatus(InfoBarSeverity.Warning, "このフォルダーに TaikoDive.exe がありません。ランチャーをゲーム本体の隣へ移動してください。");
            return;
        }

        SetHeroStatus(ready: true, "準備完了");

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

    private void QuickActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            MainPage.Current?.NavigateTo(tag);
        }
    }

    private void SetHeroStatus(bool ready, string text)
    {
        HeroStatusText.Text = text;
        HeroStatusIcon.Glyph = ready ? "\uE73E" : "\uE7BA";
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
