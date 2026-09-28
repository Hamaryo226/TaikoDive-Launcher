using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TaikoDiveLauncher.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
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
        string style = AppInstance.Context.Preferences.HomeBannerStyle;
        ((ImageBrush)HeroBaseBorder.Background).ImageSource = new BitmapImage(HomeBanner.GetImageUri(style, 'A'));
        ((ImageBrush)HeroGradientOverlay.Background).ImageSource = new BitmapImage(HomeBanner.GetImageUri(style, 'B'));
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
        RecentSongsList.ItemsSource = songs.Select((song, index) => new RecentSongItem(index + 1, song)).ToList();
        RecentSongsEmptyText.Text = "まだプレイ履歴がありません。";
        RecentSongsEmptyText.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RecentSong_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecentSong song })
        {
            return;
        }

        try
        {
            IReadOnlyList<SongBestResult> results = await _profileStore.GetSongBestResultsAsync(song.ScorePath, CancellationToken.None);
            StackPanel content = new() { Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = "難易度を選ぶと、自己ベスト時の内訳を表示します。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            StackPanel details = new() { Spacing = 6, Visibility = Visibility.Collapsed };
            foreach (SongBestResult result in results)
            {
                content.Children.Add(CreateDifficultyButton(result, details));
            }
            content.Children.Add(details);

            ContentDialog dialog = new()
            {
                Title = song.Title,
                Content = new ScrollViewer
                {
                    Content = content,
                    Width = Math.Min(480, Math.Max(260, ActualWidth - 100)),
                    MaxHeight = Math.Min(580, Math.Max(280, ActualHeight - 150)),
                },
                CloseButtonText = "閉じる",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"自己ベストを読み取れません: {ex.Message}");
        }
    }

    private static Button CreateDifficultyButton(SongBestResult result, StackPanel details)
    {
        Grid heading = new() { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = result.Difficulty, FontSize = 15 });
        TextBlock score = new()
        {
            Text = result.HasRecord ? $"{result.Score:N0} 点" : "記録なし",
            FontSize = 15,
        };
        Grid.SetColumn(score, 1);
        heading.Children.Add(score);

        StackPanel summary = new() { Spacing = 3 };
        summary.Children.Add(heading);
        if (result.HasRecord)
        {
            summary.Children.Add(new TextBlock
            {
                Text = $"王冠: {result.CrownLabel}　　スコアランク: {result.ScoreRank}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
            });
        }

        Button button = new()
        {
            Content = summary,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = result.HasRecord,
        };
        AutomationProperties.SetName(button, result.HasRecord
            ? $"{result.Difficulty}、{result.Score:N0}点、王冠 {result.CrownLabel}、スコアランク {result.ScoreRank}"
            : $"{result.Difficulty}、記録なし");
        button.Click += (_, _) => ShowScoreDetails(details, result);
        return button;
    }

    private static void ShowScoreDetails(StackPanel details, SongBestResult result)
    {
        details.Children.Clear();
        details.Children.Add(new TextBlock { Text = $"{result.Difficulty} · 自己ベスト時の内訳", FontSize = 16 });
        details.Children.Add(new TextBlock { Text = $"スコア {result.Score:N0} 点　ゲージ {result.Gauge:0.##}%", TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = $"良 {result.Great:N0}　可 {result.Good:N0}　不可 {result.Miss:N0}", TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = $"連打 {result.RollCount:N0}　最大コンボ {result.MaxCombo:N0}", TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock
        {
            Text = "王冠とスコアランクは、それぞれこれまでの最高到達記録です。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
        });
        details.Visibility = Visibility.Visible;
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
            BitmapImage image = await PngImage.DecodeAsync(png);
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

public sealed partial class RecentSongItem(int rank, RecentSong song)
{
    public string RankText { get; } = rank.ToString();

    public RecentSong Song { get; } = song;

    public string Title { get; } = song.Title;

    public Visibility DonBadgeVisibility { get; } = rank % 2 == 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KaBadgeVisibility { get; } = rank % 2 == 1 ? Visibility.Collapsed : Visibility.Visible;
}
