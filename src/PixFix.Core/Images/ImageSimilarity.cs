using System.Numerics;
using System.Text.RegularExpressions;
using PixFix.Core.Knowledge;

namespace PixFix.Core.Images;

/// <summary>
/// Bewertung der Bildsuche, 1:1 aus PixFix 69 (<c>PixFix.Search.cs</c>: <c>HashSimilarity</c>, <c>SignatureSimilarity</c>,
/// <c>OcrCoverage</c>, <c>ErrorCodeCoverage</c> und die Gesamtformel in <c>ShowImageSearchResults</c>).
/// Die Trefferqualität wurde dort über viele Versionen abgestimmt – Zahlen nur mit Vergleichstest ändern (SPEC F2).
/// </summary>
public static partial class ImageSimilarity
{
    [GeneratedRegex(@"(?i)\b(?:0x[0-9a-f]{3,}|[a-z]{1,5}[-_:]?\d{2,}|\d{3,}[-_/]?[a-z0-9]*)\b")]
    private static partial Regex ErrorCodePattern();

    /// <summary>Anzahl gleicher Bits (0–64).</summary>
    public static int HashSimilarity(ulong a, ulong b) => 64 - BitOperations.PopCount(a ^ b);

    /// <summary>Visuelle Ähnlichkeit 0–100: Pixel 35 %, Kanten 25 %, Helligkeit 15 %, Histogramm 15 %, Seitenverhältnis 10 %.</summary>
    public static int SignatureSimilarity(ImageSignature a, ImageSignature b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        long difference = 0;
        for (var i = 0; i < ImageSignature.PixelCount; i++)
        {
            difference += Math.Abs(a.Pixels[i] - b.Pixels[i]);
        }

        var pixel = 100.0 - (difference * 100.0 / (255.0 * 256.0));

        var intersection = 0;
        for (var i = 0; i < ImageSignature.HistogramBins; i++)
        {
            intersection += Math.Min(a.Histogram[i], b.Histogram[i]);
        }

        var histogram = intersection * 100.0 / 256.0;
        var average = HashSimilarity(a.AverageHash, b.AverageHash) * 100.0 / 64.0;
        var edges = HashSimilarity(a.DifferenceHash, b.DifferenceHash) * 100.0 / 64.0;
        var aspect = Math.Min(a.Aspect, b.Aspect) / Math.Max(a.Aspect, b.Aspect) * 100.0;
        return (int)Math.Round((pixel * .35) + (edges * .25) + (average * .15) + (histogram * .15) + (aspect * .10));
    }

    /// <summary>
    /// Beste Übereinstimmung aus Gesamtbild und Bildmitte; Kreuzvergleiche (ganz gegen Mitte) mit 3 Punkten Abzug.
    /// Fängt Screenshots ab, die enger oder weiter zugeschnitten sind als das gespeicherte Bild.
    /// </summary>
    public static int Visual(ImageSignaturePair query, ImageSignaturePair stored)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(stored);
        var full = SignatureSimilarity(query.Full, stored.Full);
        var center = SignatureSimilarity(query.Center, stored.Center);
        var cross1 = Math.Max(0, SignatureSimilarity(query.Full, stored.Center) - 3);
        var cross2 = Math.Max(0, SignatureSimilarity(query.Center, stored.Full) - 3);
        return Math.Max(Math.Max(full, center), Math.Max(cross1, cross2));
    }

    /// <summary>Anteil (0–100) der Wörter des erkannten Bildtexts, die im OCR-Text oder Inhalt des Eintrags vorkommen.</summary>
    public static int OcrCoverage(KnowledgeListItem entry, string? recognized)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var wanted = KnowledgeSearch.Tokens(recognized).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return 0;
        }

        var haystack = (entry.OcrText + " " + entry.PlainText).ToLowerInvariant();
        return wanted.Count(t => haystack.Contains(t, StringComparison.Ordinal)) * 100 / wanted.Count;
    }

    /// <summary>Anteil (0–100) der Fehlercodes (z. B. <c>0x80070005</c>, <c>E-1234</c>, <c>404</c>) aus dem Bildtext, die im Eintrag vorkommen.</summary>
    public static int ErrorCodeCoverage(KnowledgeListItem entry, string? recognized)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var wanted = ErrorCodePattern().Matches(recognized ?? string.Empty)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
        {
            return 0;
        }

        var haystack = (entry.Title + " " + entry.OcrText + " " + entry.PlainText).ToLowerInvariant();
        return wanted.Count(code => haystack.Contains(code, StringComparison.Ordinal)) * 100 / wanted.Count;
    }

    /// <summary>Gesamtpunktzahl: Bild × 250 + Textabdeckung × 100 + Fehlercodes × 160 + Textsuche-Punkte.</summary>
    public static int TotalScore(int visual, int coverage, int errorCodes, int textScore) =>
        (visual * 250) + (coverage * 100) + (errorCodes * 160) + textScore;
}
