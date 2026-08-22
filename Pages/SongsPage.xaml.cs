using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TaikoDiveLauncher.Pages;

public sealed partial class SongsPage : Page
{
    private IReadOnlyList<SongGenre> _genres = [];
    private bool _isBusy;

    private App AppInstance => (App)Application.Current;

    public SongsPage()
    {
        InitializeComponent();
        Loaded += SongsPage_Loaded;
    }

    private void SongsPage_Loaded(object sender, RoutedEventArgs e) => ReloadGenres();

    private void ReloadGenres()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        string? selectedPath = (GenreList.SelectedItem as SongGenre)?.DirectoryPath;
        try
        {
            _genres = installation is null ? [] : SongImportService.LoadGenres(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _genres = [];
            ShowStatus(InfoBarSeverity.Error, $"ジャンルを読み込めませんでした: {ex.Message}");
        }

        GenreList.ItemsSource = _genres;
        GenreList.SelectedItem = _genres.FirstOrDefault(genre =>
            string.Equals(genre.DirectoryPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (GenreList.SelectedItem is null)
        {
            GenreList.SelectedIndex = _genres.Count > 0 ? 0 : -1;
        }
        GenreSummaryText.Text = installation is null
            ? "TaikoDive.exeの配置を確認してください。"
            : $"{installation.SongsDirectory} から {_genres.Count} 件を読み込みました。";
        EmptyGenresText.Visibility = _genres.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSongsPathStatus();
    }

    private void UpdateSongsPathStatus()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            CurrentSongsPathText.Text = "TaikoDive.exeが見つかりません";
            SongsPathModeText.Text = "ランチャーをTaikoDive.exeと同じフォルダーへ配置してください。";
            RestoreSongsButton.Visibility = Visibility.Collapsed;
            PathCommandBar.IsEnabled = false;
            return;
        }

        SongsPathState state = SongsPathService.GetState(installation);
        CurrentSongsPathText.Text = state.EffectivePath;
        SongsPathModeText.Text = state.IsRedirected
            ? "外部Songsを使用中です。TaikoDive用のジャンル画像などは切替時に不足分だけ補完しています。"
            : "TaikoDive標準のSongsフォルダーを使用中です。";
        RestoreSongsButton.Visibility = state.CanRestore ? Visibility.Visible : Visibility.Collapsed;
        PathCommandBar.IsEnabled = !_isBusy;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadGenres();
        ShowStatus(InfoBarSeverity.Informational, $"ジャンルを再読み込みしました（{_genres.Count}件）。");
    }

