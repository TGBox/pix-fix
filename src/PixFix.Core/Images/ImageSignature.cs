namespace PixFix.Core.Images;

/// <summary>
/// Bildsignatur wie in PixFix 69 (<c>ImageSignature</c>): Graustufen-Raster 16×16, Histogramm mit 16 Stufen,
/// Average-Hash und Difference-Hash (je 64 Bit) und Seitenverhältnis.
/// </summary>
public sealed record ImageSignature(ulong AverageHash, ulong DifferenceHash, byte[] Pixels, int[] Histogram, double Aspect)
{
    public const int PixelCount = 256;
    public const int HistogramBins = 16;
}

/// <summary>Signatur des ganzen Bildes und der Bildmitte (72 %), wie im Bildindex von PixFix 69.</summary>
public sealed record ImageSignaturePair(ImageSignature Full, ImageSignature Center);
