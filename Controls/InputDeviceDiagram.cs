using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;


namespace TaikoDiveLauncher.Controls;

// どの操作タイプも、模式図のキーキャップを標準 Button で編集できる。
public sealed partial class InputDeviceDiagram : UserControl
{
    private const double KeyboardHeight = 350;
    private const double ControllerWidth = 720;
    private const double ControllerHeight = 282;
    private readonly Canvas _stage = new() { Width = 1000, Height = 350 };
    private PlayerInputBindings _bindings = new();
    private int _deviceType = -1;
    private readonly Dictionary<int, Button> _keys = [];
    private readonly HashSet<int> _extraKeys = [];
    private IReadOnlyList<ControllerDiagramInput> _controllerInputs = [];
    private readonly List<(ControllerDiagramInput Input, Button Button, bool Region, bool ShowAssignment)> _controllerButtons = [];

    public event Action<int, Button>? KeyInvoked;
    public event Action<ControllerInputBinding, Button>? ControllerInvoked;

    public InputDeviceDiagram()
    {
        InitializeComponent();
        Content = new Viewbox { Child = _stage, Stretch = Stretch.Uniform, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
    }

    internal void Update(PlayerInputBindings bindings, int deviceType, IReadOnlyList<ControllerDiagramInput> controllerInputs)
    {
        _bindings = bindings;
        ((Viewbox)Content).MaxWidth = deviceType == 0 ? 1100 : 605;
        ((Viewbox)Content).HorizontalAlignment = deviceType == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        bool inputsChanged = !ReferenceEquals(_controllerInputs, controllerInputs);
        _controllerInputs = controllerInputs;
        if (_deviceType != deviceType || (deviceType != 0 && inputsChanged))
        {
            _deviceType = deviceType;
            BuildDiagram();
        }
        if (deviceType == 0) UpdateExtraKeyboardKeys();
        RefreshAppearance();
    }

    private void BuildDiagram()
    {
        _stage.Children.Clear();
        _keys.Clear();
        _extraKeys.Clear();
        _controllerButtons.Clear();
        _stage.Width = _deviceType == 0 ? 1056 : ControllerWidth;
        _stage.Height = _deviceType == 0 ? KeyboardHeight : ControllerHeight;
        if (_deviceType == 0) BuildKeyboard();
        else if (_deviceType == 1) BuildGamepad();
        else BuildDrum();
    }

    private void BuildKeyboard()
    {
        AddBody(0, 0, 1056, KeyboardHeight, 14);
        // JIS 配列。数値は Setting.json と同じ DXLib のキーコード。
        AddKeyRow(12, 12, [1, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 87, 88]);
        AddKeyRow(718, 12, [183, 70, 197]);
        AddKeyRow(12, 68, [41, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 125, 14]);
        AddKeyRow(12, 124, [15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28]);
        AddKeyRow(12, 180, [58, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 43]);
        AddKeyRow(12, 236, [42, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54]);
        AddKeyRow(12, 292, [29, 219, 56, 123, 57, 121, 112, 184, 220, 221, 157]);
        AddKeyRow(718, 68, [210, 199, 201]);
        AddKeyRow(718, 124, [211, 207, 209]);
        AddKey(200, 764, 236, 42);
        AddKeyRow(718, 292, [203, 208, 205]);
        AddKeyRow(862, 68, [69, 181, 55, 74]);
        AddKeyRow(862, 124, [71, 72, 73, 78]);
        AddKeyRow(862, 180, [75, 76, 77]);
        AddKeyRow(862, 236, [79, 80, 81, 156]);
        AddKey(82, 862, 292, 88);
        AddKey(83, 954, 292, 42);
    }

    private void UpdateExtraKeyboardKeys()
    {
        // 保存済みのキーが JIS 配列の図にない場合も、同じキーキャップで編集できる。
        int[] assigned = Enumerable.Range(0, 4).SelectMany(_bindings.GetKeys)
            .Where(code => !_keys.ContainsKey(code) || _extraKeys.Contains(code)).Distinct().Order().ToArray();
        foreach (int code in _extraKeys.Except(assigned).ToArray())
        {
            _stage.Children.Remove(_keys[code]);
            _keys.Remove(code);
            _extraKeys.Remove(code);
        }

        const int columns = 12;
        for (int i = 0; i < assigned.Length; i++)
        {
            int code = assigned[i];
            double x = 12 + i % columns * 86;
            double y = KeyboardHeight + 10 + i / columns * 52;
            if (_extraKeys.Add(code)) AddKey(code, x, y, 78);
            else
            {
                Canvas.SetLeft(_keys[code], x);
                Canvas.SetTop(_keys[code], y);
            }
        }

        _stage.Height = assigned.Length == 0 ? KeyboardHeight : KeyboardHeight + 10 + Math.Ceiling(assigned.Length / (double)columns) * 52;
        if (!_keys.Values.Any(key => key.IsTabStop)) _keys.Values.First().IsTabStop = true;
    }

    private void AddKeyRow(double x, double y, int[] codes)
    {
        foreach (int code in codes)
        {
            double width = code switch { 57 => 92, 42 or 54 => 88, 15 or 58 => 65, 28 => 65, _ => 42 };
            AddKey(code, x, y, width);
            x += width + 4;
        }
    }

    private void AddKey(int code, double x, double y, double width)
    {
        Button key = new()
        {
            Width = width, Height = 44, Padding = new Thickness(1), CornerRadius = new CornerRadius(5),
            IsTabStop = _keys.Count == 0,
        };
        ToolTipService.SetToolTip(key, InputLabelService.KeyLabel(code));
        key.Click += (_, _) => KeyInvoked?.Invoke(code, key);
        key.GotFocus += (_, _) =>
        {
            foreach (Button candidate in _keys.Values) candidate.IsTabStop = candidate == key;
        };
        key.KeyDown += KeyboardKey_KeyDown;
        _keys.Add(code, key);
        Place(key, x, y);
    }

    private void KeyboardKey_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Button current || e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)) return;
        bool horizontal = e.Key is VirtualKey.Left or VirtualKey.Right;
        bool forward = e.Key is VirtualKey.Right or VirtualKey.Down;
        double x = Canvas.GetLeft(current) + current.Width / 2;
        double y = Canvas.GetTop(current) + current.Height / 2;
        Button? next = _keys.Values.Where(key => key != current)
            .Select(key => new { Key = key, X = Canvas.GetLeft(key) + key.Width / 2 - x, Y = Canvas.GetTop(key) + key.Height / 2 - y })
            .Where(candidate => (forward ? 1 : -1) * (horizontal ? candidate.X : candidate.Y) > 1)
            .OrderBy(candidate => horizontal ? Math.Abs(candidate.Y) * 100 + Math.Abs(candidate.X) : Math.Abs(candidate.Y) + Math.Abs(candidate.X) * 2)
            .Select(candidate => candidate.Key).FirstOrDefault();
        next?.Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private void AddControllerSchematic(string resource)
    {
        FrameworkElement schematic = (FrameworkElement)((DataTemplate)Resources[resource]).LoadContent();
        Place(schematic, 0, 0);
    }

    private void AddExtraInputs(IReadOnlyList<ControllerDiagramInput> inputs)
    {
        const int columns = 9;
        const double rowHeight = 48;
        for (int index = 0; index < inputs.Count; index++)
            AddControllerButton(inputs[index], 8 + index % columns * 79, ControllerHeight + 8 + index / columns * rowHeight, 72, 42);
        if (inputs.Count > 0) _stage.Height = ControllerHeight + 8 + Math.Ceiling(inputs.Count / (double)columns) * rowHeight;
    }

    private Button AddControllerButton(ControllerDiagramInput input, double x, double y, double width, double height,
        bool region = false, bool showAssignment = true)
    {
        Button button = new()
        {
            Width = width, Height = height, Padding = new Thickness(1), CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) =>
        {
            if (input.Binding is { } binding) ControllerInvoked?.Invoke(binding, button);
        };
        _controllerButtons.Add((input, button, region, showAssignment));
        Place(button, x, y);
        return button;
    }

    private void AddBody(double x, double y, double width, double height, double radius)
    {
        Border body = new()
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(2),
            Style = (Style)Resources["DiagramBodyStyle"],
            IsHitTestVisible = false,
        };
        Place(body, x, y);
    }


    private void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _stage.Children.Add(element);
    }

    private void RefreshAppearance()
    {
        foreach ((int code, Button key) in _keys)
        {
            int slot = Enumerable.Range(0, 4).FirstOrDefault(s => _bindings.GetKeys(s).Contains(code), -1);
            string label = ShortKeyLabel(code);
            string action = InputLabelService.HitLabel(slot);
            key.Content = new TextBlock { Text = label + (slot < 0 ? "" : "\n" + action), FontSize = 11, TextAlignment = TextAlignment.Center };
            ApplyColor(key, slot);
            string description = $"{InputLabelService.KeyLabel(code)} {(slot < 0 ? "未設定" : action)}。割り当てを変更";
            AutomationProperties.SetName(key, description);
            ToolTipService.SetToolTip(key, description);
        }
        foreach ((ControllerDiagramInput input, Button button, bool region, bool showAssignment) in _controllerButtons)
        {
            int slot = input.Binding is null ? -1 : Enumerable.Range(0, 4).FirstOrDefault(index =>
                _bindings.GetControllers(index).Any(existing => InputBindingsEditor.ControllerBindingsEqual(existing, input.Binding)), -1);
            string assignment = InputLabelService.HitLabel(slot);
            button.Content = new TextBlock
            {
                Text = input.Binding is null ? "" : input.Label + (slot >= 0 && showAssignment ? "\n" + assignment : ""),
                FontSize = region && !showAssignment ? 10 : 12,
                TextAlignment = TextAlignment.Center,
            };
            if (!region) ApplyColor(button, slot);
            button.IsEnabled = input.Binding is not null;
            string description = input.Label + "：" + (assignment.Length > 0 ? assignment : "未設定") + "。割り当てを変更";
            AutomationProperties.SetName(button, description);
            ToolTipService.SetToolTip(button, input.Binding is null ? "コントローラーを接続してください" : description);
        }
    }
    private void ApplyColor(Button button, int slot)
    {
        if (slot < 0)
        {
            button.ClearValue(StyleProperty);
        }
        else
        {
            string style = slot is 1 or 2 ? "DonKeyStyle" : "KaKeyStyle";
            button.Style = (Style)Resources[style];
        }
    }

    private static string ShortKeyLabel(int code) => code switch
    {
        14 => "BS", 41 => "半/全", 58 => "Caps", 183 => "PrtSc", 197 => "Pause", 70 => "ScrLk", 69 => "Num",
        210 => "Ins", 211 => "Del", 201 => "PgUp", 209 => "PgDn", 123 => "無変換", 121 => "変換",
        >= 71 and <= 83 or 55 or 74 or 78 or 156 or 181 => InputLabelService.KeyLabel(code).Replace("テンキー", "N"),
        _ => InputLabelService.KeyLabel(code),
    };
}
