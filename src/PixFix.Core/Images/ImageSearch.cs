using PixFix.Core.Knowledge;

namespace PixFix.Core.Images;

/// <summary>Ein gespeichertes Bild als Kandidat der Bildsuche.</summary>
public sealed record ImageCandidate(KnowledgeListItem Entry, string AssetId, ImageSignaturePair Signature);

public sealed record ImageSearchHit(KnowledgeListItem Entry, string AssetId, int Score, int Visual, int TextCoverage);

/// <summary>Bildsuche über alle gespeicherten Bilder (SPEC F2), Reihenfolge wie in PixFix 69.</summary>
public static class ImageSearch
{
    /// <summary>PixFix 69 zeigt höchstens 60 Treffer.</summary>
    public const int MaxHits = 60;

    public static IReadOnlyList<ImageSearchHit> Rank(IEnumerable<ImageCandidate> candidates, ImageSignaturePair query, string? recognizedText)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(query);

        return candidates
            .Select(c =>
            {
                var visual = ImageSimilarity.Visual(query, c.Signature);
                var text = string.IsNullOrWhiteSpace(recognizedText) ? 0 : KnowledgeSearch.Score(c.Entry, recognizedText);
                var coverage = ImageSimilarity.OcrCoverage(c.Entry, recognizedText);
                var codes = ImageSimilarity.ErrorCodeCoverage(c.Entry, recognizedText);
                return new ImageSearchHit(c.Entry, c.AssetId, ImageSimilarity.TotalScore(visual, coverage, codes, text), visual, coverage);
            })
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Visual)
            // PixFix 69 lief parallel und hatte bei Gleichstand keine feste Reihenfolge; hier ist sie stabil.
            .ThenBy(h => h.Entry.Title, StringComparer.CurrentCulture)
            .ThenBy(h => h.AssetId, StringComparer.Ordinal)
            .Take(MaxHits)
            .ToList();
    }
}
