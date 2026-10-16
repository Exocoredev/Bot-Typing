using System.Runtime.InteropServices;

namespace AutoInputTester.Native;

public sealed class KeyboardSender
{
    public IntPtr GetKeyboardLayoutForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return GetKeyboardLayout(0);

        var threadId = GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(threadId);
    }

    public void SendCharacter(char ch, IntPtr layout)
    {
        if (ch == '\r') return;
        if (ch == '\n') { SendPhysicalVirtualKey(VK_RETURN, layout); return; }
        if (ch == '\t') { SendPhysicalVirtualKey(VK_TAB, layout); return; }
        if (ch == ' ') { SendPhysicalVirtualKey(VK_SPACE, layout); return; }

        // Clear any sticky modifiers before injecting the next character. Stuck
        // Shift/Ctrl/Alt are a common cause of "random" wrong letters in trainers.
        ReleaseModifiers();

        // German umlauts: VkKeyScanEx for active layout, then fixed DE OEM map, else Unicode.
        if (IsGermanUmlaut(ch))
        {
            if (TrySendPhysicalCharacter(ch, layout) || TrySendGermanUmlaut(ch, layout))
                return;
            SendUnicode(ch);
            return;
        }

        if (!TrySendPhysicalCharacter(ch, layout))
            SendUnicode(ch);
    }

    public void SendBackspace(IntPtr layout)
    {
        ReleaseModifiers();
        SendPhysicalVirtualKey(VK_BACK, layout);
    }

    private static bool TrySendPhysicalCharacter(char ch, IntPtr layout)
    {
        var mapping = VkKeyScanExW(ch, layout);
        if (mapping == -1)
            return false;

        var vk = (ushort)(mapping & 0xFF);
        var modifierState = (mapping >> 8) & 0xFF;

        var shift = (modifierState & 1) != 0;
        var ctrl = (modifierState & 2) != 0;
        var alt = (modifierState & 4) != 0;

        var inputs = new List<INPUT>(12);

        if (ctrl) AddKey(inputs, VK_LCONTROL, layout, keyUp: false);
        if (alt) AddKey(inputs, ctrl ? VK_RMENU : VK_LMENU, layout, keyUp: false);
        if (shift) AddKey(inputs, VK_LSHIFT, layout, keyUp: false);

        AddKey(inputs, vk, layout, keyUp: false);
        AddKey(inputs, vk, layout, keyUp: true);

        if (shift) AddKey(inputs, VK_LSHIFT, layout, keyUp: true);
        if (alt) AddKey(inputs, ctrl ? VK_RMENU : VK_LMENU, layout, keyUp: true);
        if (ctrl) AddKey(inputs, VK_LCONTROL, layout, keyUp: true);

        EnsureSent(inputs.ToArray());
        return true;
    }

    private static void SendPhysicalVirtualKey(ushort vk, IntPtr layout)
    {
        ReleaseModifiers();
        var inputs = new List<INPUT>(2);
        AddKey(inputs, vk, layout, keyUp: false);
        AddKey(inputs, vk, layout, keyUp: true);
        EnsureSent(inputs.ToArray());
    }

    private static void ReleaseModifiers()
    {
        var inputs = new List<INPUT>(8);
        foreach (var vk in new ushort[] { VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU })
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                AddKey(inputs, vk, IntPtr.Zero, keyUp: true);
        }

        if (inputs.Count > 0)
            EnsureSent(inputs.ToArray());
    }

    private static void AddKey(List<INPUT> inputs, ushort vk, IntPtr layout, bool keyUp)
    {
        var mapped = layout == IntPtr.Zero
            ? MapVirtualKeyW(vk, MAPVK_VK_TO_VSC_EX)
            : MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC_EX, layout);

        var scan = (ushort)(mapped & 0xFF);
        var prefix = (mapped >> 8) & 0xFF;
        var flags = keyUp ? KEYEVENTF_KEYUP : 0u;
        if (prefix is 0xE0 or 0xE1 || IsExtendedVirtualKey(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;

        // Send both virtual key and scan code. SCANCODE-only injection is less
        // reliable in Chromium/Edge text fields and can mis-type thin keys.
        inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags
                }
            }
        });
    }

    private static bool IsExtendedVirtualKey(ushort vk)
    {
        return vk is VK_RMENU or VK_RCONTROL or VK_INSERT or VK_DELETE or VK_HOME or VK_END
            or VK_PRIOR or VK_NEXT or VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN;
    }

    private static bool IsGermanUmlaut(char ch) =>
        ch is 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü' or 'ß';

    /// <summary>
    /// DE-DE OEM mapping: ö=OEM_3, ü=OEM_1, ä=OEM_7, ß=OEM_4.
    /// </summary>
    private static bool TrySendGermanUmlaut(char ch, IntPtr layout)
    {
        ushort vk;
        var shift = false;
        switch (ch)
        {
            case 'ö': vk = VK_OEM_3; break;
            case 'Ö': vk = VK_OEM_3; shift = true; break;
            case 'ü': vk = VK_OEM_1; break;
            case 'Ü': vk = VK_OEM_1; shift = true; break;
            case 'ä': vk = VK_OEM_7; break;
            case 'Ä': vk = VK_OEM_7; shift = true; break;
            case 'ß': vk = VK_OEM_4; break;
            default: return false;
        }

        var inputs = new List<INPUT>(6);
        if (shift) AddKey(inputs, VK_LSHIFT, layout, keyUp: false);
        AddKey(inputs, vk, layout, keyUp: false);
        AddKey(inputs, vk, layout, keyUp: true);
        if (shift) AddKey(inputs, VK_LSHIFT, layout, keyUp: true);
        EnsureSent(inputs.ToArray());
        return true;
    }

    private static void SendUnicode(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE } }
        };
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
        };
        EnsureSent(inputs);
    }

    private static void EnsureSent(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
            throw new InvalidOperationException("Windows SendInput konnte nicht alle Tastendrücke senden.");
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_END = 0x23;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_OEM_1 = 0xBA; // ü
    private const ushort VK_OEM_3 = 0xC0; // ö
    private const ushort VK_OEM_4 = 0xDB; // ß
    private const ushort VK_OEM_7 = 0xDE; // ä

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanExW(char ch, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
}
