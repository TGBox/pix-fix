using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PixFix.Core.Images;

namespace PixFix.Infrastructure.Images;

/// <summary>
/// Berechnet Bildsignaturen exakt wie PixFix 69 (<c>BuildImageSignatureFromSource</c>, <c>BuildCenteredImageSignature</c>,
/// <c>ImageFileCopyUniversal</c>): GDI+ mit HighQualityBicubic auf 16×16 bzw. 9×8 Pixel. Gleiche Bibliothek, gleiche Schritte –
/// damit sind die Signaturen bitgleich (geprüft im Vergleichstest gegen den Bildindex von PixFix 69).
/// </summary>
public static class ImageSignatureBuilder
{
    /// <summary>Versionsnummer des Verfahrens; bei Änderungen erhöhen, dann werden gespeicherte Signaturen neu berechnet.</summary>
    public const int Algorithm = 1;

    /// <summary>Anteil der Bildmitte für die zweite Signatur.</summary>
    private const double CenterShare = .72;

    public static ImageSignaturePair Build(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        using var loaded = Image.FromFile(file);
        using var source = IndependentCopy(loaded);

        var full = FromSource(source, new Rectangle(0, 0, source.Width, source.Height));

        var w = Math.Max(1, (int)Math.Round(source.Width * CenterShare));
        var h = Math.Max(1, (int)Math.Round(source.Height * CenterShare));
        var x = Math.Max(0, (source.Width - w) / 2);
        var y = Math.Max(0, (source.Height - h) / 2);
        var center = FromSource(source, new Rectangle(x, y, w, h));

        return new ImageSignaturePair(full, center);
    }

    /// <summary>Wie <c>IndependentImageCopy</c> in PixFix 69: 32-Bit-ARGB, transparenter Hintergrund.</summary>
    private static Bitmap IndependentCopy(Image source)
    {
        if (source.Width < 1 || source.Height < 1)
        {
            throw new ArgumentException("Das Bild ist leer.", nameof(source));
        }

        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, new Rectangle(0, 0, copy.Width, copy.Height));
        return copy;
    }

    private static ImageSignature FromSource(Image source, Rectangle sourceRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            sourceRect = new Rectangle(0, 0, source.Width, source.Height);
        }

        var aspect = sourceRect.Height == 0 ? 1.0 : (double)sourceRect.Width / sourceRect.Height;
        var pixels = new byte[ImageSignature.PixelCount];
        var histogram = new int[ImageSignature.HistogramBins];
        ulong averageHash = 0;
        ulong differenceHash = 0;

        using (var raster = new Bitmap(16, 16))
        {
            using (var g = Graphics.FromImage(raster))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, new Rectangle(0, 0, 16, 16), sourceRect, GraphicsUnit.Pixel);
            }

            var total = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var c = raster.GetPixel(x, y);
                    var value = ((c.R * 299) + (c.G * 587) + (c.B * 114)) / 1000;
                    pixels[(y * 16) + x] = (byte)value;
                    histogram[Math.Min(15, value / 16)]++;
                    total += value;
                }
            }

            var average = total / 256;
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    if (pixels[(y * 2 * 16) + (x * 2)] >= average)
                    {
                        averageHash |= 1UL << ((y * 8) + x);
                    }
                }
            }
        }

        using (var edges = new Bitmap(9, 8))
        {
            using (var g = Graphics.FromImage(edges))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, new Rectangle(0, 0, 9, 8), sourceRect, GraphicsUnit.Pixel);
            }

            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var a = edges.GetPixel(x, y);
                    var b = edges.GetPixel(x + 1, y);
                    var left = (a.R * 299) + (a.G * 587) + (a.B * 114);
                    var right = (b.R * 299) + (b.G * 587) + (b.B * 114);
                    if (left >= right)
                    {
                        differenceHash |= 1UL << ((y * 8) + x);
                    }
                }
            }
        }

        return new ImageSignature(averageHash, differenceHash, pixels, histogram, aspect);
    }
}
