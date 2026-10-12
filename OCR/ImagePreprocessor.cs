using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AutoInputTester.Models;

namespace AutoInputTester.OCR;

public static class ImagePreprocessor
{
    public static Bitmap Prepare(Bitmap source, OcrReadMode mode)
    {
        var prepared = mode switch
        {
            OcrReadMode.RightOfCaret => CropRightOfCaret(source),
            OcrReadMode.BlackTextOnly => RemoveColoredText(source),
            _ => new Bitmap(source)
        };

        // Upscale + hard binarize markedly improves thin glyphs (f/j/1/l) and spaces
        // for Windows OCR on typewriter-style fonts.
        using (prepared)
            return EnhanceForOcr(prepared);
    }

    private static Bitmap EnhanceForOcr(Bitmap source)
    {
        var scale = source.Width < 900 ? 3 : 2;
        var width = Math.Max(1, source.Width * scale);
        var height = Math.Max(1, source.Height * scale);
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.White);
            g.DrawImage(source, 0, 0, width, height);
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = scaled.GetPixel(x, y);
                var brightness = (c.R + c.G + c.B) / 3;
                result.SetPixel(x, y, brightness < 170 ? Color.Black : Color.White);
            }
        }

        scaled.Dispose();
        return result;
    }

    private static Bitmap RemoveColoredText(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.White);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var chroma = max - min;
                var brightness = (c.R + c.G + c.B) / 3;

                // Neutral/dark pixels are retained; strongly colored pixels (e.g. green completed text) disappear.
                if (brightness < 225 && chroma < 55)
                    result.SetPixel(x, y, Color.FromArgb(c.A, c.R, c.G, c.B));
            }
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
