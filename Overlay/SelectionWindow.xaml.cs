using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WinForms = System.Windows.Forms;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace AutoInputTester.Overlay;

public partial class SelectionWindow : Window
{
    private enum DragMode { None, Drawing, Move, NW, N, NE, E, SE, S, SW, W }

    private readonly WinForms.Screen _screen;
    private WpfPoint _start;
    private Rect _selection;
    private Rect _dragOrigin;
    private DragMode _mode;
    private double _scaleX = 1;
    private double _scaleY = 1;

    public DrawingRectangle? SelectedScreenRectangle { get; private set; }

    public SelectionWindow()
    {
        InitializeComponent();
        _screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);

        foreach (var handle in Handles())
            handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;

        SourceInitialized += SelectionWindow_SourceInitialized;
        Loaded += SelectionWindow_Loaded;
    }

    private void SelectionWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _screen.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, SWP_SHOWWINDOW);
    }

    private void SelectionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            _scaleX = source.CompositionTarget.TransformToDevice.M11;
            _scaleY = source.CompositionTarget.TransformToDevice.M22;
        }
        UpdateVisuals();
        Activate();
        Focus();
    }

    private IEnumerable<WpfRectangle> Handles()
    {
        yield return HandleNW; yield return HandleN; yield return HandleNE; yield return HandleE;
        yield return HandleSE; yield return HandleS; yield return HandleSW; yield return HandleW;
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfRectangle handle || handle.Tag is null) return;
        _mode = Enum.Parse<DragMode>(handle.Tag.ToString()!);
        _start = e.GetPosition(Root);
        _dragOrigin = _selection;
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        _dragOrigin = _selection;

        if (_selection.Width > 0 && _selection.Height > 0 && _selection.Contains(_start))
        {
            _mode = DragMode.Move;
            Cursor = System.Windows.Input.Cursors.SizeAll;
        }
        else
        {
            _mode = DragMode.Drawing;
            _selection = new Rect(_start, _start);
            Cursor = System.Windows.Input.Cursors.Cross;
        }
        Mouse.Capture(this);
        UpdateVisuals();
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_mode == DragMode.None || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Root);
        var dx = p.X - _start.X;
        var dy = p.Y - _start.Y;

        if (_mode == DragMode.Drawing)
        {
            _selection = Normalize(new Rect(_start, p));
        }
        else if (_mode == DragMode.Move)
        {
            _selection = Clamp(new Rect(_dragOrigin.X + dx, _dragOrigin.Y + dy, _dragOrigin.Width, _dragOrigin.Height));
        }
        else
        {
            var r = _dragOrigin;
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (_mode is DragMode.NW or DragMode.W or DragMode.SW) left += dx;
            if (_mode is DragMode.NE or DragMode.E or DragMode.SE) right += dx;
            if (_mode is DragMode.NW or DragMode.N or DragMode.NE) top += dy;
            if (_mode is DragMode.SW or DragMode.S or DragMode.SE) bottom += dy;
            _selection = Clamp(Normalize(new Rect(new WpfPoint(left, top), new WpfPoint(right, bottom))));
        }

        UpdateVisuals();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;
        Mouse.Capture(null);
        _mode = DragMode.None;
        Cursor = System.Windows.Input.Cursors.Cross;
        if (_selection.Width < 5 || _selection.Height < 5)
            _selection = Rect.Empty;
        UpdateVisuals();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        if (e.Key == Key.Enter && _selection.Width >= 5 && _selection.Height >= 5)
        {
            var b = _screen.Bounds;
            SelectedScreenRectangle = new DrawingRectangle(
                b.Left + (int)Math.Round(_selection.Left * _scaleX),
                b.Top + (int)Math.Round(_selection.Top * _scaleY),
                Math.Max(1, (int)Math.Round(_selection.Width * _scaleX)),
                Math.Max(1, (int)Math.Round(_selection.Height * _scaleY)));
            DialogResult = true;
            Close();
        }
    }

    private Rect Clamp(Rect r)
    {
        var maxW = Math.Max(1, ActualWidth);
        var maxH = Math.Max(1, ActualHeight);
        var w = Math.Min(r.Width, maxW);
        var h = Math.Min(r.Height, maxH);
        var x = Math.Max(0, Math.Min(r.X, maxW - w));
        var y = Math.Max(0, Math.Min(r.Y, maxH - h));
        return new Rect(x, y, w, h);
    }

    private static Rect Normalize(Rect r)
    {
        var x = Math.Min(r.Left, r.Right);
        var y = Math.Min(r.Top, r.Bottom);
        return new Rect(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
    }

    private void UpdateVisuals()
    {
        var w = Math.Max(0, ActualWidth);
        var h = Math.Max(0, ActualHeight);
        var has = !_selection.IsEmpty && _selection.Width >= 1 && _selection.Height >= 1;

        if (!has)
        {
            SetRect(ShadeTop, 0, 0, w, h);
            SetRect(ShadeLeft, 0, 0, 0, 0);
            SetRect(ShadeRight, 0, 0, 0, 0);
            SetRect(ShadeBottom, 0, 0, 0, 0);
            SelectionBorder.Visibility = Visibility.Collapsed;
            foreach (var handle in Handles()) handle.Visibility = Visibility.Collapsed;
            return;
        }

        var r = Clamp(_selection);
        SetRect(ShadeTop, 0, 0, w, r.Top);
        SetRect(ShadeLeft, 0, r.Top, r.Left, r.Height);
        SetRect(ShadeRight, r.Right, r.Top, Math.Max(0, w - r.Right), r.Height);
        SetRect(ShadeBottom, 0, r.Bottom, w, Math.Max(0, h - r.Bottom));

        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, r.Left);
        Canvas.SetTop(SelectionBorder, r.Top);
        SelectionBorder.Width = r.Width;
        SelectionBorder.Height = r.Height;

        SetHandle(HandleNW, r.Left, r.Top);
        SetHandle(HandleN, r.Left + r.Width / 2, r.Top);
        SetHandle(HandleNE, r.Right, r.Top);
        SetHandle(HandleE, r.Right, r.Top + r.Height / 2);
        SetHandle(HandleSE, r.Right, r.Bottom);
        SetHandle(HandleS, r.Left + r.Width / 2, r.Bottom);
        SetHandle(HandleSW, r.Left, r.Bottom);
        SetHandle(HandleW, r.Left, r.Top + r.Height / 2);
    }

    private static void SetRect(FrameworkElement element, double x, double y, double w, double h)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, w);
        element.Height = Math.Max(0, h);
    }

    private static void SetHandle(WpfRectangle handle, double centerX, double centerY)
    {
        handle.Visibility = Visibility.Visible;
        Canvas.SetLeft(handle, centerX - handle.Width / 2);
        Canvas.SetTop(handle, centerY - handle.Height / 2);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