    private async void SelectSongsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            FolderPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, App.MainWindow.GetWindowHandle());
            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                await ConfirmAndChangeSongsPathAsync(folder.Path, "選択したフォルダー");
            }
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"Songsフォルダーを選択できませんでした: {ex.Message}");
        }
    }

    private async void FindTaikoNautsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        IReadOnlyList<TaikoNautsInstallation> installations;
        try
        {
            installations = await TaikoNautsDiscoveryService.FindInstallationsAsync();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"TaikoNautsを検索できませんでした: {ex.Message}");
            return;
        }
        finally
        {
            SetBusy(false);
        }

        if (installations.Count == 0)
        {
            ShowStatus(
                InfoBarSeverity.Warning,
                "デスクトップ、ドキュメント、ダウンロード、Program FilesからTaikoNauts.exeを見つけられませんでした。「Songsを選択」から指定してください。");
            return;
        }

        TaikoNautsInstallation? selectedInstallation = installations.Count == 1
            ? installations[0]
            : await SelectTaikoNautsInstallationAsync(installations);
        if (selectedInstallation is not null)
        {
            await ConfirmAndChangeSongsPathAsync(selectedInstallation.SongsDirectory, "TaikoNautsのSongs");
        }
    }

    private async Task<TaikoNautsInstallation?> SelectTaikoNautsInstallationAsync(
        IReadOnlyList<TaikoNautsInstallation> installations)
    {
        ComboBox picker = new()
        {
            Header = "使用するTaikoNauts",
            ItemsSource = installations,
            DisplayMemberPath = nameof(TaikoNautsInstallation.ExecutablePath),
            MinWidth = 420,
            SelectedIndex = 0,
        };
        ContentDialog dialog = new()
        {
            Title = $"TaikoNautsが{installations.Count}件見つかりました",
            Content = picker,
            PrimaryButtonText = "選択",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? picker.SelectedItem as TaikoNautsInstallation
            : null;
    }

    private async Task ConfirmAndChangeSongsPathAsync(string targetPath, string sourceLabel)
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDive.Launcher.exeをTaikoDive.exeと同じフォルダーへ配置してください。");
            return;
        }

        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = targetPath,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "元のTaikoDive Songsはバックアップとして残します。切替先には、選曲画面に必要なbox.def、CenterText.apt、Image内の不足ファイルだけをコピーし、既存ファイルや楽曲は上書きしません。",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        ContentDialog dialog = new()
        {
            Title = $"{sourceLabel}へ変更しますか？",
            Content = content,
            PrimaryButtonText = "変更",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetBusy(true);
        try
        {
            OperationResult result = await SongsPathService.ChangeAsync(installation, targetPath);
            ShowStatus(result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
            ReloadGenres();
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Warning, "Songsフォルダーの変更をキャンセルしました。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreSongsButton_Click(object sender, RoutedEventArgs e)
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (_isBusy || installation is null)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            Title = "TaikoDive標準のSongsへ戻しますか？",
            Content = "切替先の外部Songsや、そこへ補完したアセットは削除しません。",
            PrimaryButtonText = "元に戻す",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetBusy(true);
        try
        {
            OperationResult result = await SongsPathService.RestoreAsync(installation);
            ShowStatus(result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
            ReloadGenres();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        bool acceptsZip = !_isBusy && e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation = acceptsZip ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (acceptsZip)
        {
            e.DragUIOverride.Caption = "楽曲ZIPを追加";
            e.DragUIOverride.IsCaptionVisible = true;
            DropZone.BorderThickness = new Thickness(2);
        }

        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => ResetDropZone();

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        ResetDropZone();
        if (_isBusy || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            List<StorageFile> zipFiles = items
                .OfType<StorageFile>()
                .Where(file => string.Equals(file.FileType, ".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (zipFiles.Count != 1 || items.Count != 1)
            {
                ShowStatus(InfoBarSeverity.Warning, "一度に1つのZIPファイルをドロップしてください。");
                return;
            }

            await PromptAndImportAsync(zipFiles[0]);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"ドロップしたファイルを読み取れませんでした: {ex.Message}");
        }
    }

    private async void SelectZipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".zip");
            InitializeWithWindow.Initialize(picker, App.MainWindow.GetWindowHandle());
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await PromptAndImportAsync(file);
            }
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"ZIPを選択できませんでした: {ex.Message}");
        }
    }

    private async Task PromptAndImportAsync(StorageFile zipFile)
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "TaikoDive.Launcher.exeをTaikoDive.exeと同じフォルダーへ配置してください。");
            return;
        }

        ReloadGenres();
        if (_genres.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Songs直下に保存先ジャンルフォルダーがありません。");
            return;
        }

        ComboBox genrePicker = new()
        {
            Header = "保存先ジャンル",
            ItemsSource = _genres,
            DisplayMemberPath = nameof(SongGenre.Name),
            MinWidth = 300,
            SelectedItem = GenreList.SelectedItem as SongGenre ?? _genres[0],
        };
        StackPanel dialogContent = new() { Spacing = 12 };
        dialogContent.Children.Add(new TextBlock
        {
            Text = zipFile.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        dialogContent.Children.Add(genrePicker);
        dialogContent.Children.Add(new TextBlock
        {
            Text = "同名の楽曲フォルダーがある場合は上書きせず中止します。",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        ContentDialog dialog = new()
        {
            Title = "楽曲を追加",
            Content = dialogContent,
            PrimaryButtonText = "追加",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || genrePicker.SelectedItem is not SongGenre genre)
        {
            return;
        }

        SetBusy(true);
        try
        {
            SongImportResult result = await SongImportService.ImportAsync(installation, zipFile.Path, genre);
            ShowStatus(result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
            if (result.Succeeded)
            {
                GenreList.SelectedItem = genre;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DropZone.IsHitTestVisible = !busy;
        SongCommandBar.IsEnabled = !busy;
        PathCommandBar.IsEnabled = !busy && AppInstance.Context.Installation is not null;
    }

    private void ResetDropZone() => DropZone.BorderThickness = new Thickness(1);

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
