using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PixFix.Core.Documents;

namespace PixFix.Infrastructure.Documents;

/// <summary>
/// Wandelt Bilder aus PixFix-69-Dokumenten in PNG/JPEG um, die WPF anzeigen kann.
/// Reihenfolge: 1. Originaldatei aus der Ablage (über den PixFix-Marker), 2. Bitmap aus dem Metafile,
/// 3. Metafile mit GDI+ rendern. Gelingt nichts, bleibt das Originalbild im Dokument.
/// </summary>
public static class LegacyPictureConverter
{
    public static RtfPictureReplacement? Convert(RtfPicture picture, Func<string, string?> resolveLegacyPath)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(resolveLegacyPath);

        if (picture.LegacyPath is not null && resolveLegacyPath(picture.LegacyPath) is { } file && File.Exists(file))
        {
            return FromFile(file);
        }

        return picture.Format switch
        {
            "pngblip" or "jpegblip" => null,
            "wmetafile" => FromWmf(picture),
            "emfblip" => TryEncodePng(() => new Metafile(new MemoryStream(picture.Data)), picture),
            "dibitmap" => TryEncodePng(() => new Bitmap(new MemoryStream(WmfBitmapExtractor.ToBmpFile(picture.Data))), picture),
            _ => null,
        };
    }

    private static RtfPictureReplacement? FromFile(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var data = File.ReadAllBytes(file);
        return extension switch
        {
            ".png" => new RtfPictureReplacement("pngblip", data),
            ".jpg" or ".jpeg" => new RtfPictureReplacement("jpegblip", data),
            _ => TryEncodePng(() => Image.FromStream(new MemoryStream(data)), null),
        };
    }

    private static RtfPictureReplacement? FromWmf(RtfPicture picture)
    {
        var bmp = WmfBitmapExtractor.TryExtractBmp(picture.Data);
        if (bmp is not null)
        {
            var result = TryEncodePng(() => new Bitmap(new MemoryStream(bmp)), null);
            if (result is not null)
            {
                return result;
            }
        }

        return TryEncodePng(() => LoadWmf(picture.Data), picture);
    }

    /// <summary>
    /// Lädt ein Bild und speichert es als PNG. Metafiles werden dabei in der Anzeigegröße aus dem RTF gerendert.
    /// </summary>
    private static RtfPictureReplacement? TryEncodePng(Func<Image> load, RtfPicture? sizeSource)
    {
        try
        {
            using var image = load();
            using var output = new MemoryStream();
            if (image is Metafile)
            {
                var (width, height) = TargetSize(image, sizeSource);
                using var bitmap = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(image, 0, 0, width, height);
                }

                bitmap.Save(output, ImageFormat.Png);
            }
            else
            {
                image.Save(output, ImageFormat.Png);
            }

            return new RtfPictureReplacement("pngblip", output.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException or InvalidOperationException)
        {
            // GDI+ meldet unlesbare Bilder als ArgumentException/ExternalException; dann bleibt das Original im Dokument.
            return null;
        }
    }

    private static (int Width, int Height) TargetSize(Image image, RtfPicture? picture)
    {
        // Twips → Pixel bei 96 dpi (1440 Twips pro Zoll), doppelte Auflösung für scharfe Darstellung.
        const double Scale = 96.0 / 1440.0 * 2;
        if (picture is { GoalWidthTwips: > 0 and var width, GoalHeightTwips: > 0 and var height })
        {
            return ((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale));
        }

        return (Math.Clamp(image.Width, 1, 4000), Math.Clamp(image.Height, 1, 4000));
    }

    private static Metafile LoadWmf(byte[] data)
    {
        var handle = SetWinMetaFileBits((uint)data.Length, data, IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Metafile konnte nicht gelesen werden.", nameof(data));
        }

        return new Metafile(handle, deleteEmf: true);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr SetWinMetaFileBits(uint nSize, byte[] lpMeta16Data, IntPtr hdcRef, IntPtr lpMFP);
}
