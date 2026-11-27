using System.Text;
using System.Text.RegularExpressions;

namespace PixFix.Core.Legacy;

/// <summary>
/// PixFix 69 markiert eingebettete Bilder im RTF zusätzlich mit <c>[[PFIMG:&lt;Base64 des relativen Pfads&gt;]]</c>.
/// Über diese Marker werden Bilder im Dokument den Bilddateien zugeordnet.
/// </summary>
public static partial class LegacyImageMarker
{
    [GeneratedRegex(@"\[\[PFIMG:(?<path>[A-Za-z0-9+/=]+)\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    public static string Encode(string relativePath) =>
        "[[PFIMG:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath)) + "]]";

    /// <summary>Alle Bildpfade in der Reihenfolge ihres Vorkommens.</summary>
    public static IReadOnlyList<string> FindAll(string? content)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(content))
        {
            return result;
        }

        foreach (Match match in MarkerPattern().Matches(content))
        {
            var encoded = match.Groups["path"].Value;
            var buffer = new byte[encoded.Length];
            if (Convert.TryFromBase64String(encoded, buffer, out var written))
            {
                result.Add(Encoding.UTF8.GetString(buffer, 0, written));
            }
        }

        return result;
    }
}
