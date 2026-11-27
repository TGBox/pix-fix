using System.Buffers.Binary;

namespace PixFix.Infrastructure.Documents;

/// <summary>
/// Holt das eingebettete Bitmap aus einem Windows-Metafile (WMF). RichEdit speichert eingefügte Bilder als WMF,
/// das intern nur ein DIB per <c>StretchDIBits</c> zeichnet. Das Bitmap wird als vollständige BMP-Datei
/// zurückgegeben – so bleibt die Originalauflösung erhalten.
/// </summary>
public static class WmfBitmapExtractor
{
    private const uint PlaceableKey = 0x9AC6CDD7;
    private const ushort MetaEof = 0x0000;
    private const ushort MetaStretchDib = 0x0F43;
    private const ushort MetaDibStretchBlt = 0x0B41;
    private const ushort MetaDibBitBlt = 0x0940;

    /// <summary>BMP-Datei des größten eingebetteten Bitmaps oder <c>null</c>, wenn keins gefunden wurde.</summary>
    public static byte[]? TryExtractBmp(ReadOnlySpan<byte> wmf)
    {
        var offset = 0;
        if (wmf.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(wmf) == PlaceableKey)
        {
            offset = 22;
        }

        if (wmf.Length < offset + 18)
        {
            return null;
        }

        var headerWords = BinaryPrimitives.ReadUInt16LittleEndian(wmf[(offset + 2)..]);
        var position = offset + (headerWords * 2);
        ReadOnlySpan<byte> best = default;

        while (position + 6 <= wmf.Length)
        {
            var recordBytes = (long)BinaryPrimitives.ReadUInt32LittleEndian(wmf[position..]) * 2;
            var function = BinaryPrimitives.ReadUInt16LittleEndian(wmf[(position + 4)..]);
            if (function == MetaEof || recordBytes < 6 || position + recordBytes > wmf.Length)
            {
                break;
            }

            var entry = wmf.Slice(position, (int)recordBytes);
            var dibOffset = function switch
            {
                MetaStretchDib => 6 + 22,
                MetaDibStretchBlt => 6 + 20,
                MetaDibBitBlt => 6 + 16,
                _ => -1,
            };

            if (dibOffset > 0 && entry.Length > dibOffset + 40)
            {
                var dib = entry[dibOffset..];
                if (BinaryPrimitives.ReadUInt32LittleEndian(dib) is 40 or 108 or 124 && dib.Length > best.Length)
                {
                    best = dib;
                }
            }

            position += (int)recordBytes;
        }

        return best.IsEmpty ? null : ToBmpFile(best);
    }

    /// <summary>Setzt den 14-Byte-Dateikopf vor ein DIB (BITMAPINFOHEADER + Farbtabelle + Pixel).</summary>
    public static byte[] ToBmpFile(ReadOnlySpan<byte> dib)
    {
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);

        var paletteEntries = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1 << bitCount : 0;
        const uint BiBitfields = 3;
        var masks = headerSize == 40 && compression == BiBitfields ? 12 : 0;
        var pixelOffset = 14 + headerSize + masks + (paletteEntries * 4);

        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), (uint)pixelOffset);
        dib.CopyTo(file.AsSpan(14));
        return file;
    }
}
