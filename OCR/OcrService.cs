using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace AutoInputTester.OCR;

public sealed class OcrService
{
    public async Task<string> RecognizeAsync(Bitmap bitmap, string languageTag)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"AutoInputTester_{Guid.NewGuid():N}.png");
        try
        {
            bitmap.Save(temp, ImageFormat.Png);
            var file = await StorageFile.GetFileFromPathAsync(temp);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            OcrEngine? engine = null;
            try
            {
                var wanted = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(l => l.LanguageTag.Equals(languageTag, StringComparison.OrdinalIgnoreCase))
                    ?? OcrEngine.AvailableRecognizerLanguages
                        .FirstOrDefault(l => l.LanguageTag.StartsWith(languageTag.Split('-')[0], StringComparison.OrdinalIgnoreCase));

                if (wanted is not null)
                    engine = OcrEngine.TryCreateFromLanguage(wanted);
            }
            catch
            {
                // Fall back to profile languages below.
            }

            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
                throw new InvalidOperationException("Windows OCR konnte nicht gestartet werden. Installiere in Windows das gewünschte Sprach-/OCR-Paket.");

            var result = await engine.RecognizeAsync(softwareBitmap);
            return OcrTextCleanup.FromResult(result);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
