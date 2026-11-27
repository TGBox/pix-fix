using PixFix.Core.Updates;

namespace PixFix.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData(" V10.0.7 ", 10, 0, 7)]
    [InlineData("1.2.3.0", 1, 2, 3)]
    [InlineData("1.2.3+4f2c9e1", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    public void TryParseReadsTagsAndAssemblyVersions(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.3-beta.1")]
    [InlineData("latest")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3.4.5")]
    public void TryParseRejectsEverythingElse(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void IsNewerComparesNumerically(string latest, string current, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsNewer(Version.Parse(latest), Version.Parse(current)));
    }

    [Fact]
    public void IsNewerIgnoresMissingAndRevisionParts()
    {
        Assert.False(AppVersion.IsNewer(new Version(1, 2), new Version(1, 2, 0)));
        Assert.False(AppVersion.IsNewer(new Version(1, 2, 3, 9), new Version(1, 2, 3)));
    }

    [Theory]
    [InlineData("1.0.0-dev", true)]
    [InlineData("1.0.0-dev+4f2c9e1", true)]
    [InlineData("1.0.0-DEV.3", true)]
    [InlineData("1.1.0", false)]
    [InlineData("1.1.0+4f2c9e1", false)]
    [InlineData("1.1.0-beta", false)]
    [InlineData("1.1.0+dev-branch", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDevelopmentBuildDetectsDevSuffix(string? informational, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsDevelopmentBuild(informational));
    }

    [Fact]
    public void FormatShowsThreeParts()
    {
        Assert.Equal("1.2.0", AppVersion.Format(new Version(1, 2)));
        Assert.Equal("1.2.3", AppVersion.Format(new Version(1, 2, 3, 4)));
    }
}
