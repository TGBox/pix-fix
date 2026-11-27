using System.Text;
using PixFix.Core.Legacy;

namespace PixFix.Core.Tests;

public class LegacyTimestampTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Theory]
    [InlineData("2026-01-15T10:00:00", "2026-01-15T09:00:00Z")]
    [InlineData("2026-07-15T10:00:00", "2026-07-15T08:00:00Z")]
    [InlineData("2026-03-29T02:30:00", "2026-03-29T01:30:00Z")]
    [InlineData("2026-10-19T07:19:30.7497087Z", "2026-10-19T07:19:30.7497087Z")]
    public void ConvertsLocalTimeToUtc(string legacy, string expectedUtc)
    {
        var expected = DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal((DateTimeOffset?)expected, LegacyTimestamp.ToUtc(legacy, Berlin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gestern")]
    public void InvalidValuesBecomeNull(string? legacy)
    {
        Assert.Null(LegacyTimestamp.ToUtc(legacy, Berlin));
    }
}

public class CopyActionCodecTests
{
    private static string Encode(string raw) => Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

    [Fact]
    public void DecodesPixFix69Format()
    {
        var legacy = string.Join('|', Encode("@PFCP:0\nping 8.8.8.8 -f -l 1500"), Encode("@PFCP:2\nnetsh interface ipv4 show interfaces"));

        var actions = CopyActionCodec.DecodeLegacy(legacy);

        Assert.Equal(2, actions.Count);
        Assert.Equal("ping 8.8.8.8 -f -l 1500", actions[0].Text);
        Assert.Equal(0, actions[0].Occurrence);
        Assert.Equal("netsh interface ipv4 show interfaces", actions[1].Text);
        Assert.Equal(2, actions[1].Occurrence);
    }

    [Fact]
    public void AcceptsValuesWithoutPrefix()
    {
        var action = Assert.Single(CopyActionCodec.DecodeLegacy(Encode("ipconfig /all")));

        Assert.Equal("ipconfig /all", action.Text);
        Assert.Equal(0, action.Occurrence);
    }

    [Fact]
    public void SkipsInvalidEmptyAndDuplicateValues()
    {
        var legacy = string.Join('|', "kein-base64!", Encode("@PFCP:0\n   "), Encode("a"), Encode("@PFCP:1\na"));

        var action = Assert.Single(CopyActionCodec.DecodeLegacy(legacy));

        Assert.Equal("a", action.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyInputGivesNoActions(string? legacy)
    {
        Assert.Empty(CopyActionCodec.DecodeLegacy(legacy));
    }
}

public class LegacyImageMarkerTests
{
    [Fact]
    public void FindsMarkersInOrder()
    {
        var rtf = "{\\rtf1 Text " + LegacyImageMarker.Encode(@"images\abc-1.png") + " mehr "
            + LegacyImageMarker.Encode(@"images\abc-2.gif") + "}";

        Assert.Equal(new[] { @"images\abc-1.png", @"images\abc-2.gif" }, LegacyImageMarker.FindAll(rtf));
    }

    [Fact]
    public void NoMarkersGivesEmptyList()
    {
        Assert.Empty(LegacyImageMarker.FindAll("{\\rtf1 nur Text}"));
    }
}

public class LegacyPathTests
{
    [Fact]
    public void CombinesRelativeWindowsPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pixfix-root");

        var result = LegacyPath.CombineWithin(root, @"images\a-1.png");

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "images", "a-1.png"), result);
    }

    [Theory]
    [InlineData(@"..\ausserhalb.txt")]
    [InlineData(@"images\..\..\ausserhalb.txt")]
    [InlineData("")]
    public void RejectsPathsOutsideRoot(string relative)
    {
        Assert.Null(LegacyPath.CombineWithin(Path.Combine(Path.GetTempPath(), "pixfix-root"), relative));
    }
}
