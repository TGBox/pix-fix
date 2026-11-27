using PixFix.Core.Common;

namespace PixFix.Core.Workspaces;

/// <summary>Schreibbereich, z. B. „Aktuelle Fehler“ oder „Werkzeuge“ (SPEC F3).</summary>
public sealed record Workspace(string Id, string Title, string Icon, int SortOrder, bool IsProtected);

/// <summary>Reiter innerhalb eines Abschnitts eines Schreibbereichs.</summary>
public sealed record InformationEntry
{
    public required string Id { get; init; }
    public required string WorkspaceId { get; init; }
    public required string Section { get; init; }
    public int SectionOrder { get; init; }
    public bool SectionCollapsed { get; init; }
    public int SortOrder { get; init; }
    public required string Title { get; init; }
    public string PlainText { get; init; } = string.Empty;
    public required string ContentFormat { get; init; }
    public string? Content { get; init; }
    public bool IsProtected { get; init; }
    public int CursorPosition { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public IReadOnlyList<InformationAttachment> Attachments { get; init; } = [];
    public IReadOnlyList<CopyAction> CopyActions { get; init; } = [];
}

public sealed record InformationAttachment(int Ordinal, string AssetId, string DisplayName, DateTimeOffset AddedUtc);
