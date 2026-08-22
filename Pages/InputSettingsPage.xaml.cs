using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.System;

namespace TaikoDiveLauncher.Pages;

public sealed partial class InputSettingsPage : Page
{
    private static readonly string[] ActionLabels = ["カッ（左）", "ドン（左）", "ドン（右）", "カッ（右）"];
    private readonly InputBindingsStore _store = new();
    private readonly ControllerInputMonitor _controllerMonitor = new();
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private InputBindings _bindings = new();
    private InputBindingRow? _captureRow;
    private int? _capturedKey;
    private ControllerInputBinding? _capturedController;

    public IReadOnlyList<InputBindingRow> PlayerOneRows { get; } = CreateRows(1);
    public IReadOnlyList<InputBindingRow> PlayerTwoRows { get; } = CreateRows(2);

    private App AppInstance => (App)Application.Current;

    public InputSettingsPage()
    {
        InitializeComponent();
        Loaded += InputSettingsPage_Loaded;
        Unloaded += InputSettingsPage_Unloaded;
        CaptureDialog.Opened += CaptureDialog_Opened;
        _captureTimer.Tick += CaptureTimer_Tick;
    }

    private async void InputSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
        RefreshControllerStatus();
    }

    private void InputSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _captureTimer.Stop();
    }

    private async Task ReloadAsync()
    {
        TaikoDiveInstallation? installation = AppInstance.Context.Installation;
        if (installation is null)
        {
            SetEnabled(false);
            ShowStatus(InfoBarSeverity.Warning, "ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            return;
        }

        SetBusy(true);
        try
        {
            _bindings = await _store.LoadAsync(installation);
            RefreshRows();
            SetEnabled(true);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            SetEnabled(false);
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppInstance.Context.Installation is not { } installation)
        {
            ShowStatus(InfoBarSeverity.Warning, "ランチャーを TaikoDive.exe と同じフォルダーへ配置してください。");
            return;
        }

        SetBusy(true);
        try
        {
            await _store.SaveAsync(installation, _bindings);
            ShowStatus(InfoBarSeverity.Success, "入力設定を保存しました。次回のTaikoDive起動から反映されます。");
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
        await ReloadAsync();
        RefreshControllerStatus();
    }

    private void RefreshControllersButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshControllerStatus();
    }

    private async void AddBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        _captureRow = row;
        _capturedKey = null;
        _capturedController = null;
        CaptureTargetText.Text = $"{row.Player}P {row.Label} に追加";
        CaptureDialog.XamlRoot = XamlRoot;
        _controllerMonitor.ResetEdges();
        _captureTimer.Start();
        try
        {
            await CaptureDialog.ShowAsync();
        }
        finally
        {
            _captureTimer.Stop();
        }

        if (_capturedKey is int key)
        {
            AddKeyboardBinding(row, key);
        }
        else if (_capturedController is not null)
        {
            AddControllerBinding(row, _capturedController);
        }
        _captureRow = null;
    }

    private void ClearBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        PlayerInputBindings player = GetPlayer(row.Player);
        player.SetKeys(row.Slot, []);
        player.SetControllers(row.Slot, []);
        RefreshRows();
        ShowStatus(InfoBarSeverity.Informational, $"{row.Player}P {row.Label} の割り当てを消しました。保存すると反映されます。");
    }

    private void CaptureDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        CaptureFocusSurface.Focus(FocusState.Programmatic);
    }

    private void CaptureDialog_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            return;
        }

        int? keyCode = InputLabelService.ToDxLibKeyCode(e.Key, e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);
        if (keyCode is null)
        {
            return;
        }

        _capturedKey = keyCode;
        e.Handled = true;
        CaptureDialog.Hide();
    }

    private void CaptureTimer_Tick(object? sender, object e)
    {
        if (_controllerMonitor.TryGetPressed(out ControllerInputBinding? input) && input is not null)
        {
            _capturedController = input;
            CaptureDialog.Hide();
        }
    }

    private void AddKeyboardBinding(InputBindingRow row, int keyCode)
    {
        if (IsKeyboardUsedElsewhere(row, keyCode))
        {
            ShowStatus(InfoBarSeverity.Warning, $"{InputLabelService.KeyLabel(keyCode)} は別の打面に割り当て済みです。");
            return;
        }

        PlayerInputBindings player = GetPlayer(row.Player);
        player.SetKeys(row.Slot, player.GetKeys(row.Slot).Append(keyCode).Distinct().ToArray());
        RefreshRows();
        ShowStatus(InfoBarSeverity.Success, $"{InputLabelService.KeyLabel(keyCode)} を追加しました。保存すると反映されます。");
    }

    private void AddControllerBinding(InputBindingRow row, ControllerInputBinding input)
    {
        if (IsControllerUsedElsewhere(row, input))
        {
            ShowStatus(InfoBarSeverity.Warning, $"{InputLabelService.ControllerLabel(input)} は別の打面に割り当て済みです。");
            return;
        }

        PlayerInputBindings player = GetPlayer(row.Player);
        if (!player.GetControllers(row.Slot).Any(existing => ControllerBindingsEqual(existing, input)))
        {
            player.SetControllers(row.Slot, [.. player.GetControllers(row.Slot), input]);
        }
        RefreshRows();
        ShowStatus(InfoBarSeverity.Success, $"{InputLabelService.ControllerLabel(input)} を追加しました。保存すると反映されます。");
    }

    private bool IsKeyboardUsedElsewhere(InputBindingRow target, int keyCode)
    {
        return AllRows().Any(row => row != target && GetPlayer(row.Player).GetKeys(row.Slot).Contains(keyCode));
    }

    private bool IsControllerUsedElsewhere(InputBindingRow target, ControllerInputBinding input)
    {
        return AllRows().Any(row => row != target && GetPlayer(row.Player).GetControllers(row.Slot).Any(existing => ControllerBindingsEqual(existing, input)));
    }

    private static bool ControllerBindingsEqual(ControllerInputBinding left, ControllerInputBinding right)
    {
        return left.VendorId == right.VendorId
            && left.ProductId == right.ProductId
            && left.DeviceOrdinal == right.DeviceOrdinal
            && left.InputType == right.InputType
            && left.InputIndex == right.InputIndex
            && left.InputValue == right.InputValue;
    }

    private void RefreshRows()
    {
        foreach (InputBindingRow row in AllRows())
        {
            PlayerInputBindings player = GetPlayer(row.Player);
            List<string> labels = player.GetKeys(row.Slot).Select(InputLabelService.KeyLabel).ToList();
            labels.AddRange(player.GetControllers(row.Slot).Select(InputLabelService.ControllerLabel));
            row.Value = labels.Count == 0 ? "未設定" : string.Join("  /  ", labels);
        }
    }

    private void RefreshControllerStatus()
    {
        IReadOnlyList<string> devices = _controllerMonitor.Update();
        ControllerStatusText.Text = devices.Count == 0
            ? "検出されていません。接続後に更新してください。"
            : string.Join("  /  ", devices);
    }

    private PlayerInputBindings GetPlayer(int player) => player == 1 ? _bindings.Player1 : _bindings.Player2;

    private IEnumerable<InputBindingRow> AllRows() => PlayerOneRows.Concat(PlayerTwoRows);

    private static IReadOnlyList<InputBindingRow> CreateRows(int player)
    {
        return ActionLabels.Select((label, slot) => new InputBindingRow { Player = player, Slot = slot, Label = label }).ToArray();
    }

    private void SetBusy(bool isBusy)
    {
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetEnabled(bool isEnabled)
    {
        PlayersGrid.IsHitTestVisible = isEnabled;
        PlayersGrid.Opacity = isEnabled ? 1 : 0.55;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
