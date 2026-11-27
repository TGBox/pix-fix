using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixFix.Core.Common;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Knowledge;

/// <summary>Bild eines Eintrags mit Datei in der Ablage.</summary>
public sealed record KnowledgeImageRef(int Ordinal, string AssetId, string File, string? LegacyPath, string OcrText);

/// <summary>Gespeicherter Inhalt eines Eintrags: direkt in der Datenbank oder als Datei.</summary>
public sealed record KnowledgeContent(
    string EntryId,
    string Format,
    string? InlineContent,
    string? ContentAssetId,
    string? ContentFile,
    string PlainText,
    IReadOnlyList<KnowledgeImageRef> Images);

/// <summary>Neuer Inhalt beim Speichern aus dem Editor.</summary>
/// <param name="Xaml">FlowDocument als XAML; Bilder nur als Verweis auf die Ablage.</param>
/// <param name="PlainText">Klartext für Suche und Vorschau.</param>
/// <param name="ImageAssetIds">Bilder in Dokumentreihenfolge.</param>
public sealed record KnowledgeContentUpdate(string Xaml, string PlainText, IReadOnlyList<string> ImageAssetIds);

/// <summary>Abschnitt mit Anzahl seiner Einträge.</summary>
public sealed record KnowledgeSectionInfo(string Id, string Name, int SortOrder, int EntryCount);

/// <summary>Eintrag im Papierkorb.</summary>
public sealed record KnowledgeTrashItem(string TrashId, string Title, string SectionName, DateTimeOffset DeletedUtc);

