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
    private string _colorTarget = "body";
    private bool _updatingPicker;
    private readonly Character3DPreviewService _preview = new();
    private CancellationTokenSource? _previewCancellation;
    private bool _pageLoaded;
    public bool HasUnsavedChanges { get; private set; }
    public string UnsavedChangesName => "どんちゃんの着せ替え";
    private TaikoDiveInstallation? Installation => ((App)Application.Current).Context.Installation;

    public Character3DPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => { _pageLoaded = true; await LoadAsync(); };
        Unloaded += (_, _) => { _pageLoaded = false; _previewCancellation?.Cancel(); };
    }

    private async Task LoadAsync(int userSlot = 0, bool restorePrevious = false)
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
            _settings = restorePrevious
                ? await _store.LoadPreviousAsync(installation, userSlot == 0 ? null : userSlot)
                : await _store.LoadAsync(installation, userSlot == 0 ? null : userSlot);
            if (restorePrevious)
                _settings.ModelsPath = (await _store.LoadAsync(installation)).ModelsPath;
            ModelsPathBox.Text = _settings.ModelsPath;
            ModelsPathBox.IsEnabled = userSlot == 0;
            CostumeSwitch.IsOn = _settings.UseCostume;
            LoadCatalog(); UpdateColors(); UpdateMode();
            HasUnsavedChanges = restorePrevious; StatusBar.IsOpen = restorePrevious;
            if (restorePrevious)
            {
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Message = "1つ前の衣装と色を復元しました。プレビューで確認して保存してください。";
            }
            Editor.IsEnabled = true; SaveButton.IsEnabled = true;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _loading = false; }
        if (Editor.IsEnabled) QueuePreview();
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

    private void Changed(object sender, RoutedEventArgs e) { if (!_loading) { HasUnsavedChanges = true; QueuePreview(); } }
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
        _settings.Enabled = true; _settings.ModelsPath = ModelsPathBox.Text.Trim(); _settings.UseCostume = CostumeSwitch.IsOn;
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
        Editor.IsEnabled = false; SaveButton.IsEnabled = false; UserBox.IsEnabled = false;
        try
        {
            ReadSelections(); await _store.SaveAsync(installation, _settings, _selectedSlot == 0 ? null : _selectedSlot); HasUnsavedChanges = false;
            StatusBar.Severity = InfoBarSeverity.Success; StatusBar.Message = "保存しました。次回のゲーム起動から反映されます。"; StatusBar.IsOpen = true;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { Editor.IsEnabled = true; SaveButton.IsEnabled = true; UserBox.IsEnabled = true; }
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
    private async void RestorePrevious_Click(object sender, RoutedEventArgs e)
    {
        if (Installation is not { } installation) return;
        try
        {
            // バックアップが無い・壊れている場合は現在の編集を保持する。
            await _store.LoadPreviousAsync(installation, _selectedSlot == 0 ? null : _selectedSlot);
            if (HasUnsavedChanges)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "1つ前の保存を復元", Content = "編集中の衣装と色を、1つ前の保存内容に戻しますか？", PrimaryButtonText = "復元", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            await LoadAsync(_selectedSlot, restorePrevious: true);
        }
        catch (Exception ex) { ShowError(ex); }
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
        _updatingPicker = true;
        ColorEditor.Color = ParseColor(_colorTarget switch { "body" => _settings.BodyColor, "limbs" => _settings.LimbsColor, "face" => _settings.FaceColor, _ => _settings.RimColor });
        _updatingPicker = false;
    }
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        _colorTarget = (string)((Button)sender).Tag;
        ColorPickerHeading.Text = (_colorTarget switch { "body" => "胴", "limbs" => "手足", "face" => "顔", _ => "ふち" }) + "の色";
        UpdateColors();
        ColorEditor.StartBringIntoView();
    }
    private void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading || _updatingPicker) return;
        string hex = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
        switch (_colorTarget) { case "body": _settings.BodyColor = hex; break; case "limbs": _settings.LimbsColor = hex; break; case "face": _settings.FaceColor = hex; break; default: _settings.RimColor = hex; break; }
        HasUnsavedChanges = true; UpdateColors(); QueuePreview();
    }
    private void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new Character3DSettings(); _settings.BodyColor = defaults.BodyColor; _settings.LimbsColor = defaults.LimbsColor;
        _settings.FaceColor = defaults.FaceColor; _settings.RimColor = defaults.RimColor; HasUnsavedChanges = true; UpdateColors();
        QueuePreview();
    }
    private void Preview_Click(object sender, RoutedEventArgs e) => QueuePreview();

    private async void QueuePreview()
    {
        if (_loading || !_pageLoaded || Installation is not { } installation) return;
        _previewCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        PreviewImage.Source = null;
        PreviewBusy.IsActive = true; PreviewBusy.Visibility = Visibility.Visible;
        PreviewStatus.Text = "プレビューを作成しています…";
        try
        {
            ReadSelections();
            var snapshot = _settings with { };
            await Task.Delay(350, cancellation.Token);
            byte[] png = await _preview.RenderAsync(installation, snapshot, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            cancellation.Token.ThrowIfCancellationRequested();
            PreviewImage.Source = image;
            PreviewStatus.Text = "保存前の衣装と配色です。変更すると自動更新します。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) PreviewStatus.Text = "プレビューを表示できません: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                PreviewBusy.IsActive = false; PreviewBusy.Visibility = Visibility.Collapsed;
            }
        }
    }
    private void ShowError(Exception ex) { StatusBar.Severity = InfoBarSeverity.Error; StatusBar.Message = ex.Message; StatusBar.IsOpen = true; }
}
