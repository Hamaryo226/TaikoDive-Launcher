using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Foundation;

namespace TaikoDiveLauncher.Controls;

// キーと入力領域は標準 Button。機器画像は操作タイプを表示するときだけ読み込む。
public sealed partial class InputDeviceDiagram : UserControl
{
    private static readonly string[] Labels = ["カッ（左）", "ドン（左）", "ドン（右）", "カッ（右）"];
    private readonly Canvas _stage = new() { Width = 1000, Height = 350 };
    private InputBindings _bindings = new();
    private int _player = 1;
    private int _deviceType = -1;
    private int _selectedSlot = 1;
    private readonly Dictionary<int, Button> _keys = [];
    private IReadOnlyList<ControllerDiagramInput> _controllerInputs = [];
    private readonly List<(ControllerDiagramInput Input, Button Button)> _controllerButtons = [];

    public event Action<int, Button>? KeyInvoked;
    public event Action<ControllerInputBinding, Button>? ControllerInvoked;

    public InputDeviceDiagram()
    {
        InitializeComponent();
        Content = new Viewbox { Child = _stage, Stretch = Stretch.Uniform, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
    }

    internal void Update(InputBindings bindings, int player, int deviceType, int selectedSlot, IReadOnlyList<ControllerDiagramInput>? controllerInputs = null)
    {
        _bindings = bindings;
        _player = player;
        _selectedSlot = selectedSlot;
        ((Viewbox)Content).MaxWidth = deviceType == 0 ? 1100 : 800;
        ((Viewbox)Content).HorizontalAlignment = deviceType == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        bool inputsChanged = !ReferenceEquals(_controllerInputs, controllerInputs);
        _controllerInputs = controllerInputs ?? [];
        if (_deviceType != deviceType || (deviceType != 0 && inputsChanged))
        {
            _deviceType = deviceType;
            BuildDiagram();
        }
        RefreshAppearance();
    }

    private void BuildDiagram()
    {
        _stage.Children.Clear();
        _keys.Clear();
        _controllerButtons.Clear();
        _stage.Width = _deviceType == 0 ? 1056 : 720;
        _stage.Height = _deviceType == 0 ? 350 : 300;
        if (_deviceType == 0) BuildKeyboard();
        else if (_deviceType == 1) BuildGamepad();
        else BuildDrum();
    }

    private void BuildKeyboard()
    {
        AddBody(0, 0, 1056, 350, 14);
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

    private void BuildGamepad()
    {
        AddDeviceImage("Gamepad.png");
        var positions = new (string Name, double X, double Y, double W, double H)[]
        {
            ("A", 653, 234, 62, 62), ("B", 702, 176, 62, 62),
            ("X", 597, 176, 62, 62), ("Y", 653, 116, 62, 62),
            ("LB", 138, 68, 148, 33), ("RB", 613, 68, 148, 33),
            ("LT", 173, 21, 76, 33), ("RT", 652, 21, 76, 33),
            ("L3", 288, 290, 84, 84), ("R3", 528, 290, 84, 84),
            ("View", 360, 179, 53, 30), ("Menu", 487, 179, 53, 30),
            ("↑", 201, 146, 42, 42), ("↓", 201, 232, 42, 42),
            ("←", 158, 189, 42, 42), ("→", 244, 189, 42, 42),
        };
        List<ControllerDiagramInput> remaining = [.. _controllerInputs];
        foreach (var position in positions)
        {
            ControllerDiagramInput? input = remaining.FirstOrDefault(item => item.Position == position.Name);
            // Unlabelled devices keep their actual input numbers on the generic layout.
            input ??= remaining.FirstOrDefault(item => item.Position.Length == 0 && item.Binding?.InputType == ControllerInputType.Button);
            input ??= new ControllerDiagramInput(position.Name, position.Name, null);
            remaining.Remove(input);
            AddControllerButton(input, position.X, position.Y, position.W, position.H);
        }
        AddExtraInputs(remaining);
    }

    private void BuildDrum()
    {
        AddDeviceImage("TaikoController.png");
        var buttons = _controllerInputs.Where(input => input.Binding?.InputType == ControllerInputType.Button)
            .OrderBy(input => input.Binding!.InputIndex).Take(4).ToArray();
        ControllerDiagramInput Input(int index) => index < buttons.Length ? buttons[index] : new ControllerDiagramInput($"B{index + 1}", "", null);
        AddShapedInput(Input(0), 196, 9, 254, 458,
            DrumGeometry(left: true, rim: true), 4, 190, 52);
        AddShapedInput(Input(3), 450, 9, 254, 458,
            DrumGeometry(left: false, rim: true), 198, 190, 52);
        AddShapedInput(Input(1), 224, 37, 224, 402,
            DrumGeometry(left: true, rim: false), 100, 160, 110);
        AddShapedInput(Input(2), 452, 37, 224, 402,
            DrumGeometry(left: false, rim: false), 14, 160, 110);
        AddExtraInputs(_controllerInputs.Except(buttons).ToArray());
    }

    private void AddDeviceImage(string file)
    {
        _stage.Width = 900;
        _stage.Height = 600;
        Image image = new()
        {
            Width = 900, Height = 600, Stretch = Stretch.Uniform,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/InputDevices/" + file)) { DecodePixelWidth = 1000 },
            IsHitTestVisible = false,
        };
        Place(image, 0, 0);
    }

    private void AddExtraInputs(IReadOnlyList<ControllerDiagramInput> inputs)
    {
        const int columns = 10;
        for (int index = 0; index < inputs.Count; index++)
            AddControllerButton(inputs[index], 10 + index % columns * 89, 616 + index / columns * 62, 82, 54);
        if (inputs.Count > 0) _stage.Height = 616 + Math.Ceiling(inputs.Count / (double)columns) * 62;
    }

    private Button AddControllerButton(ControllerDiagramInput input, double x, double y, double width, double height)
    {
        Button button = new()
        {
            Width = width, Height = height, Padding = new Thickness(2), CornerRadius = new CornerRadius(Math.Min(width, height) / 2),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) =>
        {
            if (input.Binding is { } binding) ControllerInvoked?.Invoke(binding, button);
        };
        _controllerButtons.Add((input, button));
        Place(button, x, y);
        return button;
    }

    private void AddShapedInput(ControllerDiagramInput input, double x, double y, double width, double height,
        Geometry geometry, double labelX, double labelY, double labelWidth)
    {
        Button target = AddControllerButton(input, x, y, width, height);
        target.Tag = geometry;
        target.Template = (ControlTemplate)Resources["DrumSectorTemplate"];
        Canvas labelCanvas = new();
        Border label = new() { Width = labelWidth, Style = (Style)Resources["DiagramCaptionStyle"] };
        Canvas.SetLeft(label, labelX);
        Canvas.SetTop(label, labelY);
        labelCanvas.Children.Add(label);
        target.Content = labelCanvas;
        target.UseSystemFocusVisuals = false;
    }

    private static PathGeometry DrumGeometry(bool left, bool rim)
    {
        double x = left ? (rim ? 254 : 224) : 0;
        double radiusX = rim ? 254 : 224;
        double radiusY = rim ? 229 : 201;
        PathFigure figure = new() { StartPoint = new Point(x, 0), IsClosed = true };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(x, radiusY * 2), Size = new Size(radiusX, radiusY),
            SweepDirection = left ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
        });
        if (rim)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(x, 430) });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(x, 28), Size = new Size(226, 201),
                SweepDirection = left ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            });
        }
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
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

    private void AddDecoration(string text, double x, double y, double size) =>
        Place(new TextBlock { Text = text, FontSize = size, IsHitTestVisible = false }, x, y);

    private void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _stage.Children.Add(element);
    }

    private void RefreshAppearance()
    {
        PlayerInputBindings player = _player == 1 ? _bindings.Player1 : _bindings.Player2;
        PlayerInputBindings other = _player == 1 ? _bindings.Player2 : _bindings.Player1;
        foreach ((int code, Button key) in _keys)
        {
            int slot = Enumerable.Range(0, 4).FirstOrDefault(s => player.GetKeys(s).Contains(code), -1);
            bool otherPlayer = slot < 0;
            if (otherPlayer) slot = Enumerable.Range(0, 4).FirstOrDefault(s => other.GetKeys(s).Contains(code), -1);
            string label = ShortKeyLabel(code);
            string action = slot < 0 ? "" : $"{(otherPlayer ? 3 - _player : _player)}P {(slot is 1 or 2 ? "ドン" : "カッ")}{(slot is 0 or 1 ? "左" : "右")}";
            key.Content = new TextBlock { Text = label + (action.Length > 0 ? "\n" + action : ""), FontSize = 10, TextAlignment = TextAlignment.Center };
            ApplyColor(key, slot, !otherPlayer && slot == _selectedSlot);
            // 他プレイヤーのキーも番号で区別し、上書きは既存の重複検査で防ぐ。
            string description = $"{InputLabelService.KeyLabel(code)} {(slot < 0 ? "未設定" : action)}。割り当てを変更";
            AutomationProperties.SetName(key, description);
            ToolTipService.SetToolTip(key, description);
        }
        foreach ((ControllerDiagramInput input, Button button) in _controllerButtons)
        {
            int slot = input.Binding is null ? -1 : Enumerable.Range(0, 4).FirstOrDefault(index =>
                player.GetControllers(index).Any(existing => InputBindingsEditor.ControllerBindingsEqual(existing, input.Binding)), -1);
            bool otherPlayer = false;
            if (slot < 0 && input.Binding is not null)
            {
                slot = Enumerable.Range(0, 4).FirstOrDefault(index =>
                    other.GetControllers(index).Any(existing => InputBindingsEditor.ControllerBindingsEqual(existing, input.Binding)), -1);
                otherPlayer = slot >= 0;
            }
            string assignment = slot < 0 ? "" : $"{(otherPlayer ? 3 - _player : _player)}P {(slot is 1 or 2 ? "ドン" : "カッ")}{(slot is 0 or 1 ? "左" : "右")}";
            string displayAssignment = _deviceType == 2 && slot >= 0
                ? $"{(slot is 1 or 2 ? "ドン" : "カッ")}{(slot is 0 or 1 ? "左" : "右")}\n{(otherPlayer ? 3 - _player : _player)}P"
                : assignment;
            TextBlock content = new() { Text = input.Label + (assignment.Length > 0 ? "\n" + displayAssignment : ""),
                FontSize = _deviceType == 2 ? (button.Width > 230 ? 14 : 16) : 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (button.Content is Canvas labelCanvas && labelCanvas.Children[0] is Border label) label.Child = content;
            else button.Content = content;
            button.Style = (Style)Resources[slot < 0 ? "DiagramControlStyle" : slot is 1 or 2 ? "DonKeyStyle" : "KaKeyStyle"];
            button.IsEnabled = input.Binding is not null;
            string description = input.Label + "：" + (assignment.Length > 0 ? assignment : "未設定") + "。割り当てを変更";
            AutomationProperties.SetName(button, description);
            ToolTipService.SetToolTip(button, input.Binding is null ? "コントローラーを接続してください" : description);
        }
    }
    private void ApplyColor(Button button, int slot, bool selected)
    {
        if (slot < 0)
        {
            button.ClearValue(StyleProperty);
        }
        else
        {
            string style = (selected ? "Selected" : "") + (slot is 1 or 2 ? "DonKeyStyle" : "KaKeyStyle");
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
