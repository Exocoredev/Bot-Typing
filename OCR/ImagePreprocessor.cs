using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AutoInputTester.Models;

namespace AutoInputTester.OCR;

public static class ImagePreprocessor
{
    private const int MaxOcrWidth = 1400;

    public static Bitmap Prepare(Bitmap source, OcrReadMode mode)
    {
        var prepared = mode switch
        {
            OcrReadMode.RightOfCaret => CropRightOfCaret(source),
            OcrReadMode.BlackTextOnly => RemoveColoredText(source),
            _ => new Bitmap(source)
        };

        using (prepared)
            return EnhanceForOcr(prepared);
    }

    private static Bitmap EnhanceForOcr(Bitmap source)
    {
        // Cap size: previous 3x upscale on wide selections made OCR so slow that
        // typing appeared frozen while the tray still said "Tippt".
        // Short lines (OCR-A umlaut dots) need 2x even on wide selections.
        var scale = source.Width <= 500 || source.Height <= 48 ? 2 : 1;
        var width = Math.Max(1, source.Width * scale);
        var height = Math.Max(1, source.Height * scale);
        if (width > MaxOcrWidth)
        {
            var factor = MaxOcrWidth / (double)width;
            width = MaxOcrWidth;
            height = Math.Max(1, (int)Math.Round(height * factor));
        }

        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = scale > 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.White);
            g.DrawImage(source, 0, 0, width, height);
        }

        // Soft contrast only — hard binarize destroyed OCR-A diagonals (j/z) and
        // caused ~14% trainer errors. Windows OCR handles grayscale well.
        SoftContrastInPlace(scaled);
        return scaled;
    }

    private static unsafe void SoftContrastInPlace(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = data.Stride;
            var height = data.Height;
            var width = data.Width;
            var ptr = (byte*)data.Scan0;

            for (var y = 0; y < height; y++)
            {
                var row = ptr + y * stride;
                for (var x = 0; x < width; x++)
                {
                    var i = x * 4;
                    var b = row[i];
                    var gch = row[i + 1];
                    var r = row[i + 2];
                    // Push near-white to white, keep mid/dark strokes (j descender, z diagonal).
                    var brightness = (r + gch + b) / 3;
                    if (brightness >= 230)
                    {
                        row[i] = 255;
                        row[i + 1] = 255;
                        row[i + 2] = 255;
                    }
                    else if (brightness <= 40)
                    {
                        row[i] = 0;
                        row[i + 1] = 0;
                        row[i + 2] = 0;
                    }
                    else
                    {
                        // Mild stretch toward black for ink, preserve anti-alias.
                        var t = (brightness - 40) / 190.0;
                        var v = (byte)Math.Clamp((int)Math.Round(255 * Math.Pow(t, 0.85)), 0, 255);
                        row[i] = v;
                        row[i + 1] = v;
                        row[i + 2] = v;
                    }

                    row[i + 3] = 255;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap RemoveColoredText(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.White);

        var srcData = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(
            new Rectangle(0, 0, result.Width, result.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                var srcPtr = (byte*)srcData.Scan0;
                var dstPtr = (byte*)dstData.Scan0;
                for (var y = 0; y < source.Height; y++)
                {
                    var srcRow = srcPtr + y * srcData.Stride;
                    var dstRow = dstPtr + y * dstData.Stride;
                    for (var x = 0; x < source.Width; x++)
                    {
                        var i = x * 4;
                        var b = srcRow[i];
                        var gch = srcRow[i + 1];
                        var r = srcRow[i + 2];
                        var a = srcRow[i + 3];
                        var max = Math.Max(r, Math.Max(gch, b));
                        var min = Math.Min(r, Math.Min(gch, b));
                        var chroma = max - min;
                        var brightness = (r + gch + b) / 3;

                        if (brightness < 225 && chroma < 55)
                        {
                            dstRow[i] = b;
                            dstRow[i + 1] = gch;
                            dstRow[i + 2] = r;
                            dstRow[i + 3] = a;
                        }
                        else
                        {
                            dstRow[i] = 255;
                            dstRow[i + 1] = 255;
                            dstRow[i + 2] = 255;
                            dstRow[i + 3] = 255;
                        }
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(srcData);
            result.UnlockBits(dstData);
        }

        return result;
    }

    private static Bitmap CropRightOfCaret(Bitmap source)
    {
        var caretX = FindCaretX(source);
        if (caretX < 0 || caretX >= source.Width - 3)
            return new Bitmap(source);

        var x = Math.Min(source.Width - 1, caretX + 2);
        var width = source.Width - x;
        if (width < 2) return new Bitmap(source);

        return source.Clone(new Rectangle(x, 0, width, source.Height), source.PixelFormat);
    }

    private static int FindCaretX(Bitmap bmp)
    {
        var bestX = -1;
        var bestRun = 0;
        var required = Math.Max(10, (int)(bmp.Height * 0.45));

        for (var x = 0; x < bmp.Width; x++)
        {
            var longest = 0;
            var current = 0;
            for (var y = 0; y < bmp.Height; y++)
            {
                var c = bmp.GetPixel(x, y);
                var neutral = Math.Abs(c.R - c.G) < 18 && Math.Abs(c.G - c.B) < 18;
                var brightness = (c.R + c.G + c.B) / 3;
                var candidate = neutral && brightness >= 45 && brightness <= 210;

                if (candidate)
                {
                    current++;
                    if (current > longest) longest = current;
                }
                else
                {
                    current = 0;
                }
            }

            if (longest > bestRun)
            {
                bestRun = longest;
                bestX = x;
            }
        }

        return bestRun >= required ? bestX : -1;
    }
}
