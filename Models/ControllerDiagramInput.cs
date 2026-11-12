namespace TaikoDiveLauncher.Models;

internal sealed record ControllerDiagramInput(string Label, string Position, ControllerInputBinding? Binding);

internal sealed record ControllerDeviceOption(string Id, string Label, IReadOnlyList<ControllerDiagramInput> Inputs);
