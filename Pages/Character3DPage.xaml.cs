using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.UI;

namespace TaikoDiveLauncher.Pages;

public sealed partial class Character3DPage : Page, IUnsavedChangesAware
{
    private readonly Character3DStore _store = new();
    private Character3DSettings _settings = new();
    private bool _loading = true;
    private int _selectedSlot;
    private bool _changingUser;
    public bool HasUnsavedChanges { get; private set; }
    public string UnsavedChangesName => "どんちゃんの着せ替え";
    private TaikoDiveInstallation? Installation => ((App)Application.Current).Context.Installation;

    public Character3DPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync(int userSlot = 0)
    {
        _loading = true; Editor.IsEnabled = false; SaveButton.IsEnabled = false;
        try
        {
            if (Installation is not { } installation) throw new InvalidOperationException("ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            var profiles = await new UserProfileStore().LoadAsync(installation);
            var options = new System.Collections.Generic.List<IntOption> { new(0, "共通設定（未設定ユーザーの初期値）") };
            options.AddRange(profiles.Select(p => new IntOption(p.Slot, $"ユーザー {p.Slot} · {(p.IsConfigured ? p.Name : "未設定")}")));
            _selectedSlot = userSlot;
            UserBox.ItemsSource = options;
            UserBox.SelectedValue = userSlot;
            _settings = await _store.LoadAsync(installation, userSlot == 0 ? null : userSlot);
            EnabledSwitch.IsOn = _settings.Enabled; ModelsPathBox.Text = _settings.ModelsPath;
            EnabledSwitch.IsEnabled = ModelsPathBox.IsEnabled = userSlot == 0;
            CostumeSwitch.IsOn = _settings.UseCostume;
            LoadCatalog(); UpdateColors(); UpdateMode();
            HasUnsavedChanges = false; StatusBar.IsOpen = false;
            Editor.IsEnabled = true; SaveButton.IsEnabled = true;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _loading = false; }
    }

    private async void UserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _changingUser || UserBox.SelectedValue is not int slot || slot == _selectedSlot) return;
        _changingUser = true;
        UserBox.IsEnabled = false;
        try
        {
            if (HasUnsavedChanges)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = "変更を保存していません",
                    Content = "変更を破棄して別のユーザーへ移動しますか？",
                    PrimaryButtonText = "破棄して移動", CloseButtonText = "編集に戻る", DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    UserBox.SelectedValue = _selectedSlot;
                    return;
                }
            }
            await LoadAsync(slot);
        }
        finally { _changingUser = false; UserBox.IsEnabled = true; }
    }

    private void LoadCatalog()
    {
        if (Installation is not { } installation) return;
        string root = Character3DStore.ModelRoot(installation, ModelsPathBox.Text);
        HeadBox.ItemsSource = _store.GetOptions(root, "head", _settings.Head); HeadBox.SelectedValue = _settings.Head;
        BodyBox.ItemsSource = _store.GetOptions(root, "body", _settings.Body); BodyBox.SelectedValue = _settings.Body;
        CostumeBox.ItemsSource = _store.GetOptions(root, "cos", _settings.Costume); CostumeBox.SelectedValue = _settings.Costume;
        UpdateIcons();
    }

    private void Changed(object sender, RoutedEventArgs e) { if (!_loading) HasUnsavedChanges = true; }
    private void ModeChanged(object sender, RoutedEventArgs e) { if (HeadBox is null) return; UpdateMode(); Changed(sender, e); }
    private void UpdateMode()
    {
        HeadBox.IsEnabled = BodyBox.IsEnabled = !CostumeSwitch.IsOn;
        CostumeBox.IsEnabled = CostumeSwitch.IsOn;
        HeadIcon.Visibility = BodyIcon.Visibility = CostumeSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        CostumeIcon.Visibility = CostumeSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PartChanged(object sender, SelectionChangedEventArgs e) { if (HeadIcon is null) return; UpdateIcons(); Changed(sender, e); }
    private void UpdateIcons()
    {
        static void Set(Image image, ComboBox box) => image.Source = box.SelectedItem is CostumeOption { IconPath: { } path } ? new BitmapImage(new Uri(path)) : null;
        Set(HeadIcon, HeadBox); Set(BodyIcon, BodyBox); Set(CostumeIcon, CostumeBox);
    }
    private void ReadSelections()
    {
        _settings.Enabled = EnabledSwitch.IsOn; _settings.ModelsPath = ModelsPathBox.Text.Trim(); _settings.UseCostume = CostumeSwitch.IsOn;
        _settings.Head = HeadBox.SelectedValue as string ?? _settings.Head;
        _settings.Body = BodyBox.SelectedValue as string ?? _settings.Body;
        _settings.Costume = CostumeBox.SelectedValue as string ?? _settings.Costume;
    }
    private void ReadCatalog_Click(object sender, RoutedEventArgs e)
    {
        try { ReadSelections(); LoadCatalog(); } catch (Exception ex) { ShowError(ex); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Installation is not { } installation) return;
        Editor.IsEnabled = false; SaveButton.IsEnabled = false;
        try
        {
            ReadSelections(); await _store.SaveAsync(installation, _settings, _selectedSlot == 0 ? null : _selectedSlot); HasUnsavedChanges = false;
            StatusBar.Severity = InfoBarSeverity.Success; StatusBar.Message = "保存しました。次回のゲーム起動から反映されます。"; StatusBar.IsOpen = true;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { Editor.IsEnabled = true; SaveButton.IsEnabled = true; }
    }
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges)
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "変更を保存していません", Content = "変更を破棄して読み込み直しますか？", PrimaryButtonText = "読み込み直す", CloseButtonText = "戻る", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        await LoadAsync(_selectedSlot);
    }
    private static Color ParseColor(string value)
    {
        uint rgb = uint.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private void UpdateColors()
    {
        static void Set(Button button, string label, string hex)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 24, Height = 24, Fill = new SolidColorBrush(ParseColor(hex)), Stroke = button.Foreground, StrokeThickness = 1 });
            panel.Children.Add(new TextBlock { Text = $"{label}　{hex}", VerticalAlignment = VerticalAlignment.Center });
            button.Content = panel;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{label}の色 {hex}");
        }
        Set(BodyColorButton, "胴", _settings.BodyColor); Set(LimbsColorButton, "手足", _settings.LimbsColor);
        Set(FaceColorButton, "顔", _settings.FaceColor); Set(RimColorButton, "ふち", _settings.RimColor);
    }
    private async void Color_Click(object sender, RoutedEventArgs e)
    {
        string key = (string)((Button)sender).Tag;
        string current = key switch { "body" => _settings.BodyColor, "limbs" => _settings.LimbsColor, "face" => _settings.FaceColor, _ => _settings.RimColor };
        var picker = new ColorPicker { Color = ParseColor(current), IsAlphaEnabled = false, IsMoreButtonVisible = false, IsHexInputVisible = true };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "色を選ぶ", Content = picker, PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string hex = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        switch (key) { case "body": _settings.BodyColor = hex; break; case "limbs": _settings.LimbsColor = hex; break; case "face": _settings.FaceColor = hex; break; default: _settings.RimColor = hex; break; }
        HasUnsavedChanges = true; UpdateColors();
    }
    private void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new Character3DSettings(); _settings.BodyColor = defaults.BodyColor; _settings.LimbsColor = defaults.LimbsColor;
        _settings.FaceColor = defaults.FaceColor; _settings.RimColor = defaults.RimColor; HasUnsavedChanges = true; UpdateColors();
    }
    private void ShowError(Exception ex) { StatusBar.Severity = InfoBarSeverity.Error; StatusBar.Message = ex.Message; StatusBar.IsOpen = true; }
}
