using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class ProfilesPage : Page
{
    private readonly UserProfileStore _profileStore = new();
    private IReadOnlyList<UserProfile> _profiles = [];
    private CancellationTokenSource? _statisticsCancellation;

    private App AppInstance => (App)Application.Current;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += ProfilesPage_Loaded;
        Unloaded += ProfilesPage_Unloaded;
    }

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged += Context_InstallationChanged;
        await ReloadAsync();
    }

    private void ProfilesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AppInstance.Context.InstallationChanged -= Context_InstallationChanged;
        _statisticsCancellation?.Cancel();
        _statisticsCancellation?.Dispose();
    }

    private void Context_InstallationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () => await ReloadAsync());
    }

    private async Task ReloadAsync(int selectedSlot = 1)
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            ProfileList.ItemsSource = null;
            SetEditorEnabled(false);
            ShowStatus(InfoBarSeverity.Warning, "ホームでゲームフォルダーを選択してください。");
            return;
        }

        SetBusy(true);
        try
        {
            _profiles = await _profileStore.LoadAsync(installation);
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
        if (ProfileList.SelectedItem is not UserProfile profile || AppInstance.Context.Installation is not { } installation)
        {
            return;
        }

        SlotHeading.Text = $"{profile.Slot}P USER";
        NameBox.Text = profile.Name;
        TitleBox.Text = profile.Title;

        CharacterBox.ItemsSource = _profileStore.GetCharacterOptions(installation, profile.CharaType);
        CharacterBox.SelectedValue = profile.CharaType;
        NamePlateBox.ItemsSource = _profileStore.GetNamePlateOptions(installation, profile.NamePlateType);
        NamePlateBox.SelectedValue = profile.NamePlateType;

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
}
