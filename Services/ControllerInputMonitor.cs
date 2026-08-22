using System.Runtime.Versioning;
using TaikoDiveLauncher.Models;
using Windows.Gaming.Input;

namespace TaikoDiveLauncher.Services;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class ControllerInputMonitor
{
    private sealed class ControllerState
    {
        public required RawGameController Controller { get; init; }
        public required bool[] CurrentButtons { get; set; }
        public required bool[] PreviousButtons { get; set; }
        public required GameControllerSwitchPosition[] CurrentSwitches { get; set; }
        public required GameControllerSwitchPosition[] PreviousSwitches { get; set; }
        public required double[] Axes { get; init; }
        public bool Initialized { get; set; }
    }

    private readonly List<ControllerState> _states = [];

    public IReadOnlyList<string> Update()
    {
        IReadOnlyList<RawGameController> controllers = RawGameController.RawGameControllers;
        _states.RemoveAll(state => IndexOfReference(controllers, state.Controller) < 0);

        for (int index = 0; index < controllers.Count; index++)
        {
            RawGameController controller = controllers[index];
            ControllerState? state = _states.FirstOrDefault(item => ReferenceEquals(item.Controller, controller));
            if (state is null)
            {
                state = new ControllerState
                {
                    Controller = controller,
                    CurrentButtons = new bool[controller.ButtonCount],
                    PreviousButtons = new bool[controller.ButtonCount],
                    CurrentSwitches = new GameControllerSwitchPosition[controller.SwitchCount],
                    PreviousSwitches = new GameControllerSwitchPosition[controller.SwitchCount],
                    Axes = new double[controller.AxisCount],
                };
                _states.Insert(index, state);
            }
            else
            {
                int currentIndex = _states.IndexOf(state);
                if (currentIndex != index)
                {
                    _states.RemoveAt(currentIndex);
                    _states.Insert(index, state);
                }
            }
            try
            {
                ReadState(state);
            }
            catch
            {
                state.Initialized = false;
            }
        }

        return _states.Select(state => state.Controller.DisplayName ?? "Controller").ToArray();
    }

    public void ResetEdges()
    {
        Update();
        foreach (ControllerState state in _states)
        {
            Array.Copy(state.CurrentButtons, state.PreviousButtons, state.CurrentButtons.Length);
            Array.Copy(state.CurrentSwitches, state.PreviousSwitches, state.CurrentSwitches.Length);
        }
    }

    public bool TryGetPressed(out ControllerInputBinding? binding)
    {
        Update();
        for (int deviceIndex = 0; deviceIndex < _states.Count; deviceIndex++)
        {
            ControllerState state = _states[deviceIndex];
            for (int inputIndex = 0; inputIndex < state.CurrentButtons.Length; inputIndex++)
            {
                if (state.CurrentButtons[inputIndex] && !state.PreviousButtons[inputIndex])
                {
                    binding = CreateBinding(state, deviceIndex, ControllerInputType.Button, inputIndex, 1);
                    return true;
                }
            }
            for (int inputIndex = 0; inputIndex < state.CurrentSwitches.Length; inputIndex++)
            {
                GameControllerSwitchPosition current = state.CurrentSwitches[inputIndex];
                if (current != GameControllerSwitchPosition.Center && current != state.PreviousSwitches[inputIndex])
                {
                    binding = CreateBinding(state, deviceIndex, ControllerInputType.Switch, inputIndex, (int)current);
                    return true;
                }
            }
        }

        binding = null;
        return false;
    }

    private ControllerInputBinding CreateBinding(ControllerState state, int deviceIndex, ControllerInputType type, int inputIndex, int inputValue)
    {
        RawGameController controller = state.Controller;
        int ordinal = _states.Take(deviceIndex).Count(previous =>
            previous.Controller.HardwareVendorId == controller.HardwareVendorId
            && previous.Controller.HardwareProductId == controller.HardwareProductId);
        return new ControllerInputBinding
        {
            VendorId = controller.HardwareVendorId,
            ProductId = controller.HardwareProductId,
            DeviceIndex = deviceIndex,
            DeviceOrdinal = ordinal,
            DeviceName = controller.DisplayName ?? "Controller",
            InputType = type,
            InputIndex = inputIndex,
            InputValue = inputValue,
        };
    }

    private static void ReadState(ControllerState state)
    {
        (state.PreviousButtons, state.CurrentButtons) = (state.CurrentButtons, state.PreviousButtons);
        (state.PreviousSwitches, state.CurrentSwitches) = (state.CurrentSwitches, state.PreviousSwitches);
        state.Controller.GetCurrentReading(state.CurrentButtons, state.CurrentSwitches, state.Axes);
        if (!state.Initialized)
        {
            Array.Copy(state.CurrentButtons, state.PreviousButtons, state.CurrentButtons.Length);
            Array.Copy(state.CurrentSwitches, state.PreviousSwitches, state.CurrentSwitches.Length);
            state.Initialized = true;
        }
    }

    private static int IndexOfReference(IReadOnlyList<RawGameController> controllers, RawGameController target)
    {
        for (int index = 0; index < controllers.Count; index++)
        {
            if (ReferenceEquals(controllers[index], target))
            {
                return index;
            }
        }
        return -1;
    }
}
