using PixFix.Core.Common;

namespace PixFix.Core.Tests;

public sealed class ThemeColorContrastTests
{
    [Theory]
    [InlineData(0, 0, 255)]
    [InlineData(31, 73, 125)]
    [InlineData(0, 0, 128)]
    public void AdaptForDarkSurfaceLightensDarkBluesAndPreservesHue(byte r, byte g, byte b)
    {
        var input = new ThemeColorContrast.RgbColor(r, g, b);

        var adapted = ThemeColorContrast.AdaptForDarkSurface(input);

        Assert.True(
            ThemeColorContrast.ContrastRatio(adapted, ThemeColorContrast.DarkEditorSurface)
            >= ThemeColorContrast.TargetDarkTextContrast - 0.1);
        Assert.True(adapted.B > adapted.R && adapted.B > adapted.G);
    }

    [Fact]
    public void AdaptForDarkSurfaceLightensDarkRedAndGreenWhilePreservingDominantChannel()
    {
        var darkRed = new ThemeColorContrast.RgbColor(139, 0, 0);
        var darkGreen = new ThemeColorContrast.RgbColor(0, 100, 0);

        var adaptedRed = ThemeColorContrast.AdaptForDarkSurface(darkRed);
        var adaptedGreen = ThemeColorContrast.AdaptForDarkSurface(darkGreen);

        Assert.True(
            ThemeColorContrast.ContrastRatio(adaptedRed, ThemeColorContrast.DarkEditorSurface)
            >= ThemeColorContrast.TargetDarkTextContrast - 0.1);
        Assert.True(adaptedRed.R > adaptedRed.G && adaptedRed.R > adaptedRed.B);

        Assert.True(
            ThemeColorContrast.ContrastRatio(adaptedGreen, ThemeColorContrast.DarkEditorSurface)
            >= ThemeColorContrast.TargetDarkTextContrast - 0.1);
        Assert.True(adaptedGreen.G > adaptedGreen.R && adaptedGreen.G > adaptedGreen.B);
    }

    [Theory]
    [InlineData(117, 190, 255)]
    [InlineData(255, 215, 0)]
    [InlineData(240, 243, 248)]
    public void AdaptForDarkSurfaceLeavesAlreadyLightColorsUnchanged(byte r, byte g, byte b)
    {
        var input = new ThemeColorContrast.RgbColor(r, g, b);

        var adapted = ThemeColorContrast.AdaptForDarkSurface(input);

        Assert.Equal(input, adapted);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(34, 34, 34, true)]
    [InlineData(51, 51, 51, true)]
    [InlineData(31, 73, 125, false)]
    [InlineData(160, 160, 160, false)]
    public void IsDefaultDarkTextIdentifiesAchromaticDarkColors(byte r, byte g, byte b, bool expected)
    {
        Assert.Equal(expected, ThemeColorContrast.IsDefaultDarkText(new(r, g, b)));
    }

    [Theory]
    [InlineData(255, 255, 255, true)]
    [InlineData(250, 250, 250, true)]
    [InlineData(241, 245, 249, false)]
    [InlineData(255, 255, 0, false)]
    public void IsNearWhiteBackgroundPreservesCodeSnippetBackground(byte r, byte g, byte b, bool expected)
    {
        Assert.Equal(expected, ThemeColorContrast.IsNearWhiteBackground(new(r, g, b)));
    }
}
