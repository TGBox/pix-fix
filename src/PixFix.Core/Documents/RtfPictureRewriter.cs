using System.Globalization;
using System.Text;

namespace PixFix.Core.Documents;

/// <summary>Ein Bild aus einem RTF-Dokument (<c>{\pict …}</c>-Gruppe).</summary>
/// <param name="Format">RTF-Bildformat ohne Parameter, z. B. <c>pngblip</c>, <c>jpegblip</c>, <c>wmetafile</c>.</param>
/// <param name="Data">Binärdaten des Bildes.</param>
/// <param name="GoalWidthTwips">Gewünschte Anzeigebreite (<c>\picwgoal</c>).</param>
/// <param name="GoalHeightTwips">Gewünschte Anzeigehöhe (<c>\pichgoal</c>).</param>
/// <param name="LegacyPath">Relativer Bildpfad aus dem PixFix-69-Marker direkt vor dem Bild, falls vorhanden.</param>
public sealed record RtfPicture(string Format, byte[] Data, int? GoalWidthTwips, int? GoalHeightTwips, string? LegacyPath);

/// <summary>Ersatzbild; <paramref name="Format"/> ist <c>pngblip</c> oder <c>jpegblip</c>.</summary>
public sealed record RtfPictureReplacement(string Format, byte[] Data);

/// <summary>
/// Schreibt die Bilder eines RTF-Dokuments um, ohne den übrigen Text anzufassen, und entfernt dabei die
/// PixFix-69-Marker <c>[[PFIMG:…]]</c>. Hintergrund: PixFix 69 speichert Bilder als Windows-Metafile
/// (<c>\wmetafile8</c>), die WPF nicht zuverlässig anzeigt; ersetzt werden sie durch PNG/JPEG.
/// </summary>
public static class RtfPictureRewriter
{
    private const string MarkerStart = "[[PFIMG:";
    private const string MarkerEnd = "]]";

    /// <summary>Ein Marker gilt nur für ein Bild, das höchstens so viele Zeichen danach beginnt.</summary>
    private const int MarkerReach = 400;

    private static readonly HashSet<string> PictureFormats = new(StringComparer.Ordinal)
    {
        "pngblip", "jpegblip", "emfblip", "wmetafile", "dibitmap", "wbitmap", "macpict", "pmmetafile",
    };

    /// <param name="rtf">Das RTF-Dokument.</param>
    /// <param name="convert">Liefert das Ersatzbild oder <c>null</c>, um das Originalbild zu behalten.</param>
    public static string Rewrite(string rtf, Func<RtfPicture, RtfPictureReplacement?> convert)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(convert);

        var output = new StringBuilder(rtf.Length);
        string? markerPath = null;
        var markerEndIndex = -1;
        var i = 0;

        while (i < rtf.Length)
        {
            var c = rtf[i];

            if (c == '[' && string.CompareOrdinal(rtf, i, MarkerStart, 0, MarkerStart.Length) == 0)
            {
                var end = rtf.IndexOf(MarkerEnd, i + MarkerStart.Length, StringComparison.Ordinal);
                if (end > 0)
                {
                    markerPath = DecodeMarker(rtf.AsSpan(i + MarkerStart.Length, end - i - MarkerStart.Length));
                    markerEndIndex = end + MarkerEnd.Length;
                    i = markerEndIndex;
                    continue;
                }
            }

            if (c == '{' && IsPictureGroupStart(rtf, i))
            {
                var end = FindGroupEnd(rtf, i);
                if (end < 0)
                {
                    output.Append(rtf, i, rtf.Length - i);
                    break;
                }

                var legacyPath = markerPath is not null && i - markerEndIndex <= MarkerReach ? markerPath : null;
                markerPath = null;

                var picture = ParsePicture(rtf, i, end, legacyPath);
                var replacement = picture is null ? null : convert(picture);
                if (replacement is null)
                {
                    output.Append(rtf, i, end - i + 1);
                }
                else
                {
                    AppendPicture(output, replacement, picture!);
                }

                i = end + 1;
                continue;
            }

            if (c == '\\' && i + 1 < rtf.Length)
            {
                // Escapes wie \{ \} \\ unverändert übernehmen, damit sie nicht als Gruppen zählen.
                output.Append(c).Append(rtf[i + 1]);
                i += 2;
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    private static bool IsPictureGroupStart(string rtf, int index)
    {
        const string start = "{\\pict";
        if (string.CompareOrdinal(rtf, index, start, 0, start.Length) != 0)
        {
            return false;
        }

        var next = index + start.Length;
        return next >= rtf.Length || !char.IsAsciiLetter(rtf[next]);
    }

    /// <summary>Index der schließenden Klammer der Gruppe, die bei <paramref name="start"/> beginnt; -1 wenn unvollständig.</summary>
    public static int FindGroupEnd(string rtf, int start)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        var depth = 0;
        for (var j = start; j < rtf.Length; j++)
        {
            switch (rtf[j])
            {
                case '\\':
                    j++;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return j;
                    }

                    break;
            }
        }

        return -1;
    }

