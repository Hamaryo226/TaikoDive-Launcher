namespace TaikoDiveLauncher.Models;

using System.ComponentModel;

public enum ControllerInputType
{
    Button,
    Switch,
}

public sealed class ControllerInputBinding
{
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public int DeviceIndex { get; set; }
    public int DeviceOrdinal { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public ControllerInputType InputType { get; set; }
    public int InputIndex { get; set; }
    public int InputValue { get; set; } = 1;
}

public sealed class PlayerInputBindings
{
    public int[] KaLeft { get; set; } = [];
    public int[] DonLeft { get; set; } = [];
    public int[] DonRight { get; set; } = [];
    public int[] KaRight { get; set; } = [];
    public ControllerInputBinding[] KaLeftControllers { get; set; } = [];
    public ControllerInputBinding[] DonLeftControllers { get; set; } = [];
    public ControllerInputBinding[] DonRightControllers { get; set; } = [];
    public ControllerInputBinding[] KaRightControllers { get; set; } = [];

    public int[] GetKeys(int slot) => slot switch
    {
        0 => KaLeft,
        1 => DonLeft,
        2 => DonRight,
        _ => KaRight,
    };

    public void SetKeys(int slot, int[] keys)
    {
        switch (slot)
        {
            case 0: KaLeft = keys; break;
            case 1: DonLeft = keys; break;
            case 2: DonRight = keys; break;
            default: KaRight = keys; break;
        }
    }

    public ControllerInputBinding[] GetControllers(int slot) => slot switch
    {
        0 => KaLeftControllers,
        1 => DonLeftControllers,
        2 => DonRightControllers,
        _ => KaRightControllers,
    };

    public void SetControllers(int slot, ControllerInputBinding[] controllers)
    {
        switch (slot)
        {
            case 0: KaLeftControllers = controllers; break;
            case 1: DonLeftControllers = controllers; break;
            case 2: DonRightControllers = controllers; break;
            default: KaRightControllers = controllers; break;
        }
    }
}

public sealed class InputBindings
{
    public PlayerInputBindings Player1 { get; set; } = new()
    {
        KaLeft = [32],
        DonLeft = [33, 34],
        DonRight = [35, 36],
        KaRight = [37],
    };

    public PlayerInputBindings Player2 { get; set; } = new()
    {
        KaLeft = [44],
        DonLeft = [45],
        DonRight = [46],
        KaRight = [47],
    };
}

public sealed class InputBindingRow : INotifyPropertyChanged
{
    private string _keyboardValue = "未設定";
    private string _controllerValue = "未設定";
    private bool _hasKeyboardBindings;
    private bool _hasControllerBindings;

    public required int Player { get; init; }
    public required int Slot { get; init; }
    public required string Label { get; init; }

    public string KeyboardValue
    {
        get => _keyboardValue;
        set
        {
            if (_keyboardValue == value)
            {
                return;
            }
            _keyboardValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeyboardValue)));
        }
    }

    public string ControllerValue
    {
        get => _controllerValue;
        set
        {
            if (_controllerValue == value)
            {
                return;
            }
            _controllerValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ControllerValue)));
        }
    }

    public bool HasKeyboardBindings
    {
        get => _hasKeyboardBindings;
        set
        {
            if (_hasKeyboardBindings == value)
            {
                return;
            }
            _hasKeyboardBindings = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasKeyboardBindings)));
        }
    }

    public bool HasControllerBindings
    {
        get => _hasControllerBindings;
        set
        {
            if (_hasControllerBindings == value)
            {
                return;
            }
            _hasControllerBindings = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasControllerBindings)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
