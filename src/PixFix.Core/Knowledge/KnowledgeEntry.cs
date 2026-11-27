using PixFix.Core.Common;

namespace PixFix.Core.Knowledge;

/// <summary>Abschnitt des PixFix-Bereichs (in PixFix 69: Kategorie).</summary>
public sealed record KnowledgeSection(string Id, string Name, int SortOrder);

/// <summary>Eintrag der Wissensdatenbank (SPEC F1).</summary>
public sealed record KnowledgeEntry
{
    public required string Id { get; init; }
    public required string SectionId { get; init; }
    public required string Title { get; init; }
    public string Tags { get; init; } = string.Empty;
    public string OcrText { get; init; } = string.Empty;

    /// <summary>Klartext des Inhalts für Suche und Vorschau.</summary>
    public string PlainText { get; init; } = string.Empty;

    public required string ContentFormat { get; init; }

    /// <summary>Inhalt direkt in der Datenbank (kleine Inhalte).</summary>
    public string? Content { get; init; }

    /// <summary>Inhalt als Datei (große Inhalte, z. B. übernommene RTF-Dokumente mit Bildern).</summary>
    public string? ContentAssetId { get; init; }

    public int SortOrder { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsProtected { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public IReadOnlyList<KnowledgeEntryImage> Images { get; init; } = [];
    public IReadOnlyList<CopyAction> CopyActions { get; init; } = [];
}

public sealed record KnowledgeEntryImage(int Ordinal, string AssetId, bool IsPrimary, string? LegacyPath);
