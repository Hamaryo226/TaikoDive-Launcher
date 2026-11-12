using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

internal enum KeyboardAssignmentResult
{
    Unchanged,
    Changed,
    Conflict,
}

internal enum ControllerAssignmentResult { Unchanged, Changed, Conflict }

internal static class InputBindingsEditor
{
    public static ControllerAssignmentResult AssignController(InputBindings bindings, int player, ControllerInputBinding input, int? slot)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(input);
        if (player is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(player));
        if (slot is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        PlayerInputBindings selected = player == 1 ? bindings.Player1 : bindings.Player2;
        PlayerInputBindings other = player == 1 ? bindings.Player2 : bindings.Player1;
        if (slot.HasValue && Enumerable.Range(0, 4).Any(index => other.GetControllers(index).Any(existing => ControllerBindingsEqual(existing, input))))
            return ControllerAssignmentResult.Conflict;

        bool changed = false;
        for (int index = 0; index < 4; index++)
        {
            ControllerInputBinding[] current = selected.GetControllers(index);
            bool kept = false;
            List<ControllerInputBinding> updated = [];
            foreach (ControllerInputBinding existing in current)
            {
                if (!ControllerBindingsEqual(existing, input)) updated.Add(existing);
                else if (index == slot && !kept) { updated.Add(existing); kept = true; }
            }
            if (index == slot && !kept) updated.Add(input);
            if (!current.SequenceEqual(updated))
            {
                selected.SetControllers(index, updated.ToArray());
                changed = true;
            }
        }
        return changed ? ControllerAssignmentResult.Changed : ControllerAssignmentResult.Unchanged;
    }

    // A null slot clears this key for the selected player. Other players are never edited.
    public static KeyboardAssignmentResult AssignKeyboard(InputBindings bindings, int player, int keyCode, int? slot)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (player is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }
        if (slot is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        PlayerInputBindings selected = player == 1 ? bindings.Player1 : bindings.Player2;
        PlayerInputBindings other = player == 1 ? bindings.Player2 : bindings.Player1;
        if (slot.HasValue && Enumerable.Range(0, 4).Any(index => other.GetKeys(index).Contains(keyCode)))
        {
            return KeyboardAssignmentResult.Conflict;
        }

        bool changed = false;
        for (int index = 0; index < 4; index++)
        {
            int[] current = selected.GetKeys(index);
            bool keepKey = index == slot;
            bool keptKey = false;
            int[] updated = current.Where(key =>
            {
                if (key != keyCode)
                {
                    return true;
                }
                if (!keepKey || keptKey)
                {
                    return false;
                }
                keptKey = true;
                return true;
            }).ToArray();
            if (keepKey && !keptKey)
            {
                updated = [.. updated, keyCode];
            }
            if (!current.SequenceEqual(updated))
            {
                selected.SetKeys(index, updated);
                changed = true;
            }
        }

        return changed ? KeyboardAssignmentResult.Changed : KeyboardAssignmentResult.Unchanged;
    }

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
