using System.Runtime.InteropServices;
using Windows.System;

namespace TaikoDiveLauncher.Services;

internal sealed class KeyboardInputMonitor
{
    private const int FirstVirtualKey = 7;
    private const int LastVirtualKey = 254;
    private readonly bool[] _pressed = new bool[256];

    public void ResetEdges()
    {
        for (int virtualKey = FirstVirtualKey; virtualKey <= LastVirtualKey; virtualKey++)
        {
            _pressed[virtualKey] = IsPressed(virtualKey);
        }
    }

    public bool TryGetPressed(out int keyCode)
    {
        for (int virtualKey = FirstVirtualKey; virtualKey <= LastVirtualKey; virtualKey++)
        {
            short state = GetAsyncKeyState(virtualKey);
            bool isPressed = IsPressed(state);
            bool wasPressedSinceLastCheck = (state & 0x0001) != 0;
            bool wasPressed = _pressed[virtualKey];
            _pressed[virtualKey] = isPressed;
            if ((!wasPressedSinceLastCheck && (!isPressed || wasPressed))
                || virtualKey == (int)VirtualKey.Escape)
            {
                continue;
            }

            int? converted = InputLabelService.ToDxLibKeyCode((VirtualKey)virtualKey, 0, isExtendedKey: false);
            if (converted is int result)
            {
                keyCode = result;
                return true;
            }
        }

        keyCode = 0;
        return false;
    }

    private static bool IsPressed(int virtualKey) => IsPressed(GetAsyncKeyState(virtualKey));

    private static bool IsPressed(short state) => (state & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
