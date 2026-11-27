namespace PixFix.Core.Knowledge;

/// <summary>Eintrag der Wissensdatenbank ohne Inhalt – für Liste und Suche.</summary>
public sealed record KnowledgeListItem
{
    public required string Id { get; init; }
    public required string SectionId { get; init; }
    public required string SectionName { get; init; }
    public int SectionOrder { get; init; }
    public int SortOrder { get; init; }
    public required string Title { get; init; }
    public string Tags { get; init; } = string.Empty;
    public string OcrText { get; init; } = string.Empty;
    public string PlainText { get; init; } = string.Empty;
    public bool IsFavorite { get; init; }
    public bool IsProtected { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
