using PixFix.Core.Documents;
using PixFix.Core.Legacy;

namespace PixFix.Core.Tests;

public class RtfPictureRewriterTests
{
    private static readonly RtfPictureReplacement Png = new("pngblip", [0x89, 0x50, 0x4E, 0x47]);

    [Fact]
    public void ReplacesPictureAndRemovesMarker()
    {
        var rtf = "{\\rtf1 Text {\\v " + LegacyImageMarker.Encode(@"images\a-1.png") + "}\\cf3\\v0"
            + "{\\pict\\wmetafile8\\picw100\\pich50\\picwgoal1500\\pichgoal750 0102AB\n03}"
            + " Ende}";
        RtfPicture? seen = null;

        var result = RtfPictureRewriter.Rewrite(rtf, p =>
        {
            seen = p;
            return Png;
        });

        Assert.NotNull(seen);
        Assert.Equal("wmetafile", seen.Format);
        Assert.Equal(new byte[] { 0x01, 0x02, 0xAB, 0x03 }, seen.Data);
        Assert.Equal(1500, seen.GoalWidthTwips);
        Assert.Equal(750, seen.GoalHeightTwips);
        Assert.Equal(@"images\a-1.png", seen.LegacyPath);

        Assert.DoesNotContain("PFIMG", result, StringComparison.Ordinal);
        Assert.Contains("{\\pict\\pngblip\\picwgoal1500\\pichgoal750\n89504E47\n}", result, StringComparison.Ordinal);
        Assert.StartsWith("{\\rtf1 Text ", result, StringComparison.Ordinal);
        Assert.EndsWith(" Ende}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void PictureWithoutMarkerHasNoLegacyPath()
    {
        var rtf = "{\\rtf1 {\\pict\\pngblip\\picwgoal10\\pichgoal10 FFD8}}";
        RtfPicture? seen = null;

        var result = RtfPictureRewriter.Rewrite(rtf, p =>
        {
            seen = p;
            return null;
        });

        Assert.NotNull(seen);
        Assert.Null(seen.LegacyPath);
        Assert.Equal(rtf, result);
    }

    [Fact]
    public void MarkerFarAwayIsNotUsed()
    {
        var rtf = "{\\rtf1 " + LegacyImageMarker.Encode("images\\x.png") + new string('a', 1000)
            + "{\\pict\\wmetafile8 00}}";
        RtfPicture? seen = null;

        RtfPictureRewriter.Rewrite(rtf, p =>
        {
            seen = p;
            return null;
        });

        Assert.Null(seen!.LegacyPath);
    }

    [Fact]
    public void IgnoresNestedGroupsAndKeepsEscapedBraces()
    {
        var rtf = "{\\rtf1 a \\{ b \\} {\\pict{\\*\\blipuid 0123456789abcdef}\\jpegblip AABB}}";
        RtfPicture? seen = null;

        var result = RtfPictureRewriter.Rewrite(rtf, p =>
        {
            seen = p;
            return Png;
        });

        Assert.Equal(new byte[] { 0xAA, 0xBB }, seen!.Data);
        Assert.StartsWith("{\\rtf1 a \\{ b \\} {\\pict\\pngblip\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWithoutPicturesIsUnchanged()
    {
        const string rtf = "{\\rtf1\\ansi {\\fonttbl{\\f0 Segoe UI;}} Umlaute: \\'fc \\par}";

        Assert.Equal(rtf, RtfPictureRewriter.Rewrite(rtf, _ => Png));
    }

    [Fact]
    public void LongDataIsWrappedInLines()
    {
        var data = Enumerable.Repeat((byte)0x11, 200).ToArray();
        var rtf = "{\\rtf1 {\\pict\\wmetafile8 00}}";

        var result = RtfPictureRewriter.Rewrite(rtf, _ => new RtfPictureReplacement("pngblip", data));

        var lines = result.Split('\n');
        Assert.All(lines, l => Assert.True(l.Length <= 128 + 30));
        Assert.Contains(new string('1', 128), result, StringComparison.Ordinal);
    }
}
