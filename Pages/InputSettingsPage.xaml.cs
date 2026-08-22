using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.System;

namespace TaikoDiveLauncher.Pages;

public sealed partial class InputSettingsPage : Page
{
    private enum CaptureInputKind
    {
        None,
        Keyboard,
        Controller,
    }

    private sealed record BindingRemovalChoice(
        string Label,
        int? KeyCode = null,
        ControllerInputBinding? Controller = null);

    private static readonly string[] ActionLabels = ["カッ（左）", "ドン（左）", "ドン（右）", "カッ（右）"];
    private readonly InputBindingsStore _store = new();
    private readonly ControllerInputMonitor _controllerMonitor = new();
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private InputBindings _bindings = new();
    private CaptureInputKind _captureInputKind;
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

    private async void AddKeyboardBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        await CaptureBindingAsync(row, CaptureInputKind.Keyboard);
    }

    private async void AddControllerBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        await CaptureBindingAsync(row, CaptureInputKind.Controller);
    }

    private async Task CaptureBindingAsync(InputBindingRow row, CaptureInputKind inputKind)
    {
        _captureInputKind = inputKind;
        _capturedKey = null;
        _capturedController = null;
        bool capturesKeyboard = inputKind == CaptureInputKind.Keyboard;
        CaptureDialog.Title = capturesKeyboard ? "キーボードのキーを追加" : "外部コントローラー入力を追加";
        CaptureTargetText.Text = $"{row.Player}P {row.Label}";
        CaptureInstructionText.Text = capturesKeyboard
            ? "割り当てるキーボードのキーを押してください。コントローラー入力はこの画面では検出しません。"
            : "割り当てるコントローラーのボタン、または十字キー方向を押してください。キーボード入力はこの画面では登録しません。";
        CaptureDialog.XamlRoot = XamlRoot;
        if (!capturesKeyboard)
        {
            _controllerMonitor.ResetEdges();
            _captureTimer.Start();
        }
        try
        {
            await CaptureDialog.ShowAsync();
        }
        finally
        {
            _captureTimer.Stop();
            _captureInputKind = CaptureInputKind.None;
        }

        if (_capturedKey is int key)
        {
            AddKeyboardBinding(row, key);
        }
        else if (_capturedController is not null)
        {
            AddControllerBinding(row, _capturedController);
        }
    }

    private async void RemoveKeyboardBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        await RemoveBindingAsync(row, CaptureInputKind.Keyboard);
    }

    private async void RemoveControllerBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InputBindingRow row })
        {
            return;
        }

        await RemoveBindingAsync(row, CaptureInputKind.Controller);
    }

    private async Task RemoveBindingAsync(InputBindingRow row, CaptureInputKind inputKind)
    {
        PlayerInputBindings player = GetPlayer(row.Player);
        bool removesKeyboard = inputKind == CaptureInputKind.Keyboard;
        IReadOnlyList<BindingRemovalChoice> choices = removesKeyboard
            ? player.GetKeys(row.Slot)
                .Select(key => new BindingRemovalChoice(InputLabelService.KeyLabel(key), KeyCode: key))
                .ToArray()
            : player.GetControllers(row.Slot)
                .Select(controller => new BindingRemovalChoice(
                    InputLabelService.ControllerLabel(controller),
                    Controller: controller))
                .ToArray();
        if (choices.Count == 0)
        {
            return;
        }

        ComboBox picker = new()
        {
            Header = "削除する割り当て",
            ItemsSource = choices,
            DisplayMemberPath = nameof(BindingRemovalChoice.Label),
            MinWidth = 320,
            SelectedIndex = 0,
        };
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"{row.Player}P {row.Label} の{(removesKeyboard ? "キーボード" : "外部コントローラー")}割り当てから1件だけ削除します。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(picker);
        ContentDialog dialog = new()
        {
            Title = "割り当てを1件削除",
            Content = content,
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || picker.SelectedItem is not BindingRemovalChoice selected)
        {
            return;
        }

        bool removed = removesKeyboard && selected.KeyCode is int keyCode
            ? InputBindingsEditor.RemoveKeyboard(player, row.Slot, keyCode)
            : selected.Controller is not null
                && InputBindingsEditor.RemoveController(player, row.Slot, selected.Controller);
        if (!removed)
        {
            return;
        }

        RefreshRows();
        ShowStatus(
            InfoBarSeverity.Informational,
            $"{selected.Label} を1件削除しました。ほかの割り当ては保持しています。保存すると反映されます。");
    }

    private void CaptureDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        CaptureFocusSurface.Focus(FocusState.Programmatic);
    }

    private void CaptureDialog_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_captureInputKind != CaptureInputKind.Keyboard || e.Key == VirtualKey.Escape)
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
        if (_captureInputKind == CaptureInputKind.Controller
            && _controllerMonitor.TryGetPressed(out ControllerInputBinding? input)
            && input is not null)
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
        InputBindingsEditor.AddKeyboard(player, row.Slot, keyCode);
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
        InputBindingsEditor.AddController(player, row.Slot, input);
        RefreshRows();
        ShowStatus(InfoBarSeverity.Success, $"{InputLabelService.ControllerLabel(input)} を追加しました。保存すると反映されます。");
    }

    private bool IsKeyboardUsedElsewhere(InputBindingRow target, int keyCode)
    {
        return AllRows().Any(row => row != target && GetPlayer(row.Player).GetKeys(row.Slot).Contains(keyCode));
    }

    private bool IsControllerUsedElsewhere(InputBindingRow target, ControllerInputBinding input)
    {
        return AllRows().Any(row => row != target && GetPlayer(row.Player).GetControllers(row.Slot)
            .Any(existing => InputBindingsEditor.ControllerBindingsEqual(existing, input)));
    }

    private void RefreshRows()
    {
        foreach (InputBindingRow row in AllRows())
        {
            PlayerInputBindings player = GetPlayer(row.Player);
            int[] keys = player.GetKeys(row.Slot);
            ControllerInputBinding[] controllers = player.GetControllers(row.Slot);
            row.KeyboardValue = keys.Length == 0
                ? "未設定"
                : string.Join("  /  ", keys.Select(InputLabelService.KeyLabel));
            row.ControllerValue = controllers.Length == 0
                ? "未設定"
                : string.Join("  /  ", controllers.Select(InputLabelService.ControllerLabel));
            row.HasKeyboardBindings = keys.Length > 0;
            row.HasControllerBindings = controllers.Length > 0;
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
