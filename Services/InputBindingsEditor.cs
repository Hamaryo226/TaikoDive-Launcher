using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

internal static class InputBindingsEditor
{
    public static bool AddKeyboard(PlayerInputBindings player, int slot, int keyCode)
    {
        int[] current = player.GetKeys(slot);
        if (current.Contains(keyCode))
        {
            return false;
        }

        player.SetKeys(slot, [.. current, keyCode]);
        return true;
    }

    public static bool RemoveKeyboard(PlayerInputBindings player, int slot, int keyCode)
    {
        int[] current = player.GetKeys(slot);
        int removeIndex = Array.IndexOf(current, keyCode);
        if (removeIndex < 0)
        {
            return false;
        }

        player.SetKeys(slot, current.Where((_, index) => index != removeIndex).ToArray());
        return true;
    }

    public static bool AddController(PlayerInputBindings player, int slot, ControllerInputBinding input)
    {
        ControllerInputBinding[] current = player.GetControllers(slot);
        if (current.Any(existing => ControllerBindingsEqual(existing, input)))
        {
            return false;
        }

        player.SetControllers(slot, [.. current, input]);
        return true;
    }

    public static bool RemoveController(PlayerInputBindings player, int slot, ControllerInputBinding input)
    {
        ControllerInputBinding[] current = player.GetControllers(slot);
        int removeIndex = Array.FindIndex(current, existing => ControllerBindingsEqual(existing, input));
        if (removeIndex < 0)
        {
            return false;
        }

        player.SetControllers(slot, current.Where((_, index) => index != removeIndex).ToArray());
        return true;
    }

    public static bool ControllerBindingsEqual(ControllerInputBinding left, ControllerInputBinding right)
    {
        return left.VendorId == right.VendorId
            && left.ProductId == right.ProductId
            && left.DeviceOrdinal == right.DeviceOrdinal
            && left.InputType == right.InputType
            && left.InputIndex == right.InputIndex
            && left.InputValue == right.InputValue;
    }
}
