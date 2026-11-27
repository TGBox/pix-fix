using PixFix.Core.Images;
using PixFix.Core.Knowledge;

namespace PixFix.Core.Tests;

public class ImageSimilarityTests
{
    private static ImageSignature Uniform(byte value, ulong hash, double aspect)
    {
        var histogram = new int[16];
        histogram[value / 16] = 256;
        return new ImageSignature(hash, hash, Enumerable.Repeat(value, 256).ToArray(), histogram, aspect);
    }

    private static KnowledgeListItem Entry(string title = "T", string ocr = "", string text = "") =>
        new() { Id = title, SectionId = "s", SectionName = "S", Title = title, OcrText = ocr, PlainText = text };

    [Fact]
    public void HashSimilarityCountsEqualBits()
    {
        Assert.Equal(64, ImageSimilarity.HashSimilarity(0xABCDUL, 0xABCDUL));
        Assert.Equal(61, ImageSimilarity.HashSimilarity(0UL, 0b111UL));
        Assert.Equal(0, ImageSimilarity.HashSimilarity(0UL, ulong.MaxValue));
    }

    [Fact]
    public void IdenticalSignaturesAre100Percent()
    {
        var signature = Uniform(128, 0x1234UL, 1.5);

        Assert.Equal(100, ImageSimilarity.SignatureSimilarity(signature, signature));
    }

    [Fact]
    public void OppositeSignaturesOnlyScoreAspect()
    {
        // Pixel 0 %, Kanten 0 %, Helligkeit 0 %, Histogramm 0 %, Seitenverhältnis 1:2 = 50 % → 50 × 0,10 = 5
        var black = Uniform(0, 0UL, 1.0);
        var white = Uniform(255, ulong.MaxValue, 2.0);

        Assert.Equal(5, ImageSimilarity.SignatureSimilarity(black, white));
    }

    [Fact]
    public void VisualUsesBestOfFullCenterAndCrossWithPenalty()
    {
        var a = Uniform(0, 0UL, 1.0);
        var b = Uniform(255, ulong.MaxValue, 1.0);

        // Nur der Kreuzvergleich passt (query.Full ↔ stored.Center): 100 − 3
        var visual = ImageSimilarity.Visual(new ImageSignaturePair(a, b), new ImageSignaturePair(b, a));

        Assert.Equal(97, visual);
    }

    [Fact]
    public void OcrCoverageCountsMatchingWords()
    {
        var entry = Entry(ocr: "Drucker ist offline");

        Assert.Equal(66, ImageSimilarity.OcrCoverage(entry, "Drucker offline Fehler"));
        Assert.Equal(0, ImageSimilarity.OcrCoverage(entry, ""));
    }

    [Fact]
    public void ErrorCodeCoverageFindsCodes()
    {
        var entry = Entry(title: "Druckfehler 0x80070005");

        Assert.Equal(50, ImageSimilarity.ErrorCodeCoverage(entry, "Fehler 0x80070005 bei Druck E-1234"));
        Assert.Equal(0, ImageSimilarity.ErrorCodeCoverage(entry, "nur Text ohne Code"));
    }

    [Fact]
    public void TotalScoreUsesPixFix69Weights()
    {
        Assert.Equal((80 * 250) + (50 * 100) + (100 * 160) + 1234, ImageSimilarity.TotalScore(80, 50, 100, 1234));
    }

    [Fact]
    public void RankPutsMostSimilarImageFirstAndLimitsTo60()
    {
        var query = new ImageSignaturePair(Uniform(100, 0x0FUL, 1.0), Uniform(100, 0x0FUL, 1.0));
        var same = new ImageCandidate(Entry("Gleich"), "a", query);
        var other = new ImageCandidate(Entry("Anders"), "b", new ImageSignaturePair(Uniform(250, ulong.MaxValue, 3.0), Uniform(250, ulong.MaxValue, 3.0)));
        var many = Enumerable.Range(0, 100).Select(i => other with { AssetId = "x" + i });

        var hits = ImageSearch.Rank(new[] { other, same }.Concat(many), query, null);

        Assert.Equal(60, hits.Count);
        Assert.Equal("Gleich", hits[0].Entry.Title);
        Assert.Equal(100, hits[0].Visual);
        Assert.Equal(100 * 250, hits[0].Score);
    }
}