    private static RtfPicture? ParsePicture(string rtf, int start, int end, string? legacyPath)
    {
        string? format = null;
        int? goalWidth = null;
        int? goalHeight = null;
        var hex = new StringBuilder();

        var j = start + 1;
        while (j < end)
        {
            var c = rtf[j];
            if (c == '{')
            {
                // Untergruppen wie {\*\blipuid …} gehören nicht zu den Bilddaten.
                var groupEnd = FindGroupEnd(rtf, j);
                j = groupEnd < 0 ? end : groupEnd + 1;
                continue;
            }

            if (c == '\\')
            {
                var wordStart = j + 1;
                var k = wordStart;
                while (k < end && char.IsAsciiLetter(rtf[k]))
                {
                    k++;
                }

                var word = rtf[wordStart..k];
                var paramStart = k;
                if (k < end && rtf[k] == '-')
                {
                    k++;
                }

                while (k < end && char.IsAsciiDigit(rtf[k]))
                {
                    k++;
                }

                int? parameter = k > paramStart && int.TryParse(rtf.AsSpan(paramStart, k - paramStart), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var p)
                    ? p
                    : null;

                if (k < end && rtf[k] == ' ')
                {
                    k++;
                }

                if (word.Length == 0)
                {
                    k = Math.Min(end, j + 2);
                }
                else if (PictureFormats.Contains(word))
                {
                    format = word;
                }
                else if (word == "picwgoal")
                {
                    goalWidth = parameter;
                }
                else if (word == "pichgoal")
                {
                    goalHeight = parameter;
                }

                j = k;
                continue;
            }

            if (char.IsAsciiHexDigit(c))
            {
                hex.Append(c);
            }

            j++;
        }

        if (format is null || hex.Length < 2)
        {
            return null;
        }

        if (hex.Length % 2 == 1)
        {
            hex.Length--;
        }

        return new RtfPicture(format, Convert.FromHexString(hex.ToString()), goalWidth, goalHeight, legacyPath);
    }

    private static void AppendPicture(StringBuilder output, RtfPictureReplacement replacement, RtfPicture original)
    {
        output.Append("{\\pict\\").Append(replacement.Format);
        if (original.GoalWidthTwips is > 0)
        {
            output.Append("\\picwgoal").Append(original.GoalWidthTwips.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (original.GoalHeightTwips is > 0)
        {
            output.Append("\\pichgoal").Append(original.GoalHeightTwips.Value.ToString(CultureInfo.InvariantCulture));
        }

        output.Append('\n');
        var hex = Convert.ToHexString(replacement.Data);
        for (var offset = 0; offset < hex.Length; offset += 128)
        {
            output.Append(hex, offset, Math.Min(128, hex.Length - offset)).Append('\n');
        }

        output.Append('}');
    }

    private static string? DecodeMarker(ReadOnlySpan<char> encoded)
    {
        var buffer = new byte[encoded.Length];
        return Convert.TryFromBase64Chars(encoded, buffer, out var written) ? Encoding.UTF8.GetString(buffer, 0, written) : null;
    }
}
