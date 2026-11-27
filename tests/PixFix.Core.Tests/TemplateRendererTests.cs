using PixFix.Core.Templates;

namespace PixFix.Core.Tests;

public class TemplateRendererTests
{
    [Fact]
    public void ReplacesKnownPlaceholders()
    {
        var values = new Dictionary<string, string?>
        {
            [TemplatePlaceholders.CustomerNumber] = "00074",
            [TemplatePlaceholders.Reason] = "Drucker druckt nicht",
            [TemplatePlaceholders.Solution] = "Treiber neu installiert",
        };

        var result = TemplateRenderer.Render("L-Nr. {LNUM}: {WARUM} --> {LOESUNG}", values);

        Assert.Equal("L-Nr. 00074: Drucker druckt nicht --> Treiber neu installiert", result);
    }

    [Fact]
    public void KeepsUnknownPlaceholders()
    {
        var result = TemplateRenderer.Render("Hallo {NAME}", new Dictionary<string, string?>());

        Assert.Equal("Hallo {NAME}", result);
    }

    [Fact]
    public void NullValueBecomesEmpty()
    {
        var values = new Dictionary<string, string?> { [TemplatePlaceholders.Fax] = null };

        Assert.Equal("Fax: ", TemplateRenderer.Render("Fax: {FAX}", values));
    }
}
