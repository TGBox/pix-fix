namespace PixFix.Core.Common;

/// <summary>
/// Gespeichertes Format eines formatierten Inhalts. Zielformat ist <see cref="Xaml"/> (SPEC 6.2);
/// aus PixFix 69 übernommene Inhalte bleiben bis zur Umwandlung im Editor (Phase 2) RTF bzw. HTML.
/// </summary>
public static class ContentFormat
{
    public const string Xaml = "xaml";
    public const string Rtf = "rtf";
    public const string Html = "html";
    public const string PlainText = "text";
}
