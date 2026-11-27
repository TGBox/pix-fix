using System.Globalization;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Knowledge;

/// <summary>Gespeicherter Inhalt eines Eintrags: direkt in der Datenbank oder als Datei.</summary>
public sealed record KnowledgeContent(
    string EntryId,
    string Format,
    string? InlineContent,
    string? ContentAssetId,
    string? ContentFile,
    string PlainText,
    IReadOnlyDictionary<string, string> ImageFilesByLegacyPath);

/// <summary>Lesender Zugriff auf die Wissensdatenbank (Phase 2a). Schreiben folgt mit dem Editor.</summary>
public sealed class KnowledgeRepository(PixFixDatabase database, AssetStore assets)
{
    public IReadOnlyList<KnowledgeListItem> LoadAll()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            """
            SELECT e.id, e.section_id, s.name AS section_name, s.sort_order AS section_order, e.sort_order, e.title, e.tags,
                   e.ocr_text, e.plain_text, e.is_favorite, e.is_protected, e.is_archived, e.updated_utc
            FROM knowledge_entries e JOIN knowledge_sections s ON s.id = e.section_id
            ORDER BY s.sort_order, s.name, e.sort_order, e.title;
            """,
            null);
        using var reader = command.ExecuteReader();

        var result = new List<KnowledgeListItem>();
        while (reader.Read())
        {
            result.Add(new KnowledgeListItem
            {
                Id = reader.GetText("id"),
                SectionId = reader.GetText("section_id"),
                SectionName = reader.GetText("section_name"),
                SectionOrder = (int)reader.GetInteger("section_order"),
                SortOrder = (int)reader.GetInteger("sort_order"),
                Title = reader.GetText("title"),
                Tags = reader.GetText("tags"),
                OcrText = reader.GetText("ocr_text"),
                PlainText = reader.GetText("plain_text"),
                IsFavorite = reader.GetFlag("is_favorite"),
                IsProtected = reader.GetFlag("is_protected"),
                IsArchived = reader.GetFlag("is_archived"),
                UpdatedUtc = DateTimeOffset.TryParse(reader.GetText("updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updated)
                    ? updated
                    : DateTimeOffset.MinValue,
            });
        }

        return result;
    }

    public KnowledgeContent? LoadContent(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        using var db = database.Open();

        KnowledgeContent? content = null;
        using (var command = SqliteExtensions.Create(
            db,
            """
            SELECT e.content_format, e.content, e.content_asset_id, e.plain_text, a.relative_path
            FROM knowledge_entries e LEFT JOIN assets a ON a.id = e.content_asset_id
            WHERE e.id = $id;
            """,
            null,
            ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            var assetPath = reader.GetText("relative_path");
            var inline = reader.GetText("content");
            var assetId = reader.GetText("content_asset_id");
            content = new KnowledgeContent(
                entryId,
                reader.GetText("content_format"),
                inline.Length > 0 ? inline : null,
                assetId.Length > 0 ? assetId : null,
                assetPath.Length > 0 ? assets.GetFullPath(assetPath) : null,
                reader.GetText("plain_text"),
                new Dictionary<string, string>());
        }

        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var command = SqliteExtensions.Create(
            db,
            """
            SELECT i.legacy_path, a.relative_path
            FROM knowledge_entry_images i JOIN assets a ON a.id = i.asset_id
            WHERE i.entry_id = $id AND i.legacy_path IS NOT NULL;
            """,
            null,
            ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                images[reader.GetText("legacy_path")] = assets.GetFullPath(reader.GetText("relative_path"));
            }
        }

        return content with { ImageFilesByLegacyPath = images };
    }
}
