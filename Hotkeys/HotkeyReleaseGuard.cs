using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AutoInputTester.Hotkeys;

public sealed class HotkeyReleaseGuard
{
    public async Task WaitForHotkeyReleaseAsync(string hotkey, CancellationToken token)
    {
        var keys = ParseVirtualKeys(hotkey);
        AddCommonModifiers(keys);

        var deadline = Environment.TickCount64 + 3000;
        while (keys.Any(IsDown) && Environment.TickCount64 < deadline)
            await Task.Delay(12, token);

        await Task.Delay(35, token);
    }

    /// <summary>
    /// Waits until modifiers / manual keys are up (lock-toggle or user typing).
    /// Never leaves the run permanently paused — only a short settle, then continue.
    /// </summary>
    public async Task WaitForUserModifiersReleasedAsync(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 2500;
        var sawDown = false;

        while (Environment.TickCount64 < deadline)
        {
            if (!AnyUserKeyDown())
            {
                if (sawDown)
                    await Task.Delay(150, token);
                return;
            }

            sawDown = true;
            await Task.Delay(15, token);
        }

        // Timed out — continue typing anyway.
        await Task.Delay(50, token);
    }

    private static bool AnyUserKeyDown()
    {
        // Only modifiers — not A–Z. GetAsyncKeyState also sees our own SendInput
        // letters and would stall typing after every character.
        return IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) ||
               IsDown(VK_LWIN) || IsDown(VK_RWIN) ||
               IsDown(VK_LSHIFT) || IsDown(VK_RSHIFT) ||
               IsDown(VK_LCONTROL) || IsDown(VK_RCONTROL) ||
               IsDown(VK_LMENU) || IsDown(VK_RMENU);
    }

    private static HashSet<int> ParseVirtualKeys(string text)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(VK_CONTROL);
                continue;
            }
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(VK_SHIFT);
                continue;
            }
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(VK_MENU);
                continue;
            }
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(VK_LWIN);
                result.Add(VK_RWIN);
                continue;
            }

            var keyText = part.Equals("Esc", StringComparison.OrdinalIgnoreCase) ? "Escape" : part;
            try
            {
                var converter = new KeyConverter();
                if (converter.ConvertFromString(keyText) is Key key && key != Key.None)
                    result.Add(KeyInterop.VirtualKeyFromKey(key));
            }
            catch
            {
                // Unknown custom text: modifiers are still guarded.
            }
        }

        return result;
    }

    private static void AddCommonModifiers(HashSet<int> keys)
    {
        keys.Add(VK_SHIFT);
        keys.Add(VK_CONTROL);
        keys.Add(VK_MENU);
        keys.Add(VK_LWIN);
        keys.Add(VK_RWIN);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
