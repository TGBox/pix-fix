using System.Text.RegularExpressions;

namespace PixFix.Core.Templates;

/// <summary>Bekannte Platzhalter in E-Mail-, Fax- und Anrufvorlagen (SPEC F6).</summary>
public static class TemplatePlaceholders
{
    public const string CustomerNumber = "LNUM";
    public const string Id = "ID";
    public const string Reason = "WARUM";
    public const string Solution = "LOESUNG";
    public const string Email = "EMAIL";
    public const string Fax = "FAX";
}

/// <summary>Ersetzt Platzhalter der Form <c>{NAME}</c> in Vorlagentexten.</summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(?<name>[A-ZÄÖÜ_]+)\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// Ersetzt alle bekannten Platzhalter. Unbekannte Platzhalter bleiben unverändert stehen,
    /// damit Tippfehler in Vorlagen sichtbar bleiben statt still zu verschwinden.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return PlaceholderPattern().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return values.TryGetValue(name, out var value) ? value ?? string.Empty : match.Value;
        });
    }
}
