using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoInputTester.Hotkeys;

/// <summary>
/// System keyboard lock via WH_KEYBOARD_LL.
/// Toggle: Left Shift + Right Shift.
/// Unlock: a s d f j k l ö + - held together (10 keys).
/// Injected keys (SendInput) always pass through so auto-typing still works.
/// </summary>
public sealed class KeyboardLockService : IDisposable
{
    public bool IsLocked { get; private set; }

    public event Action<bool>? LockChanged;

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _leftShift;
    private bool _rightShift;
    private bool _toggleArmed = true;
    private readonly HashSet<int> _held = new();

    // Each group is one logical unlock key; any VK in the group counts.
    private static readonly int[][] UnlockGroups =
    {
        new[] { 0x41 },             // A
        new[] { 0x53 },             // S
        new[] { 0x44 },             // D
        new[] { 0x46 },             // F
        new[] { 0x4A },             // J
        new[] { 0x4B },             // K
        new[] { 0x4C },             // L
        new[] { 0xBA, 0xC0 },       // Ö (OEM_1 / OEM_3 depending on layout)
        new[] { 0xBB, 0x6B },       // +  (OEM_PLUS / numpad add)
        new[] { 0xBD, 0x6D }        // -  (OEM_MINUS / numpad subtract)
    };

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_INJECTED = 0x10;

    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_SHIFT = 0x10;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Tastatur-Hook konnte nicht installiert werden.");
    }

    public void SetLocked(bool locked)
    {
        if (IsLocked == locked) return;
        IsLocked = locked;
        _held.Clear();
        _toggleArmed = true;
        LockChanged?.Invoke(IsLocked);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            var injected = (info.flags & LLKHF_INJECTED) != 0;

            if (injected)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            var vk = (int)info.vkCode;

            if (isDown)
            {
                _held.Add(vk);
                UpdateShifts(vk, down: true);

                if (_leftShift && _rightShift && _toggleArmed)
                {
                    _toggleArmed = false;
                    SetLocked(!IsLocked);
                    return (IntPtr)1;
                }

                if (IsLocked)
                {
                    if (UnlockChordComplete())
                    {
                        SetLocked(false);
                        return (IntPtr)1;
                    }

                    // Must not swallow app hotkeys (Stop/Pause/Start) — RegisterHotKey
                    // never fires if the LL hook eats Ctrl/Alt/F-keys/Escape.
                    if (IsHotkeyPassthrough(vk))
                        return CallNextHookEx(_hook, nCode, wParam, lParam);

                    return (IntPtr)1;
                }
            }
            else if (isUp)
            {
                _held.Remove(vk);
                UpdateShifts(vk, down: false);
                if (!_leftShift || !_rightShift)
                    _toggleArmed = true;

                if (IsLocked)
                {
                    if (IsHotkeyPassthrough(vk))
                        return CallNextHookEx(_hook, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Allow modifier / function / Escape chords through while locked so global
    /// hotkeys (Ctrl+Shift+F10, Ctrl+Alt+Escape, …) still reach the app.
    /// </summary>
    private static bool IsHotkeyPassthrough(int vk)
    {
        // Modifiers
        if (vk is VK_LSHIFT or VK_RSHIFT or VK_SHIFT
            or 0xA2 or 0xA3 or 0x11  // LCONTROL / RCONTROL / CONTROL
            or 0xA4 or 0xA5 or 0x12) // LMENU / RMENU / MENU
            return true;

        // Escape + F1–F12 (common stop/pause bindings)
        if (vk == 0x1B || vk is >= 0x70 and <= 0x7B)
            return true;

        // If Ctrl or Alt is held, pass the key so chords like Ctrl+Shift+S work.
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0)
            return true;

        return false;
    }

    private void UpdateShifts(int vk, bool down)
    {
        if (vk == VK_LSHIFT)
            _leftShift = down;
        else if (vk == VK_RSHIFT)
            _rightShift = down;
        else if (vk == VK_SHIFT && !down)
        {
            _leftShift = false;
            _rightShift = false;
        }
    }

    private bool UnlockChordComplete()
    {
        foreach (var group in UnlockGroups)
        {
            var ok = false;
            foreach (var key in group)
            {
                if (_held.Contains(key) || (GetAsyncKeyState(key) & 0x8000) != 0)
                {
                    ok = true;
                    break;
                }
            }

            if (!ok)
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _proc = null;
        IsLocked = false;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
