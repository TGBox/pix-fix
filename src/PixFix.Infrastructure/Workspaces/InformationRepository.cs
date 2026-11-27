using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixFix.Core.Common;
using PixFix.Core.Documents;
using PixFix.Core.Workspaces;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Documents;

namespace PixFix.Infrastructure.Workspaces;

/// <summary>Reiter eines Schreibbereichs ohne Inhalt – für die Liste.</summary>
public sealed record InformationListItem(
    string Id, string WorkspaceId, string Section, int SectionOrder, bool SectionCollapsed, int SortOrder, string Title, string PlainText, bool IsProtected);

/// <summary>Inhalt eines Reiters zur Anzeige: <c>xaml</c>, <c>rtf</c> (aus PixFix 69, Bilder aufbereitet) oder <c>text</c>.</summary>
public sealed record InformationDocument(string Format, string Text);

/// <summary>PDF-Anhang mit Datei in der Ablage.</summary>
public sealed record InformationAttachmentFile(int Ordinal, string DisplayName, string File, DateTimeOffset AddedUtc);

/// <summary>Zugriff auf Schreibbereiche, Abschnitte und Reiter (SPEC F3).</summary>
public sealed class InformationRepository(PixFixDatabase database, AssetStore assets, TimeProvider? timeProvider = null)
{
    private const string OwnerKind = "information";
    private const string TrashKind = "information";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    // ------------------------------------------------------------------ Bereiche

    public IReadOnlyList<Workspace> LoadWorkspaces()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT * FROM workspaces ORDER BY sort_order, title;", null);
        using var reader = command.ExecuteReader();
        var result = new List<Workspace>();
        while (reader.Read())
        {
            result.Add(new Workspace(
                reader.GetText("id"), reader.GetText("title"), reader.GetText("icon"), (int)reader.GetInteger("sort_order"), reader.GetFlag("is_protected")));
        }

