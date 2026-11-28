using System.Globalization;
using System.Xml.Linq;
using PixFix.Core.Common;

namespace PixFix.Core.Tests;

/// <summary>
/// Prüft die Farbpaletten der Oberfläche (src/PixFix.App/Themes) auf lesbaren Kontrast nach WCAG 2.1 –
/// in Hell und Dunkel, für die farbigen Schaltflächen und die Grundtexte.
/// </summary>
public sealed class ThemePaletteContrastTests
{
    private const double MinimumTextContrast = 4.5;

    // Hintergrund und Schrift der farbigen Schaltflächen (Einstellung „Farbig“, Hauptaktion auch in „Dezent“).
    private static readonly string[] Roles = ["Neutral", "Primary", "Create", "Danger", "Warning", "Action"];

    public static TheoryData<string> Palettes => new() { "LightPalette.xaml", "DarkPalette.xaml" };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void RoleButtonTextIsReadable(string palette)
    {
        var colors = LoadPalette(palette);

        foreach (var role in Roles)
        {
            var background = colors[$"AppRole{role}Background"];
            var foreground = colors[$"AppRole{role}Foreground"];
            var ratio = ThemeColorContrast.ContrastRatio(background, foreground);
            Assert.True(ratio >= MinimumTextContrast, $"{palette}: {role} hat nur {ratio:0.00}:1");
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void RoleButtonsStandOutFromTheWindow(string palette)
    {
        var colors = LoadPalette(palette);
        var window = colors["AppWindowBackground"];

        foreach (var role in Roles)
        {
            // Fläche oder Rahmen muss sich vom Fensterhintergrund abheben (Nicht-Text-Kontrast, mindestens 3:1).
            var fill = ThemeColorContrast.ContrastRatio(colors[$"AppRole{role}Background"], window);
            var border = ThemeColorContrast.ContrastRatio(colors[$"AppRole{role}Border"], window);
            Assert.True(Math.Max(fill, border) >= 3.0, $"{palette}: {role} hebt sich nicht ab ({fill:0.00}/{border:0.00})");
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void BaseTextIsReadableOnSurfaces(string palette)
    {
        var colors = LoadPalette(palette);

        foreach (var surface in new[] { "AppWindowBackground", "AppSurfaceBackground", "AppSidebarBackground" })
        {
            foreach (var text in new[] { "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush" })
            {
                var ratio = ThemeColorContrast.ContrastRatio(colors[text], colors[surface]);
                Assert.True(ratio >= MinimumTextContrast, $"{palette}: {text} auf {surface} nur {ratio:0.00}:1");
            }
        }
    }

    private static Dictionary<string, ThemeColorContrast.RgbColor> LoadPalette(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "PixFix.App", "Themes", fileName);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = new Dictionary<string, ThemeColorContrast.RgbColor>(StringComparer.Ordinal);
        foreach (var brush in XDocument.Load(path).Root!.Elements().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = (string?)brush.Attribute(x + "Key");
            var color = (string?)brush.Attribute("Color");
            if (key is not null && color is { Length: 7 } && color[0] == '#')
            {
                colors[key] = new ThemeColorContrast.RgbColor(
                    byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return colors;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PixFix.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("PixFix.sln nicht gefunden");
    }
}
