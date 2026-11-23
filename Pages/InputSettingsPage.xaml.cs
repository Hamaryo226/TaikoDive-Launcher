using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class InputSettingsPage : Page, IUnsavedChangesAware
{
    private readonly InputBindingsStore _store = new();
    private readonly ControllerInputMonitor _controllerMonitor = new();
    private InputBindings _bindings = new();
    private bool _editorReady;
    private bool _canEdit;
    private bool _isBusy;
    private MenuFlyout? _assignmentMenu;

    public bool HasUnsavedChanges { get; private set; }
    public string UnsavedChangesName => "入力設定";
    private int SelectedPlayer => PlayerPicker.SelectedIndex == 1 ? 2 : 1;
    private bool IsKeyboard => DeviceTypeBox.SelectedIndex == 0;
    private App AppInstance => (App)Application.Current;

    public InputSettingsPage()
    {
        InitializeComponent();
        _editorReady = true;
        DeviceDiagram.KeyInvoked += ShowKeyboardAssignmentMenu;
        DeviceDiagram.ControllerInvoked += ShowControllerAssignmentMenu;
        Loaded += async (_, _) => { await ReloadAsync(); RefreshControllers(); };
        Unloaded += (_, _) => _assignmentMenu?.Hide();
        RefreshEditor();
        RefreshEnabledState();
    }

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 900;
        PageRoot.Padding = (Thickness)Application.Current.Resources[narrow ? "PageNarrowPadding" : "PageWidePadding"];
    }

    private async Task ReloadAsync()
    {
        if (AppInstance.Context.Installation is not { } installation) { _canEdit = false; RefreshEnabledState(); return; }
        SetBusy(true);
        try
        {
            _bindings = await _store.LoadAsync(installation);
            _canEdit = true;
            HasUnsavedChanges = false;
            RefreshEditor();
            StatusBar.IsOpen = false;
        }
        catch (Exception ex) { _canEdit = false; ShowStatus(InfoBarSeverity.Error, ex.Message); }
        finally { SetBusy(false); }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit || _isBusy || AppInstance.Context.Installation is not { } installation) return;
        SetBusy(true);
        try
        {
            await _store.SaveAsync(installation, _bindings);
            HasUnsavedChanges = false;
            RefreshEditor();
            ShowStatus(InfoBarSeverity.Success, "入力設定を保存しました。");
        }
        catch (Exception ex) { ShowStatus(InfoBarSeverity.Error, ex.Message); }
        finally { SetBusy(false); }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) { await ReloadAsync(); RefreshControllers(); }
    private void RefreshControllersButton_Click(object sender, RoutedEventArgs e) => RefreshControllers();
    private void EditorSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_editorReady) return;
        _assignmentMenu?.Hide();
        RefreshEditor();
    }
    private void ControllerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_editorReady) return;
        _assignmentMenu?.Hide();
        RefreshEditor();
    }

    private void RefreshControllers()
    {
        string? selectedId = (ControllerBox.SelectedItem as ControllerDeviceOption)?.Id;
        List<ControllerDeviceOption> devices = [.. _controllerMonitor.GetDevices()];
        var stored = Enumerable.Range(0, 4).SelectMany(slot => _bindings.Player1.GetControllers(slot).Concat(_bindings.Player2.GetControllers(slot)))
            .GroupBy(ControllerInputMonitor.DeviceId);
        foreach (var device in stored.Where(group => devices.All(item => item.Id != group.Key)))
        {
            ControllerInputBinding identity = device.First();
            var inputs = device.DistinctBy(input => (input.InputType, input.InputIndex, input.InputValue))
                .OrderBy(input => input.InputType).ThenBy(input => input.InputIndex).ThenBy(input => input.InputValue)
                .Select(input => new ControllerDiagramInput(ShortInputLabel(input), string.Empty, input)).ToArray();
            devices.Add(new ControllerDeviceOption(device.Key, identity.DeviceName + "（未接続）", inputs));
        }

        ControllerBox.ItemsSource = devices;
        ControllerBox.SelectedItem = devices.FirstOrDefault(item => item.Id == selectedId) ?? devices.FirstOrDefault();
        ControllerBox.PlaceholderText = "コントローラーを接続してください";
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        if (!_editorReady) return;
        ControllerDeviceOption? device = ControllerBox.SelectedItem as ControllerDeviceOption;
        ControllerSurface.Visibility = IsKeyboard ? Visibility.Collapsed : Visibility.Visible;
        DeviceDiagram.Update(GetPlayer(SelectedPlayer), Math.Max(0, DeviceTypeBox.SelectedIndex), device?.Inputs ?? []);
        RefreshEnabledState();
    }

    private void ShowKeyboardAssignmentMenu(int keyCode, Button anchor)
    {
        if (!_canEdit || _isBusy) return;
        int player = SelectedPlayer;
        PlayerInputBindings selected = GetPlayer(player);
        bool conflict = Enumerable.Range(0, 4).Any(slot => GetPlayer(3 - player).GetKeys(slot).Contains(keyCode));
        ShowAssignmentMenu(anchor, InputLabelService.KeyLabel(keyCode), conflict,
            slot => selected.GetKeys(slot).Contains(keyCode),
            slot =>
            {
                var result = InputBindingsEditor.AssignKeyboard(_bindings, player, keyCode, slot);
                if (result == KeyboardAssignmentResult.Changed) AssignmentChanged();
                else if (result == KeyboardAssignmentResult.Conflict) ShowConflict();
            });
    }

    private void ShowControllerAssignmentMenu(ControllerInputBinding input, Button anchor)
    {
        if (!_canEdit || _isBusy) return;
        int player = SelectedPlayer;
        bool Has(PlayerInputBindings p, int slot) => p.GetControllers(slot).Any(existing => InputBindingsEditor.ControllerBindingsEqual(existing, input));
        bool conflict = Enumerable.Range(0, 4).Any(slot => Has(GetPlayer(3 - player), slot));
        ShowAssignmentMenu(anchor, ShortInputLabel(input), conflict,
            slot => Has(GetPlayer(player), slot),
            slot =>
            {
                var result = InputBindingsEditor.AssignController(_bindings, player, input, slot);
                if (result == ControllerAssignmentResult.Changed) AssignmentChanged();
                else if (result == ControllerAssignmentResult.Conflict) ShowConflict();
            });
    }

    private void ShowAssignmentMenu(Button anchor, string label, bool conflict, Func<int, bool> isAssigned, Action<int?> apply)
    {
        _assignmentMenu?.Hide();
        MenuFlyout menu = new();
        menu.Items.Add(new MenuFlyoutItem { Text = label, IsEnabled = false });
        if (conflict) menu.Items.Add(new MenuFlyoutItem { Text = "別のプレイヤーに使用中です", IsEnabled = false });
        for (int slot = 0; slot < 4; slot++)
        {
            int target = slot;
            MenuFlyoutItem item = new() { Text = (isAssigned(slot) ? "✓ " : "") + InputLabelService.HitLabel(slot), IsEnabled = !conflict };
            item.Click += (_, _) => { if (_canEdit && !_isBusy) apply(target); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem clear = new() { Text = "割り当てを解除", IsEnabled = Enumerable.Range(0, 4).Any(isAssigned) };
        clear.Click += (_, _) => { if (_canEdit && !_isBusy) apply(null); };
        menu.Items.Add(clear);
        _assignmentMenu = menu;
        menu.ShowAt(anchor);
    }

    private void AssignmentChanged() { HasUnsavedChanges = true; RefreshEditor(); }
    private void ShowConflict() => ShowStatus(InfoBarSeverity.Warning, "別のプレイヤーに割り当て済みです。プレイヤーを切り替えて編集してください。");
    private PlayerInputBindings GetPlayer(int player) => player == 1 ? _bindings.Player1 : _bindings.Player2;
    private static string ShortInputLabel(ControllerInputBinding input) => input.InputType == ControllerInputType.Button
        ? $"B{input.InputIndex + 1}" : $"S{input.InputIndex + 1} {new[] { "", "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" }[Math.Clamp(input.InputValue, 0, 8)]}";

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        if (busy) _assignmentMenu?.Hide();
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool enabled = _canEdit && !_isBusy;
        DeviceDiagram.IsEnabled = enabled;
        PlayerPicker.IsEnabled = enabled;
        DeviceTypeBox.IsEnabled = enabled;
        ControllerBox.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled && HasUnsavedChanges;
        ReloadButton.IsEnabled = !_isBusy;
    }

    private void ShowStatus(InfoBarSeverity severity, string text)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = text;
        StatusBar.IsOpen = true;
    }
}
