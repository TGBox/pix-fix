using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PixFix.Core.Common;

namespace PixFix.Core.Legacy;

/// <summary>
/// Liest die Copy-Actions aus PixFix 69: durch <c>|</c> getrennte Base64-Werte (UTF-8),
/// jeweils optional mit Präfix <c>@PFCP:&lt;Vorkommen&gt;\n</c>.
/// </summary>
public static partial class CopyActionCodec
{
    [GeneratedRegex(@"^@PFCP:(\d+)\n", RegexOptions.CultureInvariant)]
    private static partial Regex OccurrencePrefix();

    public static IReadOnlyList<CopyAction> DecodeLegacy(string? value)
    {
        var result = new List<CopyAction>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (var item in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var raw = DecodeBase64(item.Trim());
            if (raw is null)
            {
                continue;
            }

            var occurrence = 0;
            var text = raw;
            var match = OccurrencePrefix().Match(raw);
            if (match.Success)
            {
                occurrence = int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? Math.Max(0, parsed) : 0;
                text = raw[match.Length..];
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Wie in PixFix 69: derselbe Text wird nur einmal übernommen.
            if (result.Exists(x => string.Equals(x.Text, text, StringComparison.Ordinal)))
            {
                continue;
            }

            result.Add(new CopyAction(text, occurrence));
        }

        return result;
    }

    private static string? DecodeBase64(string value)
    {
        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var written)
            ? Encoding.UTF8.GetString(buffer, 0, written)
            : null;
    }
}
