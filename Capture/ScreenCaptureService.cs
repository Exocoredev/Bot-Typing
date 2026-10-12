using System.Drawing;
using System.Drawing.Imaging;

namespace AutoInputTester.Capture;

public sealed class ScreenCaptureService
{
    private readonly object _gate = new();

    public Bitmap Capture(Rectangle screenRectangle)
    {
        // GDI+ is not safe for unconstrained parallel use from thread-pool OCR.
        lock (_gate)
        {
            var bitmap = new Bitmap(screenRectangle.Width, screenRectangle.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screenRectangle.Location, Point.Empty, screenRectangle.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
    }
}
