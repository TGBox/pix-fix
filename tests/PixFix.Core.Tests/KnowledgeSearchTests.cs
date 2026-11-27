using PixFix.Core.Knowledge;

namespace PixFix.Core.Tests;

public class KnowledgeSearchTests
{
    private static KnowledgeListItem Item(string title, string text = "", string tags = "", string ocr = "", string section = "Allgemein", bool archived = false) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SectionId = "s",
            SectionName = section,
            Title = title,
            PlainText = text,
            Tags = tags,
            OcrText = ocr,
            IsArchived = archived,
        };

    [Fact]
    public void ScoreMatchesPixFix69Formula()
    {
        // Werte von Hand nach PixFix 69 Search.cs nachgerechnet:
        // Heuhaufen "kim fehler allgemein  kim-modul startet nicht" → "kim fehler allgemein kim-modul startet nicht"
        var entry = Item("KIM Fehler", text: "KIM-Modul startet nicht");

        // Ganze Phrase "kim" enthalten: 10000 + 3*20 = 10060
        // Token "kim": 2 Vorkommen à 10 = 20, im Titel +40 → 60; matched = 1 → +100
        Assert.Equal(10060 + 60 + 100, KnowledgeSearch.Score(entry, "KIM"));

        // "kim startet": Phrase nicht enthalten (0); "kim" 20+40=60; "startet" 1×20 = 20; beide Tokens → +1500
        Assert.Equal(60 + 20 + 1500, KnowledgeSearch.Score(entry, "kim startet"));
    }

    [Fact]
    public void TagsAndOcrHaveOwnWeights()
    {
        var withTag = Item("A", tags: "drucker");
        var withOcr = Item("B", ocr: "drucker");

        // "drucker": Phrase 10000+7*20=10140; 1 Vorkommen (≥5 Zeichen) 20; Tag +60 bzw. OCR +25; matched +100
        Assert.Equal(10140 + 20 + 60 + 100, KnowledgeSearch.Score(withTag, "drucker"));
        Assert.Equal(10140 + 20 + 25 + 100, KnowledgeSearch.Score(withOcr, "drucker"));
    }

    [Fact]
    public void SearchExcludesArchivedAndOrdersByScoreThenTitle()
    {
        var entries = new[]
        {
            Item("Zebra", text: "drucker"),
            Item("Alpha", text: "drucker"),
            Item("Drucker Treiber", text: "drucker"),
            Item("Archiv", text: "drucker", archived: true),
            Item("Anderes Thema", text: "kim"),
        };

        var hits = KnowledgeSearch.Search(entries, "drucker");

        Assert.Equal(new[] { "Drucker Treiber", "Alpha", "Zebra" }, hits.Select(h => h.Entry.Title));
    }

    [Fact]
    public void ArchivedEntriesCanBeIncluded()
    {
        var entries = new[] { Item("Aktiv", text: "kim"), Item("Alt", text: "kim", archived: true) };

        Assert.Single(KnowledgeSearch.Search(entries, "kim"));
        Assert.Equal(2, KnowledgeSearch.Search(entries, "kim", includeArchived: true).Count);
        Assert.Equal(2, KnowledgeSearch.Search(entries, string.Empty, includeArchived: true).Count);
    }

    [Fact]
    public void EmptyQueryListsAllActiveEntriesInSectionOrder()
    {
        var entries = new[]
        {
            Item("B2", section: "B") with { SectionOrder = 2, SortOrder = 2 },
            Item("A1", section: "A") with { SectionOrder = 1, SortOrder = 1 },
            Item("B1", section: "B") with { SectionOrder = 2, SortOrder = 1 },
            Item("X", archived: true),
        };

        var hits = KnowledgeSearch.Search(entries, "  ");

        Assert.Equal(new[] { "A1", "B1", "B2" }, hits.Select(h => h.Entry.Title));
    }

    [Fact]
    public void SnippetShowsTextAroundHit()
    {
        var text = new string('x', 100) + " Konnektor neu starten " + new string('y', 300);
        var entry = Item("T", text: text);

        var snippet = KnowledgeSearch.Snippet(entry, "konnektor");

        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);
        Assert.Contains("Konnektor neu starten", snippet, StringComparison.Ordinal);
        Assert.Equal(170 + 2, snippet.Length);
    }

    [Fact]
    public void TokensIgnoreSingleCharacters()
    {
        // Bindestriche gehören wie in PixFix 69 zum Wort.
        Assert.Equal(new[] { "ti-störung", "kim-2" }, KnowledgeSearch.Tokens("TI-Störung? a KIM-2"));
    }
}
