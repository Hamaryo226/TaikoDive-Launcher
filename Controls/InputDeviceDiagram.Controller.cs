using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;
using Windows.Foundation;

namespace TaikoDiveLauncher.Controls;

public sealed partial class InputDeviceDiagram
{
    private void BuildGamepad()
    {
        AddControllerSchematic("GamepadSchematic");
        var parts = new (string Id, Rect Bounds, bool Face, bool Round, bool Wide)[]
        {
            ("A", new(444, 166, 34, 34), true, true, false),
            ("B", new(480, 130, 34, 34), false, true, false),
            ("X", new(408, 130, 34, 34), true, true, false),
            ("Y", new(444, 94, 34, 34), false, true, false),
            ("LB", new(236, 58, 92, 27), false, false, true),
            ("RB", new(392, 58, 92, 27), false, false, true),
            ("LT", new(236, 24, 92, 29), false, false, true),
            ("RT", new(392, 24, 92, 29), false, false, true),
            ("↑", new(278, 131, 28, 28), false, false, false),
            ("↓", new(278, 185, 28, 28), true, false, false),
            ("←", new(251, 158, 28, 28), false, false, false),
            ("→", new(305, 158, 28, 28), true, false, false),
        };
        List<ControllerDiagramInput> remaining = [.. _controllerInputs];
        foreach (var part in parts)
        {
            var input = remaining.FirstOrDefault(item => item.Position == part.Id)
                ?? remaining.FirstOrDefault(item => item.Position.Length == 0 && item.Binding?.InputType == ControllerInputType.Button);
            if (input is not null) remaining.Remove(input);
            // The geometry is the visible button and its hit target, not a rectangular overlay.
            Geometry shape = part.Round
                ? new EllipseGeometry { Center = new(part.Bounds.Width / 2, part.Bounds.Height / 2), RadiusX = part.Bounds.Width / 2 - 2, RadiusY = part.Bounds.Height / 2 - 2 }
                : new RectangleGeometry { Rect = new(1, 1, part.Bounds.Width - 2, part.Bounds.Height - 2) };
            AddControllerRegion(input, part.Id, part.Bounds, shape, part.Face, part.Wide);
        }
        AddExtraInputs(remaining);
    }

    private void BuildDrum()
    {
        AddControllerSchematic("DrumSchematic");
        List<ControllerDiagramInput> remaining = [.. _controllerInputs];
        // Retain the physical button numbering when an unplugged device only exposes saved buttons.
        for (int index = 0; index < 4; index++)
        {
            bool rim = index is 0 or 3;
            bool left = index < 2;
            var input = remaining.FirstOrDefault(item => item.Binding is { InputType: ControllerInputType.Button } binding && binding.InputIndex == index);
            if (input is not null) remaining.Remove(input);
            double radiusX = rim ? 117 : 96;
            double radiusY = rim ? 106 : 87;
            const double centerX = 360;
            const double centerY = 122;
            string id = (rim ? "Rim" : "Face") + (left ? "Left" : "Right");
            Button button = AddControllerRegion(input, id,
                new(left ? centerX - radiusX : centerX, centerY - radiusY, radiusX, radiusY * 2),
                DrumRegionGeometry(radiusX, radiusY, left, rim), face: !rim, showAssignment: !rim);
            if (rim)
            {
                button.HorizontalContentAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                button.Padding = left ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 3, 0);
            }
        }
        AddExtraInputs(remaining);
    }

    private Button AddControllerRegion(ControllerDiagramInput? input, string id, Rect bounds, Geometry shape, bool face, bool showAssignment)
    {
        Button button = AddControllerButton(input ?? new ControllerDiagramInput(id, id, null),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, region: true, showAssignment: showAssignment);
        button.Style = (Style)Resources[face ? "FaceRegionStyle" : "RimRegionStyle"];
        button.Tag = shape;
        AutomationProperties.SetAutomationId(button, "ControllerRegion-" + id);
        if (input is null) AutomationProperties.SetAccessibilityView(button, AccessibilityView.Raw);
        return button;
    }

    private static PathGeometry DrumRegionGeometry(double radiusX, double radiusY, bool left, bool rim)
    {
        double centerX = left ? radiusX : 0;
        SweepDirection outer = left ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
        var figure = new PathFigure { StartPoint = new(centerX, 0), IsClosed = true };
        figure.Segments.Add(new ArcSegment { Point = new(centerX, radiusY * 2), Size = new(radiusX, radiusY), SweepDirection = outer });
        if (rim)
        {
            const double verticalInset = 19;
            figure.Segments.Add(new LineSegment { Point = new(centerX, radiusY * 2 - verticalInset) });
            figure.Segments.Add(new ArcSegment
            {
                Point = new(centerX, verticalInset), Size = new(radiusX - 21, radiusY - verticalInset),
                SweepDirection = left ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
