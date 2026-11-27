using System.Buffers.Binary;

namespace PixFix.Infrastructure.Tests;

/// <summary>Minimales WMF wie von RichEdit: Kopf, ein StretchDIBits-Datensatz mit 1×1-Pixel-Bitmap, Ende.</summary>
internal static class WmfSample
{
    public static byte[] Create()
    {
        // DIB: BITMAPINFOHEADER (40) + 1 Pixel 24 bit (3 Byte + 1 Byte Zeilenauffüllung)
        var dib = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), 4);
        dib[40] = 0x00; // Blau
        dib[41] = 0x00; // Grün
        dib[42] = 0xFF; // Rot

        // META_STRETCHDIB: 6 Byte Kopf + 22 Byte Parameter + DIB
        var stretchDib = new byte[6 + 22 + dib.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(stretchDib, (uint)(stretchDib.Length / 2));
        BinaryPrimitives.WriteUInt16LittleEndian(stretchDib.AsSpan(4), 0x0F43);
        dib.CopyTo(stretchDib.AsSpan(28));

        var eof = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(eof, 3);

        var header = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 0x0300);

        return [.. header, .. stretchDib, .. eof];
    }
}
