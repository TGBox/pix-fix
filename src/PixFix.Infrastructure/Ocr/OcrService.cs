using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PixFix.Infrastructure.Ocr;

/// <summary>
/// Texterkennung mit der in Windows eingebauten OCR (dieselbe Engine wie in PixFix 69, dort per PowerShell aufgerufen).
/// Verwendet die Sprachen des Windows-Benutzerprofils.
/// </summary>
public sealed class OcrService
{
    /// <summary>Erkannter Text oder leer, wenn keine OCR-Sprache installiert ist.</summary>
    public async Task<string> RecognizeAsync(string imageFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return string.Empty;
        }

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imageFile));
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();

        // Die Engine verarbeitet höchstens OcrEngine.MaxImageDimension Pixel je Seite; größere Bilder werden verkleinert.
        var max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        var largest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        if (largest > max)
        {
            var scale = (double)max / largest;
            transform.ScaledWidth = (uint)Math.Max(1, Math.Floor(decoder.PixelWidth * scale));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Floor(decoder.PixelHeight * scale));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await engine.RecognizeAsync(bitmap);
        return result.Text;
    }
}