/// <summary>Zugriff auf die Wissensdatenbank (SPEC F1).</summary>
public sealed class KnowledgeRepository(PixFixDatabase database, AssetStore assets, TimeProvider? timeProvider = null)
{
    public const string DefaultSection = "Allgemein";
    private const string TrashKind = "knowledge";
    private const string SnapshotFormat = "pixfix-knowledge-1";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    // ------------------------------------------------------------------ Lesen

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
                UpdatedUtc = ParseTime(reader.GetText("updated_utc")),
            });
        }

        return result;
    }

    public IReadOnlyList<string> LoadSectionNames()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT name FROM knowledge_sections ORDER BY sort_order, name;", null);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetText("name"));
        }

        return result;
    }

    public KnowledgeContent? LoadContent(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        using var db = database.Open();

        string format, plain;
        string? inline, assetId, contentFile;
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

            format = reader.GetText("content_format");
            plain = reader.GetText("plain_text");
            inline = NullIfEmpty(reader.GetText("content"));
            assetId = NullIfEmpty(reader.GetText("content_asset_id"));
            var relative = reader.GetText("relative_path");
            contentFile = relative.Length > 0 ? assets.GetFullPath(relative) : null;
        }

        var images = new List<KnowledgeImageRef>();
        using (var command = SqliteExtensions.Create(
            db,
            """
            SELECT i.ordinal, i.asset_id, i.legacy_path, i.ocr_text, a.relative_path
            FROM knowledge_entry_images i JOIN assets a ON a.id = i.asset_id
            WHERE i.entry_id = $id ORDER BY i.ordinal;
            """,
            null,
            ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                images.Add(new KnowledgeImageRef(
                    (int)reader.GetInteger("ordinal"),
                    reader.GetText("asset_id"),
                    assets.GetFullPath(reader.GetText("relative_path")),
                    NullIfEmpty(reader.GetText("legacy_path")),
                    reader.GetText("ocr_text")));
            }
        }

        return new KnowledgeContent(entryId, format, inline, assetId, contentFile, plain, images);
    }

    /// <summary>Datei eines Assets oder <c>null</c>, wenn es nicht existiert.</summary>
    public string? GetAssetFile(string assetId)
    {
        using var db = database.Open();
        return db.Scalar("SELECT relative_path FROM assets WHERE id = $id;", null, ("$id", assetId)) is string relative
            ? assets.GetFullPath(relative)
            : null;
    }

    // ------------------------------------------------------------------ Schreiben

    /// <summary>Legt einen leeren Eintrag am Ende des Abschnitts an (Abschnitt wird bei Bedarf erzeugt).</summary>
    public string CreateEntry(string sectionName, string title, string initialXaml = "")
    {
        var now = time.GetUtcNow().ToDb();
        var id = Guid.NewGuid().ToString("N");
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var sectionId = GetOrCreateSection(db, tx, sectionName);
        var order = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM knowledge_entries WHERE section_id = $s;", tx, ("$s", sectionId)),
            CultureInfo.InvariantCulture);
        db.Execute(
            """
            INSERT INTO knowledge_entries(id, section_id, title, content_format, content, sort_order, created_utc, updated_utc)
            VALUES($id, $section, $title, $format, $content, $order, $now, $now);
            """,
            tx,
            ("$id", id),
            ("$section", sectionId),
            ("$title", string.IsNullOrWhiteSpace(title) ? "Neuer Eintrag" : title.Trim()),
            ("$format", ContentFormat.Xaml),
            ("$content", initialXaml),
            ("$order", order),
            ("$now", now));
        tx.Commit();
        return id;
    }

    /// <summary>Titel, Abschnitt und Tags ändern. Ein neuer Abschnittsname legt den Abschnitt an.</summary>
    public void UpdateMetadata(string entryId, string title, string sectionName, string tags)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        var sectionId = GetOrCreateSection(db, tx, sectionName);
        db.Execute(
            "UPDATE knowledge_entries SET title = $title, section_id = $section, tags = $tags, updated_utc = $now WHERE id = $id;",
            tx,
            ("$id", entryId),
            ("$title", string.IsNullOrWhiteSpace(title) ? "Ohne Titel" : title.Trim()),
            ("$section", sectionId),
            ("$tags", tags.Trim()),
            ("$now", time.GetUtcNow().ToDb()));
        tx.Commit();
    }

    public void SetFlags(string entryId, bool favorite, bool isProtected, bool archived)
    {
        using var db = database.Open();
        db.Execute(
            "UPDATE knowledge_entries SET is_favorite = $f, is_protected = $p, is_archived = $a WHERE id = $id;",
            null,
            ("$id", entryId),
            ("$f", favorite.ToDb()),
            ("$p", isProtected.ToDb()),
            ("$a", archived.ToDb()));
    }

    /// <summary>
    /// Speichert den Inhalt aus dem Editor. Danach ist der Eintrag im XAML-Format; das alte RTF-Dokument bleibt als
    /// Datei in der Ablage erhalten. Bilder behalten ihren OCR-Text, wenn sie vorher schon zum Eintrag gehörten.
    /// </summary>
    public void SaveContent(string entryId, KnowledgeContentUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);

        var previous = new Dictionary<string, (string Ocr, string? Legacy)>(StringComparer.Ordinal);
        using (var command = SqliteExtensions.Create(
            db, "SELECT asset_id, ocr_text, legacy_path FROM knowledge_entry_images WHERE entry_id = $id;", tx, ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                previous.TryAdd(reader.GetText("asset_id"), (reader.GetText("ocr_text"), NullIfEmpty(reader.GetText("legacy_path"))));
            }
        }

        db.Execute(
            """
            UPDATE knowledge_entries
            SET content_format = $format, content = $content, content_asset_id = NULL, plain_text = $plain, updated_utc = $now
            WHERE id = $id;
            """,
            tx,
            ("$id", entryId),
            ("$format", ContentFormat.Xaml),
            ("$content", update.Xaml),
            ("$plain", update.PlainText),
            ("$now", time.GetUtcNow().ToDb()));

        db.Execute("DELETE FROM knowledge_entry_images WHERE entry_id = $id;", tx, ("$id", entryId));
        for (var i = 0; i < update.ImageAssetIds.Count; i++)
        {
            var assetId = update.ImageAssetIds[i];
            previous.TryGetValue(assetId, out var old);
            db.Execute(
                """
                INSERT INTO knowledge_entry_images(entry_id, ordinal, asset_id, is_primary, legacy_path, ocr_text)
                VALUES($entry, $ordinal, $asset, $primary, $legacy, $ocr);
                """,
                tx,
                ("$entry", entryId),
                ("$ordinal", i),
                ("$asset", assetId),
                ("$primary", (i == 0).ToDb()),
                ("$legacy", old.Legacy),
                ("$ocr", old.Ocr ?? string.Empty));
        }

        tx.Commit();
    }

    /// <summary>Bild aus dem Editor (PNG/JPEG) in der Ablage speichern; liefert die Asset-Id.</summary>
    public string StoreImage(byte[] data, string extension)
    {
        using var db = database.Open();
        return assets.ImportBytes(data, extension, db, null, time.GetUtcNow()).Id;
    }

    /// <summary>Bilder des Eintrags ohne OCR-Text (z. B. neu eingefügt).</summary>
    public IReadOnlyList<KnowledgeImageRef> ImagesWithoutOcr(string entryId) =>
        LoadContent(entryId)?.Images.Where(i => i.OcrText.Length == 0 && i.LegacyPath is null).ToList() ?? [];

    /// <summary>OCR-Text eines Bildes speichern und an den durchsuchbaren OCR-Text des Eintrags anhängen.</summary>
    public void AddImageOcr(string entryId, string assetId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var changed = db.Execute(
            "UPDATE knowledge_entry_images SET ocr_text = $text WHERE entry_id = $entry AND asset_id = $asset AND ocr_text = '';",
            tx,
            ("$entry", entryId),
            ("$asset", assetId),
            ("$text", text.Trim()));
        if (changed > 0)
        {
            db.Execute(
                "UPDATE knowledge_entries SET ocr_text = TRIM(ocr_text || ' ' || $text) WHERE id = $entry;",
                tx,
                ("$entry", entryId),
                ("$text", text.Trim()));
        }

        tx.Commit();
    }

    // ------------------------------------------------------------------ Copy-Actions

    public IReadOnlyList<CopyAction> LoadCopyActions(string entryId)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT text, occurrence FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id ORDER BY ordinal;", null, ("$id", entryId));
        using var reader = command.ExecuteReader();
        var result = new List<CopyAction>();
        while (reader.Read())
        {
            result.Add(new CopyAction(reader.GetText("text"), (int)reader.GetInteger("occurrence")));
        }

        return result;
    }

    /// <summary>Neuen Kopier-Button anlegen. Derselbe Text wird wie in PixFix 69 nur einmal angelegt.</summary>
    public bool AddCopyAction(string entryId, CopyAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Text))
        {
            return false;
        }

        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        var exists = Convert.ToInt64(
            db.Scalar("SELECT COUNT(*) FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id AND text = $text;", tx, ("$id", entryId), ("$text", action.Text)),
            CultureInfo.InvariantCulture) > 0;
        if (exists)
        {
            return false;
        }

        var ordinal = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(ordinal), -1) + 1 FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id;", tx, ("$id", entryId)),
            CultureInfo.InvariantCulture);
        db.Execute(
            "INSERT INTO copy_actions(owner_kind, owner_id, ordinal, text, occurrence) VALUES('knowledge', $id, $ordinal, $text, $occ);",
            tx,
            ("$id", entryId),
            ("$ordinal", ordinal),
            ("$text", action.Text),
            ("$occ", Math.Max(0, action.Occurrence)));
        tx.Commit();
        return true;
    }

    public void DeleteCopyAction(string entryId, string text)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        db.Execute("DELETE FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id AND text = $text;", tx, ("$id", entryId), ("$text", text));
        tx.Commit();
    }

    // ------------------------------------------------------------------ Abschnitte und Reihenfolge

    public IReadOnlyList<KnowledgeSectionInfo> LoadSections()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            """
            SELECT s.id, s.name, s.sort_order, COUNT(e.id) AS entry_count
            FROM knowledge_sections s LEFT JOIN knowledge_entries e ON e.section_id = s.id
            GROUP BY s.id, s.name, s.sort_order
            ORDER BY s.sort_order, s.name;
            """,
            null);
        using var reader = command.ExecuteReader();
        var result = new List<KnowledgeSectionInfo>();
        while (reader.Read())
        {
            result.Add(new KnowledgeSectionInfo(
                reader.GetText("id"), reader.GetText("name"), (int)reader.GetInteger("sort_order"), (int)reader.GetInteger("entry_count")));
        }

        return result;
    }

    public void RenameSection(string sectionId, string newName)
    {
        var name = newName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Der Abschnittsname darf nicht leer sein.");
        }

        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var clash = db.Scalar("SELECT id FROM knowledge_sections WHERE name = $name AND id <> $id;", tx, ("$name", name), ("$id", sectionId));
        if (clash is string)
        {
            throw new InvalidOperationException($"Es gibt bereits einen Abschnitt „{name}“.");
        }

        db.Execute("UPDATE knowledge_sections SET name = $name WHERE id = $id;", tx, ("$name", name), ("$id", sectionId));
        tx.Commit();
    }

    /// <summary>Abschnitt um eine Position nach oben (-1) oder unten (+1) verschieben.</summary>
    public void MoveSection(string sectionId, int direction)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var ids = ReadIds(db, tx, "SELECT id FROM knowledge_sections ORDER BY sort_order, name;");
        if (Swap(ids, sectionId, direction))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                db.Execute("UPDATE knowledge_sections SET sort_order = $o WHERE id = $id;", tx, ("$o", i), ("$id", ids[i]));
            }
        }

        tx.Commit();
    }

    /// <summary>Löscht einen leeren Abschnitt. Abschnitte mit Einträgen werden nicht gelöscht.</summary>
    public void DeleteSection(string sectionId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var count = Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM knowledge_entries WHERE section_id = $id;", tx, ("$id", sectionId)), CultureInfo.InvariantCulture);
        if (count > 0)
        {
            throw new InvalidOperationException("Der Abschnitt enthält noch Einträge. Bitte zuerst verschieben oder löschen.");
        }

        db.Execute("DELETE FROM knowledge_sections WHERE id = $id;", tx, ("$id", sectionId));
        tx.Commit();
    }

    /// <summary>Eintrag innerhalb seines Abschnitts nach oben (-1) oder unten (+1) verschieben.</summary>
    public void MoveEntry(string entryId, int direction)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        if (db.Scalar("SELECT section_id FROM knowledge_entries WHERE id = $id;", tx, ("$id", entryId)) is not string sectionId)
        {
            return;
        }

        var ids = ReadIds(db, tx, "SELECT id FROM knowledge_entries WHERE section_id = $s ORDER BY sort_order, title;", ("$s", sectionId));
        if (Swap(ids, entryId, direction))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                db.Execute("UPDATE knowledge_entries SET sort_order = $o WHERE id = $id;", tx, ("$o", i), ("$id", ids[i]));
            }
        }

        tx.Commit();
    }

    private static List<string> ReadIds(SqliteConnection db, SqliteTransaction tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = SqliteExtensions.Create(db, sql, tx, parameters);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetText("id"));
        }

        return ids;
    }

    private static bool Swap(List<string> ids, string id, int direction)
    {
        var index = ids.IndexOf(id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return false;
        }

        (ids[index], ids[target]) = (ids[target], ids[index]);
        return true;
    }

    // ------------------------------------------------------------------ Papierkorb

    /// <summary>Verschiebt einen Eintrag in den Papierkorb. Dateien in der Ablage bleiben erhalten.</summary>
    public void MoveToTrash(string entryId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);

        var snapshot = ReadSnapshot(db, tx, entryId) ?? throw new InvalidOperationException("Eintrag nicht gefunden.");
        db.Execute(
            "INSERT INTO trash(id, kind, original_id, deleted_utc, snapshot_json) VALUES($id, $kind, $original, $now, $json);",
            tx,
            ("$id", Guid.NewGuid().ToString("N")),
            ("$kind", TrashKind),
            ("$original", entryId),
            ("$now", time.GetUtcNow().ToDb()),
            ("$json", JsonSerializer.Serialize(snapshot)));
        db.Execute("DELETE FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id;", tx, ("$id", entryId));
        db.Execute("DELETE FROM knowledge_entries WHERE id = $id;", tx, ("$id", entryId));
        tx.Commit();
    }

    public IReadOnlyList<KnowledgeTrashItem> LoadTrash()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT id, deleted_utc, snapshot_json FROM trash WHERE kind = $kind ORDER BY deleted_utc DESC;", null, ("$kind", TrashKind));
        using var reader = command.ExecuteReader();
        var result = new List<KnowledgeTrashItem>();
        while (reader.Read())
        {
            var snapshot = ParseSnapshot(reader.GetText("snapshot_json"));
            result.Add(new KnowledgeTrashItem(reader.GetText("id"), snapshot.Title, snapshot.SectionName, ParseTime(reader.GetText("deleted_utc"))));
        }

        return result;
    }

    /// <summary>Stellt einen Eintrag wieder her und gibt seine Id zurück.</summary>
    public string Restore(string trashId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var json = db.Scalar("SELECT snapshot_json FROM trash WHERE id = $id AND kind = $kind;", tx, ("$id", trashId), ("$kind", TrashKind)) as string
            ?? throw new InvalidOperationException("Papierkorb-Eintrag nicht gefunden.");
        var s = ParseSnapshot(json);

        // Wurde die Id inzwischen wieder vergeben, bekommt der wiederhergestellte Eintrag eine neue.
        var exists = Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM knowledge_entries WHERE id = $id;", tx, ("$id", s.Id)), CultureInfo.InvariantCulture) > 0;
        var id = string.IsNullOrEmpty(s.Id) || exists ? Guid.NewGuid().ToString("N") : s.Id;
        var sectionId = GetOrCreateSection(db, tx, s.SectionName);
        var order = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM knowledge_entries WHERE section_id = $s;", tx, ("$s", sectionId)),
            CultureInfo.InvariantCulture);
        var now = time.GetUtcNow().ToDb();

        db.Execute(
            """
            INSERT INTO knowledge_entries(id, section_id, title, tags, ocr_text, plain_text, content_format, content, content_asset_id,
                sort_order, is_favorite, is_protected, is_archived, created_utc, updated_utc)
            VALUES($id, $section, $title, $tags, $ocr, $plain, $format, $content, $asset, $order, $fav, 0, $arch, $created, $now);
            """,
            tx,
            ("$id", id),
            ("$section", sectionId),
            ("$title", s.Title),
            ("$tags", s.Tags),
            ("$ocr", s.OcrText),
            ("$plain", s.PlainText),
            ("$format", s.ContentFormat),
            ("$content", s.Content),
            ("$asset", ExistingAsset(db, tx, s.ContentAssetId)),
            ("$order", order),
            ("$fav", s.IsFavorite.ToDb()),
            ("$arch", s.IsArchived.ToDb()),
            ("$created", string.IsNullOrEmpty(s.CreatedUtc) ? now : s.CreatedUtc),
            ("$now", now));

        var ordinal = 0;
        foreach (var image in s.Images)
        {
            if (ExistingAsset(db, tx, image.AssetId) is null)
            {
                continue;
            }

            db.Execute(
                """
                INSERT INTO knowledge_entry_images(entry_id, ordinal, asset_id, is_primary, legacy_path, ocr_text)
                VALUES($entry, $ordinal, $asset, $primary, $legacy, $ocr);
                """,
                tx,
                ("$entry", id),
                ("$ordinal", ordinal),
                ("$asset", image.AssetId),
                ("$primary", (ordinal == 0).ToDb()),
                ("$legacy", image.LegacyPath),
                ("$ocr", image.OcrText));
            ordinal++;
        }

        for (var i = 0; i < s.CopyActions.Count; i++)
        {
            db.Execute(
                "INSERT INTO copy_actions(owner_kind, owner_id, ordinal, text, occurrence) VALUES('knowledge', $id, $ordinal, $text, $occ);",
                tx,
                ("$id", id),
                ("$ordinal", i),
                ("$text", s.CopyActions[i].Text),
                ("$occ", s.CopyActions[i].Occurrence));
        }

        db.Execute("DELETE FROM trash WHERE id = $id;", tx, ("$id", trashId));
        tx.Commit();
        return id;
    }

    /// <summary>Endgültig löschen. Bilddateien bleiben in der Ablage, weil sie von anderen Einträgen genutzt werden können.</summary>
    public void DeletePermanently(string trashId)
    {
        using var db = database.Open();
        db.Execute("DELETE FROM trash WHERE id = $id AND kind = $kind;", null, ("$id", trashId), ("$kind", TrashKind));
    }

    // ------------------------------------------------------------------ Hilfsfunktionen

    private sealed record SnapshotImage(string AssetId, string? LegacyPath, string OcrText);

    private sealed record Snapshot(
        string Format,
        string Id,
        string Title,
        string SectionName,
        string Tags,
        string OcrText,
        string PlainText,
        string ContentFormat,
        string? Content,
        string? ContentAssetId,
        bool IsFavorite,
        bool IsArchived,
        string CreatedUtc,
        List<SnapshotImage> Images,
        List<CopyAction> CopyActions);

    private static Snapshot? ReadSnapshot(SqliteConnection db, SqliteTransaction tx, string entryId)
    {
        Snapshot? snapshot = null;
        using (var command = SqliteExtensions.Create(
            db,
            """
            SELECT e.*, s.name AS section_name FROM knowledge_entries e JOIN knowledge_sections s ON s.id = e.section_id WHERE e.id = $id;
            """,
            tx,
            ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            snapshot = new Snapshot(
                SnapshotFormat,
                entryId,
                reader.GetText("title"),
                reader.GetText("section_name"),
                reader.GetText("tags"),
                reader.GetText("ocr_text"),
                reader.GetText("plain_text"),
                reader.GetText("content_format"),
                NullIfEmpty(reader.GetText("content")),
                NullIfEmpty(reader.GetText("content_asset_id")),
                reader.GetFlag("is_favorite"),
                reader.GetFlag("is_archived"),
                reader.GetText("created_utc"),
                [],
                []);
        }

        using (var command = SqliteExtensions.Create(
            db, "SELECT asset_id, legacy_path, ocr_text FROM knowledge_entry_images WHERE entry_id = $id ORDER BY ordinal;", tx, ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                snapshot.Images.Add(new SnapshotImage(reader.GetText("asset_id"), NullIfEmpty(reader.GetText("legacy_path")), reader.GetText("ocr_text")));
            }
        }

        using (var command = SqliteExtensions.Create(
            db, "SELECT text, occurrence FROM copy_actions WHERE owner_kind = 'knowledge' AND owner_id = $id ORDER BY ordinal;", tx, ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                snapshot.CopyActions.Add(new CopyAction(reader.GetText("text"), (int)reader.GetInteger("occurrence")));
            }
        }

        return snapshot;
    }

    /// <summary>Liest neue Snapshots und die aus PixFix 69 übernommenen (Feld/Wert-Liste der alten FixEntry-Klasse).</summary>
    private static Snapshot ParseSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.TryGetProperty("Format", out var f) ? f.GetString() ?? string.Empty : string.Empty;
        if (format == SnapshotFormat)
        {
            return JsonSerializer.Deserialize<Snapshot>(json) ?? throw new JsonException("Leerer Snapshot.");
        }

        // PixFix-69-Snapshot: { "Format": "pixfix69-FixEntry", "Values": { "Title": …, "Category": …, … } }
        var values = root.TryGetProperty("Values", out var v)
            ? v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        string Value(string key) => values.TryGetValue(key, out var value) ? value : string.Empty;
        return new Snapshot(
            format,
            Value("Id"),
            Value("Title").Length > 0 ? Value("Title") : "Ohne Titel",
            Value("Category").Length > 0 ? Value("Category") : DefaultSection,
            Value("Tags"),
            Value("Ocr"),
            Value("Solution"),
            Core.Common.ContentFormat.PlainText,
            Value("Solution"),
            null,
            string.Equals(Value("Favorite"), "true", StringComparison.OrdinalIgnoreCase),
            false,
            string.Empty,
            [],
            []);
    }

    private string GetOrCreateSection(SqliteConnection db, SqliteTransaction tx, string sectionName)
    {
        var name = string.IsNullOrWhiteSpace(sectionName) ? DefaultSection : sectionName.Trim();
        if (db.Scalar("SELECT id FROM knowledge_sections WHERE name = $name;", tx, ("$name", name)) is string id)
        {
            return id;
        }

        id = Guid.NewGuid().ToString("N");
        var order = Convert.ToInt32(db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM knowledge_sections;", tx), CultureInfo.InvariantCulture);
        db.Execute("INSERT INTO knowledge_sections(id, name, sort_order) VALUES($id, $name, $order);", tx, ("$id", id), ("$name", name), ("$order", order));
        return id;
    }

    private static void EnsureNotProtected(SqliteConnection db, SqliteTransaction tx, string entryId)
    {
        var flag = db.Scalar("SELECT is_protected FROM knowledge_entries WHERE id = $id;", tx, ("$id", entryId));
        if (flag is null || flag is DBNull)
        {
            throw new InvalidOperationException("Eintrag nicht gefunden.");
        }

        if (Convert.ToInt64(flag, CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("Der Eintrag ist geschützt. Bitte zuerst den Schutz aufheben.");
        }
    }

    private static string? ExistingAsset(SqliteConnection db, SqliteTransaction tx, string? assetId) =>
        assetId is not null && db.Scalar("SELECT id FROM assets WHERE id = $id;", tx, ("$id", assetId)) is string id ? id : null;

    private static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
