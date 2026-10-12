using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class NamePlatePage : Page, IUnsavedChangesAware
{
    private readonly UserProfileStore _profileStore = new();

    private bool _updatingEditor;
    private UserProfile? _activeProfile;
    private int _requestedSlot = 1;

    public bool HasUnsavedChanges => _activeProfile is not null
        && (NameBox.Text != _activeProfile.Name
            || TitleBox.Text != _activeProfile.Title
            || (NamePlateBox.SelectedValue is int plateType ? plateType : _activeProfile.NamePlateType) != _activeProfile.NamePlateType);

    public string UnsavedChangesName => "ネームプレート";

    private App AppInstance => (App)Application.Current;

    public NamePlatePage()
    {
        InitializeComponent();
        Loaded += NamePlatePage_Loaded;
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 820;
        PageRoot.Padding = (Thickness)Application.Current.Resources[narrow ? "PageNarrowPadding" : "PageWidePadding"];
        Grid.SetColumn(ProfileCommandBar, narrow ? 0 : 1);
        Grid.SetRow(ProfileCommandBar, narrow ? 1 : 0);
        ProfileCommandBar.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int slot && slot is >= 1 and <= 9) _requestedSlot = slot;
    }

    private async void NamePlatePage_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync(_requestedSlot);
    }

    private async Task ReloadAsync(int selectedSlot = 1)
    {
        if (AppInstance.Context.Installation is not { } installation) { SetEditorEnabled(false); return; }
        SetBusy(true);
        try
        {
            _activeProfile = (await _profileStore.LoadAsync(installation)).First(p => p.Slot == selectedSlot);
            _updatingEditor = true;
            NameBox.Text = _activeProfile.Name;
            TitleBox.Text = _activeProfile.Title;
            NamePlateBox.ItemsSource = _profileStore.GetNamePlateOptions(installation, _activeProfile.NamePlateType);
            NamePlateBox.SelectedValue = _activeProfile.NamePlateType;
            _updatingEditor = false;
            await NamePlatePreview.ShowNamePlateAsync(installation, _activeProfile.NamePlateType);
            await UpdateNamePlateTextAsync(installation);
            UpdateEditFeedback();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, ex.Message); }
        finally { _updatingEditor = false; SetBusy(false); }
    }

    private async void NamePlateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEditor)
        {
            return;
        }

        if (NamePlateBox.SelectedValue is int plateType && AppInstance.Context.Installation is { } installation)
        {
            UpdateEditFeedback();
            await NamePlatePreview.ShowNamePlateAsync(installation, plateType);
        }
    }

    private async void ProfileText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingEditor || NamePlatePreview is null || AppInstance.Context.Installation is not { } installation) return;
        UpdateEditFeedback();
        await UpdateNamePlateTextAsync(installation);
    }

    private void UpdateEditFeedback()
    {
        EditFeedbackText.Text = _activeProfile is null
            ? "プロフィールを選択してください。"
            : $"ユーザー {_activeProfile.Slot} · 名前 {NameBox.Text.Length}/32文字 · 称号 {TitleBox.Text.Length}/64文字 · {(HasUnsavedChanges ? "未保存" : "保存済み")}";
    }

    private async Task UpdateNamePlateTextAsync(TaikoDiveInstallation installation)
    {
        try { await NamePlatePreview.SetTextAsync(installation, NameBox.Text, TitleBox.Text); }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, $"ネームプレートの文字を描画できません: {ex.Message}"); }
    }

    private async void BackToProfile_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges && !await ConfirmDiscardChangesAsync()) return;
        Frame.Navigate(typeof(ProfilesPage), _requestedSlot);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProfile is not UserProfile selected || AppInstance.Context.Installation is not { } installation)
        {
            ShowStatus(InfoBarSeverity.Warning, "保存するプロフィールを選択してください。");
            return;
        }

        UserProfile updated = new()
        {
            Slot = selected.Slot,
            Name = NameBox.Text,
            Title = TitleBox.Text,
            CharaType = selected.CharaType,
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
        int selectedSlot = _requestedSlot;
        if (HasUnsavedChanges && !await ConfirmDiscardChangesAsync()) return;
        await ReloadAsync(selectedSlot);
    }

    private void SetBusy(bool isBusy)
    {
        SetEditorEnabled(!isBusy && _activeProfile is not null);
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetEditorEnabled(bool isEnabled)
    {
        ProfileCommandBar.IsEnabled = isEnabled;
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
        ProfileCommandBar.IsEnabled = false;
        ContentDialog dialog = new()
        {
            Title = "変更を保存していません",
            Content = "このプロフィールの変更は保存されていません。破棄して移動または読み込み直しますか？",
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
            ProfileCommandBar.IsEnabled = true;
        }
    }
}
