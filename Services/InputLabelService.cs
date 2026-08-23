using System.Runtime.InteropServices;
using TaikoDiveLauncher.Models;
using Windows.System;

namespace TaikoDiveLauncher.Services;

internal static class InputLabelService
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;

    private static readonly IReadOnlyDictionary<int, string> KeyLabels = new Dictionary<int, string>
    {
        [1] = "Esc",
        [2] = "1", [3] = "2", [4] = "3", [5] = "4", [6] = "5", [7] = "6", [8] = "7", [9] = "8", [10] = "9", [11] = "0",
        [12] = "-", [13] = "^", [14] = "BackSpace", [15] = "Tab",
        [16] = "Q", [17] = "W", [18] = "E", [19] = "R", [20] = "T", [21] = "Y", [22] = "U", [23] = "I", [24] = "O", [25] = "P",
        [26] = "@", [27] = "[", [28] = "Enter", [29] = "LCtrl",
        [30] = "A", [31] = "S", [32] = "D", [33] = "F", [34] = "G", [35] = "H", [36] = "J", [37] = "K", [38] = "L",
        [39] = ";", [40] = ":", [41] = "半角/全角", [42] = "LShift", [43] = "]",
        [44] = "Z", [45] = "X", [46] = "C", [47] = "V", [48] = "B", [49] = "N", [50] = "M",
        [51] = ",", [52] = ".", [53] = "/", [54] = "RShift", [55] = "テンキー*", [56] = "LAlt", [57] = "Space",
        [58] = "CapsLock", [59] = "F1", [60] = "F2", [61] = "F3", [62] = "F4", [63] = "F5", [64] = "F6", [65] = "F7", [66] = "F8", [67] = "F9", [68] = "F10",
        [69] = "NumLock", [70] = "ScrollLock", [71] = "テンキー7", [72] = "テンキー8", [73] = "テンキー9", [74] = "テンキー-",
        [75] = "テンキー4", [76] = "テンキー5", [77] = "テンキー6", [78] = "テンキー+", [79] = "テンキー1", [80] = "テンキー2", [81] = "テンキー3", [82] = "テンキー0", [83] = "テンキー.",
        [87] = "F11", [88] = "F12", [112] = "かな", [121] = "変換", [123] = "無変換", [125] = "\\",
        [156] = "テンキーEnter", [157] = "RCtrl", [181] = "テンキー/", [183] = "PrintScreen", [184] = "RAlt", [197] = "Pause",
        [199] = "Home", [200] = "↑", [201] = "PageUp", [203] = "←", [205] = "→", [207] = "End", [208] = "↓", [209] = "PageDown", [210] = "Insert", [211] = "Delete",
        [219] = "LWin", [220] = "RWin", [221] = "Menu",
    };

    public static int? ToDxLibKeyCode(VirtualKey key, uint scanCode, bool isExtendedKey)
    {
        if (key == VirtualKey.Pause)
        {
            return 197;
        }

        if (scanCode == 0)
        {
            uint mappedScanCode = MapVirtualKey((uint)key, MapVirtualKeyToScanCodeExtended);
            scanCode = mappedScanCode & 0xff;
            isExtendedKey = isExtendedKey
                || (mappedScanCode & 0xff00) != 0
                || IsExtendedVirtualKey(key);
            if (scanCode == 0)
            {
                return null;
            }
        }

        int result = (int)(scanCode & 0xff);
        if (isExtendedKey)
        {
            result |= 0x80;
        }
        return result is >= 0 and <= 255 ? result : null;
    }

    public static string KeyLabel(int keyCode) => KeyLabels.TryGetValue(keyCode, out string? label) ? label : $"Key({keyCode})";

    public static string ControllerLabel(ControllerInputBinding input)
    {
        string device = string.IsNullOrWhiteSpace(input.DeviceName) ? "Controller" : input.DeviceName;
        return input.InputType == ControllerInputType.Button
            ? $"{device}  B{input.InputIndex + 1}"
            : $"{device}  S{input.InputIndex + 1} {SwitchLabel(input.InputValue)}";
    }

    private static string SwitchLabel(int value) => value switch
    {
        1 => "↑", 2 => "↗", 3 => "→", 4 => "↘", 5 => "↓", 6 => "↙", 7 => "←", 8 => "↖", _ => value.ToString(),
    };

    private static bool IsExtendedVirtualKey(VirtualKey key) => key is
        VirtualKey.Home or
        VirtualKey.End or
        VirtualKey.PageUp or
        VirtualKey.PageDown or
        VirtualKey.Left or
        VirtualKey.Up or
        VirtualKey.Right or
        VirtualKey.Down or
        VirtualKey.Insert or
        VirtualKey.Delete;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