        return result;
    }

    public string CreateWorkspace(string title, string icon)
    {
        var id = Guid.NewGuid().ToString("N");
        using var db = database.Open();
        var order = Convert.ToInt32(db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM workspaces;"), CultureInfo.InvariantCulture);
        db.Execute(
            "INSERT INTO workspaces(id, title, icon, sort_order) VALUES($id, $title, $icon, $order);",
            null,
            ("$id", id),
            ("$title", RequireText(title, "Der Name des Bereichs")),
            ("$icon", icon.Trim()),
            ("$order", order));
        return id;
    }

    public void UpdateWorkspace(string workspaceId, string title, string icon, bool isProtected)
    {
        using var db = database.Open();
        db.Execute(
            "UPDATE workspaces SET title = $title, icon = $icon, is_protected = $p WHERE id = $id;",
            null,
            ("$id", workspaceId),
            ("$title", RequireText(title, "Der Name des Bereichs")),
            ("$icon", icon.Trim()),
            ("$p", isProtected.ToDb()));
    }

    public void MoveWorkspace(string workspaceId, int direction) =>
        Reorder("SELECT id FROM workspaces ORDER BY sort_order, title;", "UPDATE workspaces SET sort_order = $o WHERE id = $id;", workspaceId, direction);

    /// <summary>Nur leere Bereiche können gelöscht werden.</summary>
    public void DeleteWorkspace(string workspaceId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        if (Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM information_entries WHERE workspace_id = $id;", tx, ("$id", workspaceId)), CultureInfo.InvariantCulture) > 0)
        {
            throw new InvalidOperationException("Der Bereich enthält noch Reiter. Bitte zuerst löschen.");
        }

        if (Convert.ToInt64(db.Scalar("SELECT is_protected FROM workspaces WHERE id = $id;", tx, ("$id", workspaceId)), CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("Der Bereich ist geschützt.");
        }

        db.Execute("DELETE FROM workspaces WHERE id = $id;", tx, ("$id", workspaceId));
        tx.Commit();
    }

    // ------------------------------------------------------------------ Reiter

    public IReadOnlyList<InformationListItem> LoadEntries(string workspaceId)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            """
            SELECT id, workspace_id, section, section_order, section_collapsed, sort_order, title, plain_text, is_protected
            FROM information_entries WHERE workspace_id = $w
            ORDER BY section_order, section, sort_order, title;
            """,
            null,
            ("$w", workspaceId));
        using var reader = command.ExecuteReader();
        var result = new List<InformationListItem>();
        while (reader.Read())
        {
            result.Add(new InformationListItem(
                reader.GetText("id"),
                reader.GetText("workspace_id"),
                reader.GetText("section"),
                (int)reader.GetInteger("section_order"),
                reader.GetFlag("section_collapsed"),
                (int)reader.GetInteger("sort_order"),
                reader.GetText("title"),
                reader.GetText("plain_text"),
                reader.GetFlag("is_protected")));
        }

        return result;
    }

    /// <summary>Inhalt zur Anzeige. Altes RTF aus PixFix 69 bekommt PNG statt Metafile-Bildern (wie in der Wissensdatenbank).</summary>
    public InformationDocument? LoadDocument(string entryId)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT content_format, content, plain_text FROM information_entries WHERE id = $id;", null, ("$id", entryId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var format = reader.GetText("content_format");
        var content = reader.GetText("content");
        return format switch
        {
            ContentFormat.Xaml => new InformationDocument(ContentFormat.Xaml, content),
            ContentFormat.Rtf => new InformationDocument(
                ContentFormat.Rtf, RtfPictureRewriter.Rewrite(content, picture => LegacyPictureConverter.Convert(picture, _ => null))),
            _ => new InformationDocument(ContentFormat.PlainText, content.Length > 0 ? content : reader.GetText("plain_text")),
        };
    }

    /// <summary>Neuer Reiter am Ende des Abschnitts; ein neuer Abschnitt kommt ans Ende des Bereichs.</summary>
    public string CreateEntry(string workspaceId, string section, string title)
    {
        var sectionName = RequireText(section, "Der Abschnitt");
        var id = Guid.NewGuid().ToString("N");
        var now = time.GetUtcNow().ToDb();
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var sectionOrder = SectionOrder(db, tx, workspaceId, sectionName);
        var order = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM information_entries WHERE workspace_id = $w AND section = $s;", tx, ("$w", workspaceId), ("$s", sectionName)),
            CultureInfo.InvariantCulture);
        db.Execute(
            """
            INSERT INTO information_entries(id, workspace_id, section, section_order, sort_order, title, content_format, content, created_utc, updated_utc)
            VALUES($id, $w, $s, $so, $o, $title, $format, '', $now, $now);
            """,
            tx,
            ("$id", id),
            ("$w", workspaceId),
            ("$s", sectionName),
            ("$so", sectionOrder),
            ("$o", order),
            ("$title", string.IsNullOrWhiteSpace(title) ? "Neuer Reiter" : title.Trim()),
            ("$format", ContentFormat.Xaml),
            ("$now", now));
        tx.Commit();
        return id;
    }

    public void UpdateEntry(string entryId, string title, string section)
    {
        var sectionName = RequireText(section, "Der Abschnitt");
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        var workspaceId = (string)db.Scalar("SELECT workspace_id FROM information_entries WHERE id = $id;", tx, ("$id", entryId))!;
        var sectionOrder = SectionOrder(db, tx, workspaceId, sectionName);
        db.Execute(
            "UPDATE information_entries SET title = $title, section = $s, section_order = $so, updated_utc = $now WHERE id = $id;",
            tx,
            ("$id", entryId),
            ("$title", string.IsNullOrWhiteSpace(title) ? "Ohne Titel" : title.Trim()),
            ("$s", sectionName),
            ("$so", sectionOrder),
            ("$now", time.GetUtcNow().ToDb()));
        tx.Commit();
    }

    public void SetProtected(string entryId, bool isProtected)
    {
        using var db = database.Open();
        db.Execute("UPDATE information_entries SET is_protected = $p WHERE id = $id;", null, ("$id", entryId), ("$p", isProtected.ToDb()));
    }

    /// <summary>Eingeklappt-Zustand gilt für den ganzen Abschnitt (alle Reiter darin).</summary>
    public void SetSectionCollapsed(string workspaceId, string section, bool collapsed)
    {
        using var db = database.Open();
        db.Execute(
            "UPDATE information_entries SET section_collapsed = $c WHERE workspace_id = $w AND section = $s;",
            null,
            ("$w", workspaceId),
            ("$s", section),
            ("$c", collapsed.ToDb()));
    }

    public void MoveEntry(string entryId, int direction)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT workspace_id, section FROM information_entries WHERE id = $id;", null, ("$id", entryId));
        string workspaceId, section;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return;
            }

            workspaceId = reader.GetText("workspace_id");
            section = reader.GetText("section");
        }

        Reorder(
            "SELECT id FROM information_entries WHERE workspace_id = $w AND section = $s ORDER BY sort_order, title;",
            "UPDATE information_entries SET sort_order = $o WHERE id = $id;",
            entryId,
            direction,
            ("$w", workspaceId),
            ("$s", section));
    }

    /// <summary>Abschnitt im Bereich nach oben/unten verschieben.</summary>
    public void MoveSection(string workspaceId, string section, int direction)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var sections = new List<string>();
        using (var command = SqliteExtensions.Create(
            db,
            "SELECT section FROM information_entries WHERE workspace_id = $w GROUP BY section ORDER BY MIN(section_order), section;",
            tx,
            ("$w", workspaceId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sections.Add(reader.GetText("section"));
            }
        }

        var index = sections.IndexOf(section);
        var target = index + Math.Sign(direction);
        if (index >= 0 && target >= 0 && target < sections.Count)
        {
            (sections[index], sections[target]) = (sections[target], sections[index]);
            for (var i = 0; i < sections.Count; i++)
            {
                db.Execute(
                    "UPDATE information_entries SET section_order = $o WHERE workspace_id = $w AND section = $s;",
                    tx,
                    ("$o", i),
                    ("$w", workspaceId),
                    ("$s", sections[i]));
            }
        }

        tx.Commit();
    }

    /// <summary>Löscht einen Reiter. Zur Sicherheit wird ein Abbild im Papierkorb abgelegt; Dateien bleiben in der Ablage.</summary>
    public void DeleteEntry(string entryId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        using (var command = SqliteExtensions.Create(db, "SELECT * FROM information_entries WHERE id = $id;", tx, ("$id", entryId)))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
            }
        }

        values["copy_actions"] = LoadCopyActions(db, tx, entryId);
        values["attachments"] = LoadAttachmentRows(db, tx, entryId);
        db.Execute(
            "INSERT INTO trash(id, kind, original_id, deleted_utc, snapshot_json) VALUES($id, $kind, $original, $now, $json);",
            tx,
            ("$id", Guid.NewGuid().ToString("N")),
            ("$kind", TrashKind),
            ("$original", entryId),
            ("$now", time.GetUtcNow().ToDb()),
            ("$json", JsonSerializer.Serialize(new { Format = "pixfix-information-1", Values = values })));
        db.Execute("DELETE FROM copy_actions WHERE owner_kind = $k AND owner_id = $id;", tx, ("$k", OwnerKind), ("$id", entryId));
        db.Execute("DELETE FROM information_entries WHERE id = $id;", tx, ("$id", entryId));
        tx.Commit();
    }

    /// <summary>Speichert den Inhalt aus dem Editor (FlowDocument-XAML).</summary>
    public void SaveContent(string entryId, string xaml, string plainText, IReadOnlyList<string> imageAssetIds)
    {
        ArgumentNullException.ThrowIfNull(imageAssetIds);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        db.Execute(
            "UPDATE information_entries SET content_format = $f, content = $c, plain_text = $p, updated_utc = $now WHERE id = $id;",
            tx,
            ("$id", entryId),
            ("$f", ContentFormat.Xaml),
            ("$c", xaml),
            ("$p", plainText),
            ("$now", time.GetUtcNow().ToDb()));
        db.Execute("DELETE FROM information_entry_images WHERE entry_id = $id;", tx, ("$id", entryId));
        for (var i = 0; i < imageAssetIds.Count; i++)
        {
            db.Execute(
                "INSERT INTO information_entry_images(entry_id, ordinal, asset_id) VALUES($id, $o, $a);",
                tx,
                ("$id", entryId),
                ("$o", i),
                ("$a", imageAssetIds[i]));
        }

        tx.Commit();
    }

    public string StoreImage(byte[] data, string extension)
    {
        using var db = database.Open();
        return assets.ImportBytes(data, extension, db, null, time.GetUtcNow()).Id;
    }

    public string? GetAssetFile(string assetId)
    {
        using var db = database.Open();
        return db.Scalar("SELECT relative_path FROM assets WHERE id = $id;", null, ("$id", assetId)) is string relative
            ? assets.GetFullPath(relative)
            : null;
    }

    // ------------------------------------------------------------------ Copy-Actions

    public IReadOnlyList<CopyAction> LoadCopyActions(string entryId)
    {
        using var db = database.Open();
        return LoadCopyActions(db, null, entryId);
    }

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
        if (Convert.ToInt64(
                db.Scalar("SELECT COUNT(*) FROM copy_actions WHERE owner_kind = $k AND owner_id = $id AND text = $t;", tx, ("$k", OwnerKind), ("$id", entryId), ("$t", action.Text)),
                CultureInfo.InvariantCulture) > 0)
        {
            return false;
        }

        var ordinal = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(ordinal), -1) + 1 FROM copy_actions WHERE owner_kind = $k AND owner_id = $id;", tx, ("$k", OwnerKind), ("$id", entryId)),
            CultureInfo.InvariantCulture);
        db.Execute(
            "INSERT INTO copy_actions(owner_kind, owner_id, ordinal, text, occurrence) VALUES($k, $id, $o, $t, $occ);",
            tx,
            ("$k", OwnerKind),
            ("$id", entryId),
            ("$o", ordinal),
            ("$t", action.Text),
            ("$occ", Math.Max(0, action.Occurrence)));
        tx.Commit();
        return true;
    }

    public void DeleteCopyAction(string entryId, string text)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        db.Execute("DELETE FROM copy_actions WHERE owner_kind = $k AND owner_id = $id AND text = $t;", tx, ("$k", OwnerKind), ("$id", entryId), ("$t", text));
        tx.Commit();
    }

    // ------------------------------------------------------------------ PDF-Anhänge

    public IReadOnlyList<InformationAttachmentFile> LoadAttachments(string entryId)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            """
            SELECT i.ordinal, i.display_name, i.added_utc, a.relative_path
            FROM information_attachments i JOIN assets a ON a.id = i.asset_id
            WHERE i.entry_id = $id ORDER BY i.ordinal;
            """,
            null,
            ("$id", entryId));
        using var reader = command.ExecuteReader();
        var result = new List<InformationAttachmentFile>();
        while (reader.Read())
        {
            result.Add(new InformationAttachmentFile(
                (int)reader.GetInteger("ordinal"),
                reader.GetText("display_name"),
                assets.GetFullPath(reader.GetText("relative_path")),
                DateTimeOffset.TryParse(reader.GetText("added_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var added) ? added : DateTimeOffset.MinValue));
        }

        return result;
    }

    /// <summary>Kopiert die Datei in die Ablage und hängt sie an den Reiter.</summary>
    public void AddAttachment(string entryId, string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var now = time.GetUtcNow();
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        var asset = assets.Import(file, db, tx, now);
        var ordinal = Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(ordinal), -1) + 1 FROM information_attachments WHERE entry_id = $id;", tx, ("$id", entryId)),
            CultureInfo.InvariantCulture);
        db.Execute(
            "INSERT INTO information_attachments(entry_id, ordinal, asset_id, display_name, added_utc) VALUES($id, $o, $a, $n, $now);",
            tx,
            ("$id", entryId),
            ("$o", ordinal),
            ("$a", asset.Id),
            ("$n", Path.GetFileName(file)),
            ("$now", now.ToDb()));
        tx.Commit();
    }

    /// <summary>Entfernt den Anhang vom Reiter; die Datei bleibt in der Ablage.</summary>
    public void RemoveAttachment(string entryId, int ordinal)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        EnsureNotProtected(db, tx, entryId);
        db.Execute("DELETE FROM information_attachments WHERE entry_id = $id AND ordinal = $o;", tx, ("$id", entryId), ("$o", ordinal));
        tx.Commit();
    }

    // ------------------------------------------------------------------ Hilfsfunktionen

    private static List<CopyAction> LoadCopyActions(SqliteConnection db, SqliteTransaction? tx, string entryId)
    {
        using var command = SqliteExtensions.Create(
            db, "SELECT text, occurrence FROM copy_actions WHERE owner_kind = $k AND owner_id = $id ORDER BY ordinal;", tx, ("$k", OwnerKind), ("$id", entryId));
        using var reader = command.ExecuteReader();
        var result = new List<CopyAction>();
        while (reader.Read())
        {
            result.Add(new CopyAction(reader.GetText("text"), (int)reader.GetInteger("occurrence")));
        }

        return result;
    }

    private static List<Dictionary<string, object?>> LoadAttachmentRows(SqliteConnection db, SqliteTransaction tx, string entryId)
    {
        using var command = SqliteExtensions.Create(db, "SELECT * FROM information_attachments WHERE entry_id = $id ORDER BY ordinal;", tx, ("$id", entryId));
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Bestehende Reihenfolge eines Abschnitts oder eine neue Position am Ende des Bereichs.</summary>
    private static int SectionOrder(SqliteConnection db, SqliteTransaction tx, string workspaceId, string section)
    {
        var existing = db.Scalar(
            "SELECT MIN(section_order) FROM information_entries WHERE workspace_id = $w AND section = $s;", tx, ("$w", workspaceId), ("$s", section));
        if (existing is not null and not DBNull)
        {
            return Convert.ToInt32(existing, CultureInfo.InvariantCulture);
        }

        return Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(section_order), -1) + 1 FROM information_entries WHERE workspace_id = $w;", tx, ("$w", workspaceId)),
            CultureInfo.InvariantCulture);
    }

    private void Reorder(string selectSql, string updateSql, string id, int direction, params (string Name, object? Value)[] parameters)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var ids = new List<string>();
        using (var command = SqliteExtensions.Create(db, selectSql, tx, parameters))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetText("id"));
            }
        }

        var index = ids.IndexOf(id);
        var target = index + Math.Sign(direction);
        if (index >= 0 && target >= 0 && target < ids.Count)
        {
            (ids[index], ids[target]) = (ids[target], ids[index]);
            for (var i = 0; i < ids.Count; i++)
            {
                db.Execute(updateSql, tx, ("$o", i), ("$id", ids[i]));
            }
        }

        tx.Commit();
    }

    private static void EnsureNotProtected(SqliteConnection db, SqliteTransaction tx, string entryId)
    {
        var flag = db.Scalar(
            """
            SELECT e.is_protected OR w.is_protected
            FROM information_entries e JOIN workspaces w ON w.id = e.workspace_id WHERE e.id = $id;
            """,
            tx,
            ("$id", entryId));
        if (flag is null or DBNull)
        {
            throw new InvalidOperationException("Reiter nicht gefunden.");
        }

        if (Convert.ToInt64(flag, CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("Der Reiter oder sein Bereich ist geschützt. Bitte zuerst den Schutz aufheben.");
        }
    }

    private static string RequireText(string value, string what)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length > 0 ? text : throw new InvalidOperationException(what + " darf nicht leer sein.");
    }
}
