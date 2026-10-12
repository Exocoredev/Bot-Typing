using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AutoInputTester.Hotkeys;

public enum HotkeyAction
{
    SelectArea = 1,
    Start = 2,
    Pause = 3,
    Stop = 4
}

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _actions = new();
    private readonly Dictionary<int, (uint Mods, uint Key)> _bindings = new();

    public event Action<HotkeyAction>? Pressed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("AutoInputTesterHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void RegisterDefaults(string select, string start, string pause, string stop)
    {
        // Parse first so invalid input never clears working hotkeys.
        var planned = new[]
        {
            CreatePlan(HotkeyAction.SelectArea, select),
            CreatePlan(HotkeyAction.Start, start),
            CreatePlan(HotkeyAction.Pause, pause),
            CreatePlan(HotkeyAction.Stop, stop)
        };
        EnsureUnique(planned);

        var previousActions = new Dictionary<int, HotkeyAction>(_actions);
        var previousBindings = new Dictionary<int, (uint Mods, uint Key)>(_bindings);

        UnregisterAll();
        try
        {
            foreach (var item in planned)
                RegisterParsed(item.Id, item.Action, item.Mods, item.Key, item.Text);
        }
        catch
        {
            UnregisterAll();
            Restore(previousActions, previousBindings);
            throw;
        }
    }

    public static void ValidateHotkeys(string select, string start, string pause, string stop)
    {
        var planned = new[]
        {
            CreatePlan(HotkeyAction.SelectArea, select),
            CreatePlan(HotkeyAction.Start, start),
            CreatePlan(HotkeyAction.Pause, pause),
            CreatePlan(HotkeyAction.Stop, stop)
        };
        EnsureUnique(planned);
    }

    private static (int Id, HotkeyAction Action, uint Mods, uint Key, string Text) CreatePlan(HotkeyAction action, string text)
    {
        var parsed = Parse(text);
        return ((int)action, action, parsed.Mods, parsed.Key, text);
    }

    private static void EnsureUnique((int Id, HotkeyAction Action, uint Mods, uint Key, string Text)[] planned)
    {
        for (var i = 0; i < planned.Length; i++)
        {
            for (var j = i + 1; j < planned.Length; j++)
            {
                if (planned[i].Mods == planned[j].Mods && planned[i].Key == planned[j].Key)
                    throw new InvalidOperationException($"Hotkey '{planned[i].Text}' ist doppelt vergeben.");
            }
        }
    }

    private void RegisterParsed(int id, HotkeyAction action, uint mods, uint key, string text)
    {
        if (!RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, key))
            throw new InvalidOperationException($"Hotkey '{text}' konnte nicht registriert werden. Er wird vermutlich bereits von einem anderen Programm verwendet.");
        _actions[id] = action;
        _bindings[id] = (mods, key);
    }

    private void Restore(Dictionary<int, HotkeyAction> actions, Dictionary<int, (uint Mods, uint Key)> bindings)
    {
        foreach (var pair in bindings)
        {
            if (!actions.TryGetValue(pair.Key, out var action))
                continue;
            if (RegisterHotKey(_source.Handle, pair.Key, pair.Value.Mods | MOD_NOREPEAT, pair.Value.Key))
            {
                _actions[pair.Key] = action;
                _bindings[pair.Key] = pair.Value;
            }
        }
    }

    private static (uint Mods, uint Key) Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Hotkey darf nicht leer sein.");

        uint mods = 0;
        string? keyPart = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = raw.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
            else keyPart = p;
        }

        if (keyPart is null) throw new FormatException($"Kein Tastenanteil in '{text}'.");
        if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase)) keyPart = "Escape";

        var converter = new KeyConverter();
        var converted = converter.ConvertFromString(keyPart);
        if (converted is not Key key || key == Key.None)
            throw new FormatException($"Unbekannte Taste '{keyPart}'.");

        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_actions.TryGetValue(id, out var action))
            {
                Pressed?.Invoke(action);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToArray())
            UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _bindings.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
