using System.Runtime.InteropServices;
using System.Text;

namespace AutoInputTester.Target;

public sealed class TargetWindowService
{
    public IntPtr CaptureForegroundWindow()
    {
        return Normalize(GetForegroundWindow());
    }

    public bool IsForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return Normalize(GetForegroundWindow()) == Normalize(hwnd);
    }

    public bool EnsureForeground(IntPtr hwnd)
    {
        hwnd = Normalize(hwnd);
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        if (IsForeground(hwnd)) return true;

        if (IsIconic(hwnd))
        {
            // Synchronous restore so subsequent focus calls are not racing the shell.
            ShowWindow(hwnd, SW_RESTORE);
            var restoreDeadline = Environment.TickCount64 + 250;
            while (IsIconic(hwnd) && Environment.TickCount64 < restoreDeadline)
                Thread.Sleep(10);
        }

        var foreground = Normalize(GetForegroundWindow());
        var thisThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var foregroundThread = foreground != IntPtr.Zero
            ? GetWindowThreadProcessId(foreground, out _)
            : 0u;

        var attachedTarget = false;
        var attachedForeground = false;

        try
        {
            if (targetThread != 0 && targetThread != thisThread)
                attachedTarget = AttachThreadInput(thisThread, targetThread, true);

            if (foregroundThread != 0 && foregroundThread != thisThread && foregroundThread != targetThread)
                attachedForeground = AttachThreadInput(thisThread, foregroundThread, true);

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedForeground)
                AttachThreadInput(thisThread, foregroundThread, false);
            if (attachedTarget)
                AttachThreadInput(thisThread, targetThread, false);
        }

        return IsForeground(hwnd);
    }

    public bool IsAlive(IntPtr hwnd)
    {
        hwnd = Normalize(hwnd);
        return hwnd != IntPtr.Zero && IsWindow(hwnd);
    }

    public string GetWindowTitle(IntPtr hwnd)
    {
        hwnd = Normalize(hwnd);
        if (hwnd == IntPtr.Zero) return "Unbekannt";
        var len = GetWindowTextLength(hwnd);
        var sb = new StringBuilder(Math.Max(1, len + 1));
        GetWindowText(hwnd, sb, sb.Capacity);
        return string.IsNullOrWhiteSpace(sb.ToString()) ? "Fenster ohne Titel" : sb.ToString();
    }

    private static IntPtr Normalize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        var root = GetAncestor(hwnd, GA_ROOT);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private const uint GA_ROOT = 2;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
