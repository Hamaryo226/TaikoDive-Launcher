using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class ProfilesPage : Page, IUnsavedChangesAware
{
    private readonly UserProfileStore _profileStore = new();
    private IReadOnlyList<UserProfile> _profiles = [];
    private CancellationTokenSource? _statisticsCancellation;
    private bool _updatingEditor;
    private bool _restoringProfileSelection;
    private UserProfile? _activeProfile;

    public bool HasUnsavedChanges => _activeProfile is not null
        && (NameBox.Text != _activeProfile.Name
            || TitleBox.Text != _activeProfile.Title
            || (CharacterBox.SelectedValue as string ?? _activeProfile.CharaType) != _activeProfile.CharaType
            || (NamePlateBox.SelectedValue is int plateType ? plateType : _activeProfile.NamePlateType) != _activeProfile.NamePlateType);

    public string UnsavedChangesName => "プロフィール";

    private App AppInstance => (App)Application.Current;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += ProfilesPage_Loaded;
        Unloaded += ProfilesPage_Unloaded;
    }

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private void ProfilesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _statisticsCancellation?.Cancel();
        _statisticsCancellation?.Dispose();
    }

    private async Task ReloadAsync(int selectedSlot = 1)
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            ProfileList.ItemsSource = null;
            SetEditorEnabled(false);
            ShowStatus(InfoBarSeverity.Warning, "ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            return;
        }

        SetBusy(true);
        try
        {
            _profiles = await _profileStore.LoadAsync(installation);
            _activeProfile = null;
            ProfileList.ItemsSource = _profiles;
            ProfileList.SelectedItem = _profiles.FirstOrDefault(profile => profile.Slot == selectedSlot) ?? _profiles[0];
            SetEditorEnabled(true);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            SetEditorEnabled(false);
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringProfileSelection)
        {
            return;
        }

        if (ProfileList.SelectedItem is not UserProfile profile || AppInstance.Context.Installation is not { } installation)
        {
            return;
        }

        if (_activeProfile is not null
            && _activeProfile.Slot != profile.Slot
            && HasUnsavedChanges
            && !await ConfirmDiscardChangesAsync())
        {
            _restoringProfileSelection = true;
            ProfileList.SelectedItem = _activeProfile;
            _restoringProfileSelection = false;
            return;
        }

        _activeProfile = profile;
        _updatingEditor = true;
        SlotHeading.Text = $"{profile.Slot}P USER";
        NameBox.Text = profile.Name;
        TitleBox.Text = profile.Title;
        CharacterBox.ItemsSource = _profileStore.GetCharacterOptions(installation, profile.CharaType);
        CharacterBox.SelectedValue = profile.CharaType;
        NamePlateBox.ItemsSource = _profileStore.GetNamePlateOptions(installation, profile.NamePlateType);
        NamePlateBox.SelectedValue = profile.NamePlateType;
        _updatingEditor = false;
        await CharacterPreview.ShowCharacterAsync(installation, profile.CharaType);
        await NamePlatePreview.ShowNamePlateAsync(installation, profile.NamePlateType);

        _statisticsCancellation?.Cancel();
        _statisticsCancellation?.Dispose();
        _statisticsCancellation = new CancellationTokenSource();
        ScoreCountText.Text = "…";
        ReplayCountText.Text = "…";
        DataFolderText.Text = string.Empty;

        try
        {
            UserStatistics statistics = await _profileStore.GetStatisticsAsync(
                installation,
                profile.Name,
                _statisticsCancellation.Token);
            ScoreCountText.Text = statistics.ScoreCount.ToString();
            ReplayCountText.Text = statistics.ReplayCount.ToString();
            DataFolderText.Text = statistics.FolderPath;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ScoreCountText.Text = "—";
            ReplayCountText.Text = "—";
            DataFolderText.Text = ex.Message;
        }
    }

    private async void CharacterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEditor)
        {
            return;
        }

        if (CharacterBox.SelectedValue is string characterType && AppInstance.Context.Installation is { } installation)
        {
            await CharacterPreview.ShowCharacterAsync(installation, characterType);
        }
    }

    private async void NamePlateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEditor)
        {
            return;
        }

        if (NamePlateBox.SelectedValue is int plateType && AppInstance.Context.Installation is { } installation)
        {
            await NamePlatePreview.ShowNamePlateAsync(installation, plateType);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not UserProfile selected || AppInstance.Context.Installation is not { } installation)
        {
            ShowStatus(InfoBarSeverity.Warning, "保存するプロフィールを選択してください。");
            return;
        }

        UserProfile updated = new()
        {
            Slot = selected.Slot,
            Name = NameBox.Text,
            Title = TitleBox.Text,
            CharaType = CharacterBox.SelectedValue as string ?? selected.CharaType,
            NamePlateType = NamePlateBox.SelectedValue is int plateType ? plateType : selected.NamePlateType,
            IsConfigured = true,
        };

        SetBusy(true);
        try
        {
            await _profileStore.SaveAsync(installation, updated);
            await ReloadAsync(updated.Slot);
            ShowStatus(InfoBarSeverity.Success, "プロフィールを保存しました。User.ini.launcher.bak に直前の内容を残しています。");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        int selectedSlot = (ProfileList.SelectedItem as UserProfile)?.Slot ?? 1;
        await ReloadAsync(selectedSlot);
    }

    private void SetBusy(bool isBusy)
    {
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetEditorEnabled(bool isEnabled)
    {
        ProfileList.IsEnabled = isEnabled;
        EditorSurface.IsEnabled = isEnabled;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        ProfileList.IsEnabled = false;
        ContentDialog dialog = new()
        {
            Title = "変更を保存していません",
            Content = "このプロフィールの変更は保存されていません。破棄して別のプロフィールへ移動しますか？",
            PrimaryButtonText = "破棄して移動",
            CloseButtonText = "編集に戻る",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            ProfileList.IsEnabled = true;
        }
    }
}
