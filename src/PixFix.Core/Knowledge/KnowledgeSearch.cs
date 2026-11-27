using System.Text.RegularExpressions;

namespace PixFix.Core.Knowledge;

public sealed record KnowledgeSearchHit(KnowledgeListItem Entry, int Score, string Snippet);

/// <summary>
/// Textsuche der Wissensdatenbank (SPEC F2). Die Gewichtung ist 1:1 aus PixFix 69
/// (<c>PixFix.Search.cs</c>, <c>Score</c>/<c>Tokens</c>/<c>MatchSnippet</c>) übernommen, damit die Trefferreihenfolge
/// gleich bleibt. Änderungen an den Zahlen nur mit Vergleichstest.
/// </summary>
public static partial class KnowledgeSearch
{
    private const int SnippetLead = 55;
    private const int SnippetLength = 170;

    [GeneratedRegex(@"[\p{L}\p{N}_-]{2,}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Nicht archivierte Einträge mit Punktzahl &gt; 0, absteigend nach Punktzahl, dann nach Titel.
    /// Ohne Suchbegriff: alle nicht archivierten Einträge in Abschnitts- und Eintragsreihenfolge.
    /// Mit <paramref name="includeArchived"/> werden archivierte Einträge mit angezeigt (zum Zurückholen aus dem Archiv).
    /// </summary>
    public static IReadOnlyList<KnowledgeSearchHit> Search(IEnumerable<KnowledgeListItem> entries, string? query, bool includeArchived = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var active = entries.Where(e => includeArchived || !e.IsArchived);

        if (string.IsNullOrWhiteSpace(query))
        {
            return active
                .OrderBy(e => e.SectionOrder).ThenBy(e => e.SectionName, StringComparer.CurrentCulture).ThenBy(e => e.SortOrder)
                .Select(e => new KnowledgeSearchHit(e, 1, string.Empty))
                .ToList();
        }

        var q = query.Trim();
        return active
            .Select(e => new { Entry = e, Value = Score(e, q) })
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Entry.Title, StringComparer.CurrentCulture)
            .Select(x => new KnowledgeSearchHit(x.Entry, x.Value, Snippet(x.Entry, q)))
            .ToList();
    }

    public static IEnumerable<string> Tokens(string? text) =>
        TokenPattern().Matches((text ?? string.Empty).ToLowerInvariant()).Select(m => m.Value);

    public static int Score(KnowledgeListItem entry, string? query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(query))
        {
            return 1;
        }

        var titleLower = entry.Title.ToLowerInvariant();
        var tagsLower = entry.Tags.ToLowerInvariant();
        var ocrLower = entry.OcrText.ToLowerInvariant();
        var haystack = Collapse(
            (entry.Title + " " + entry.SectionName + " " + entry.Tags + " " + entry.OcrText + " " + entry.PlainText).ToLowerInvariant());
        var needle = Collapse(query.ToLowerInvariant());
        var tokens = Tokens(needle).Distinct().ToList();

        var value = haystack.Contains(needle, StringComparison.Ordinal) ? 10000 + (needle.Length * 20) : 0;
        var matched = 0;
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.Ordinal))
            {
                matched++;
            }

            // Höchstens 9 Vorkommen zählen (wie in PixFix 69: c++ < 8).
            var position = 0;
            var count = 0;
            while ((position = haystack.IndexOf(token, position, StringComparison.Ordinal)) >= 0 && count++ < 8)
            {
                value += token.Length >= 5 ? 20 : 10;
                position += token.Length;
            }

            if (titleLower.Contains(token, StringComparison.Ordinal))
            {
                value += 40;
            }

            if (tagsLower.Contains(token, StringComparison.Ordinal))
            {
                value += 60;
            }

            if (ocrLower.Contains(token, StringComparison.Ordinal))
            {
                value += 25;
            }
        }

        value += tokens.Count > 1 && matched == tokens.Count ? 1500 : matched * 100;
        return value;
    }

    /// <summary>Textausschnitt um die erste Fundstelle (Klartext, sonst OCR-Text).</summary>
    public static string Snippet(KnowledgeListItem entry, string? query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = string.IsNullOrWhiteSpace(entry.PlainText) ? entry.OcrText : entry.PlainText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = Collapse(text);
        var q = query ?? string.Empty;
        var at = q.Length > 0 ? flat.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) : -1;
        if (at < 0)
        {
            foreach (var token in Tokens(q))
            {
                at = flat.IndexOf(token, StringComparison.CurrentCultureIgnoreCase);
                if (at >= 0)
                {
                    break;
                }
            }
        }

        at = Math.Max(0, at);
        var start = Math.Max(0, at - SnippetLead);
        var length = Math.Min(SnippetLength, flat.Length - start);
        return (start > 0 ? "…" : string.Empty) + flat.Substring(start, length) + (start + length < flat.Length ? "…" : string.Empty);
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();
}
