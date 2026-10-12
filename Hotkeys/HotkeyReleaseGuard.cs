using System.Runtime.InteropServices;
using System.Windows.Input;

namespace AutoInputTester.Hotkeys;

public sealed class HotkeyReleaseGuard
{
    public async Task WaitForHotkeyReleaseAsync(string hotkey, CancellationToken token)
    {
        var keys = ParseVirtualKeys(hotkey);
        AddCommonModifiers(keys);

        while (keys.Any(IsDown))
            await Task.Delay(12, token);

        // A tiny settling time avoids the first injected key being combined with
        // the just-released shortcut on slower systems.
        await Task.Delay(35, token);
    }

    public async Task WaitForUserModifiersReleasedAsync(CancellationToken token)
    {
        var keys = new HashSet<int>();
        AddCommonModifiers(keys);
        while (keys.Any(IsDown))
            await Task.Delay(12, token);
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
