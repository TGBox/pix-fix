using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Legacy;

/// <summary>
/// Liest den XML-Datenbestand von PixFix 69 vor der SQLite-Umstellung (Sicherungsformat 2.1: <c>pixfix.xml</c>,
/// <c>anrufe.xml</c>, <c>anrufe-geloescht.xml</c>, <c>papierkorb.xml</c>, <c>schreibbereiche.xml</c>, <c>informationen.xml</c>)
/// und schreibt daraus eine PixFix-69-Datenbank mit Schema 1 – Feld für Feld wie <c>PixFixStorageMigration</c> in 69.74.178.
/// Danach übernimmt der normale <see cref="LegacyImporter"/>. Nur auf einer entpackten Kopie verwenden.
/// </summary>
internal static class Legacy21Converter
{
    private static readonly string[] Schema =
    [
        "CREATE TABLE schema_info(schema_version INTEGER NOT NULL,created_utc TEXT NOT NULL,migrated_from TEXT NOT NULL,migration_id TEXT NOT NULL,program_version TEXT NOT NULL)",
        "CREATE TABLE fix_entries(id TEXT PRIMARY KEY,title TEXT,category TEXT,ocr TEXT,solution TEXT,html TEXT,legacy_rtf TEXT,native_doc TEXT,image_primary TEXT,images_legacy TEXT,tags TEXT,copy_actions TEXT,created TEXT,updated TEXT,native_format INTEGER,native_marker_version INTEGER,category_order INTEGER,page_order INTEGER,scroll_x INTEGER,scroll_y INTEGER,last_open INTEGER,favorite INTEGER,protected INTEGER,archived INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE entry_images(id INTEGER PRIMARY KEY AUTOINCREMENT,entry_id TEXT NOT NULL,ordinal INTEGER NOT NULL,relative_path TEXT NOT NULL,is_primary INTEGER NOT NULL,sha256 TEXT,file_length INTEGER,UNIQUE(entry_id,ordinal))",
        "CREATE TABLE calls(id TEXT PRIMARY KEY,customer_number TEXT,call_identifier TEXT,alternative_phone TEXT,phone1 TEXT,phone2 TEXT,phone3 TEXT,phone_private TEXT,phone_direct TEXT,mobile TEXT,email1 TEXT,email2 TEXT,email_target TEXT,fax TEXT,notes TEXT,reason TEXT,reason_rtf TEXT,work_performed TEXT,work_performed_rtf TEXT,status TEXT,received_at TEXT,billing_start TEXT,billing_end TEXT,created_at TEXT,updated_at TEXT,completed_at TEXT,reminder_at TEXT,reminder_last_shown TEXT,callback_targets TEXT,is_billable INTEGER,is_collapsed INTEGER,reminder_active INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE deleted_calls(id INTEGER PRIMARY KEY AUTOINCREMENT,source_ordinal INTEGER NOT NULL,call_id TEXT,deleted_at TEXT NOT NULL,snapshot_xml TEXT NOT NULL,source_hash TEXT NOT NULL)",
        "CREATE TABLE writing_workspaces(id TEXT PRIMARY KEY,title TEXT,icon_glyph TEXT,sort_order INTEGER,last_open INTEGER,protected INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE information_entries(id TEXT PRIMARY KEY,workspace_id TEXT,section TEXT,title TEXT,content TEXT,rtf TEXT,copy_actions TEXT,created_at TEXT,updated_at TEXT,section_order INTEGER,item_order INTEGER,cursor_position INTEGER,first_visible_line INTEGER,last_open INTEGER,section_collapsed INTEGER,protected INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE information_pdf_attachments(id TEXT NOT NULL,information_entry_id TEXT NOT NULL,name TEXT,relative_path TEXT,added_at TEXT,ordinal INTEGER NOT NULL,source_hash TEXT NOT NULL,PRIMARY KEY(information_entry_id,ordinal))",
        "CREATE TABLE deleted_fix_entries(trash_id TEXT PRIMARY KEY,entry_id TEXT,deleted_at TEXT NOT NULL,snapshot_xml TEXT NOT NULL,source_hash TEXT NOT NULL)",
    ];

    private const string SourceHash = "xml-2.1";

    /// <summary>
    /// Erzeugt <c>pixfix.db</c> im Datenordner aus den XML-Dateien. Liefert die Anzahlen mit denselben Namen wie das
    /// Sicherungsmanifest von PixFix 69 (für den Soll/Ist-Abgleich).
    /// </summary>
    public static Dictionary<string, long> CreateDatabase(string dataDirectory)
    {
        var fixes = Load(dataDirectory, "pixfix.xml");
        var deletedFixes = Load(dataDirectory, "papierkorb.xml");
        var calls = Load(dataDirectory, "anrufe.xml");
        var deletedCalls = Load(dataDirectory, "anrufe-geloescht.xml");
        var workspaces = Load(dataDirectory, "schreibbereiche.xml");
        var infos = Load(dataDirectory, "informationen.xml");
        if (fixes.Count + calls.Count + workspaces.Count + infos.Count == 0 && !File.Exists(Path.Combine(dataDirectory, "pixfix.xml")))
        {
            throw new LegacyImportException("Die Sicherung enthält keine PixFix-Daten (pixfix.xml fehlt).");
        }

        var file = Path.Combine(dataDirectory, "pixfix.db");
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        {
            db.Open();
            using var tx = db.BeginTransaction();
            foreach (var statement in Schema)
            {
                db.Execute(statement, tx);
            }

            db.Execute(
                "INSERT INTO schema_info VALUES(1, $created, 'LegacyBackup2.1', $id, '69.74.178');",
                tx,
                ("$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                ("$id", Guid.NewGuid().ToString("N")));

            foreach (var e in fixes)
            {
                InsertFix(db, tx, e, dataDirectory);
            }

            foreach (var e in calls)
            {
                InsertCall(db, tx, e);
            }

            foreach (var e in workspaces)
            {
                db.Execute(
                    "INSERT INTO writing_workspaces VALUES($id,$title,$icon,$order,$last,$protected,$hash);",
                    tx,
                    ("$id", S(e, "Id")),
                    ("$title", S(e, "Title")),
                    ("$icon", S(e, "IconGlyph")),
                    ("$order", I(e, "Order")),
                    ("$last", B(e, "LastOpen")),
                    ("$protected", B(e, "Protected")),
                    ("$hash", SourceHash));
            }

            var pdfCount = 0L;
            foreach (var e in infos)
            {
                pdfCount += InsertInformation(db, tx, e);
            }

            for (var i = 0; i < deletedCalls.Count; i++)
            {
                var entry = Snapshot(deletedCalls[i], "CallEntry");
                db.Execute(
                    "INSERT INTO deleted_calls(source_ordinal,call_id,deleted_at,snapshot_xml,source_hash) VALUES($ordinal,$id,$deleted,$xml,$hash);",
                    tx,
                    ("$ordinal", i),
                    ("$id", S(entry, "Id")),
                    ("$deleted", S(deletedCalls[i], "DeletedAt")),
                    ("$xml", entry.ToString(SaveOptions.DisableFormatting)),
                    ("$hash", SourceHash));
            }

            foreach (var item in deletedFixes)
            {
                var entry = Snapshot(item, "FixEntry");
                db.Execute(
                    "INSERT INTO deleted_fix_entries VALUES($trash,$id,$deleted,$xml,$hash);",
                    tx,
                    ("$trash", S(item, "TrashId") is { Length: > 0 } trashId ? trashId : Guid.NewGuid().ToString("N")),
                    ("$id", S(entry, "Id")),
                    ("$deleted", S(item, "DeletedAt")),
                    ("$xml", entry.ToString(SaveOptions.DisableFormatting)),
                    ("$hash", SourceHash));
            }

            tx.Commit();

            return new Dictionary<string, long>
            {
                ["Einträge"] = fixes.Count,
                ["Anrufe"] = calls.Count,
                ["Gelöschte Anrufe"] = deletedCalls.Count,
                ["Schreibbereiche"] = workspaces.Count,
                ["Reiter"] = infos.Count,
                ["PDF-Anhänge"] = pdfCount,
            };
        }
    }

    private static void InsertFix(SqliteConnection db, SqliteTransaction tx, XElement e, string dataDirectory)
    {
        db.Execute(
            "INSERT INTO fix_entries VALUES($id,$title,$category,$ocr,$solution,$html,$rtf,$native,$image,$images,$tags,$copy,$created,$updated,"
            + "$nativeFormat,$marker,$categoryOrder,$pageOrder,$scrollX,$scrollY,$last,$favorite,$protected,$archived,$hash);",
            tx,
            ("$id", S(e, "Id")),
            ("$title", S(e, "Title")),
            ("$category", S(e, "Category", "Allgemein")),
            ("$ocr", S(e, "Ocr")),
            ("$solution", S(e, "Solution")),
            ("$html", S(e, "Html")),
            ("$rtf", S(e, "Rtf")),
            ("$native", S(e, "NativeDoc")),
            ("$image", S(e, "Image")),
            ("$images", S(e, "Images")),
            ("$tags", S(e, "Tags")),
            ("$copy", S(e, "CopyActions")),
            ("$created", S(e, "Created")),
            ("$updated", S(e, "Updated")),
            ("$nativeFormat", I(e, "NativeFormat")),
            ("$marker", I(e, "NativeMarkerVersion")),
            ("$categoryOrder", I(e, "CategoryOrder")),
            ("$pageOrder", I(e, "PageOrder")),
            ("$scrollX", I(e, "ScrollX")),
            ("$scrollY", I(e, "ScrollY")),
            ("$last", B(e, "LastOpen")),
            ("$favorite", B(e, "Favorite")),
            ("$protected", B(e, "Protected")),
            ("$archived", B(e, "Archived")),
            ("$hash", SourceHash));

        // Bilder wie PixFix 69: „Images“ (mit | getrennt), sonst „Image“; Hauptbild = „Image“ bzw. das erste.
        var primary = S(e, "Image").Trim();
        var images = S(e, "Images").Length > 0
            ? S(e, "Images").Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray()
            : primary.Length > 0 ? [primary] : [];
        for (var i = 0; i < images.Length; i++)
        {
            var full = Path.Combine(dataDirectory, images[i]);
            string? sha = null;
            long? length = null;
            if (File.Exists(full))
            {
                using var stream = File.OpenRead(full);
                sha = Convert.ToHexStringLower(SHA256.HashData(stream));
                length = stream.Length;
            }

            var isPrimary = i == 0 || (primary.Length > 0 && string.Equals(images[i], primary, StringComparison.OrdinalIgnoreCase));
            db.Execute(
                "INSERT INTO entry_images(entry_id,ordinal,relative_path,is_primary,sha256,file_length) VALUES($entry,$ordinal,$path,$primary,$sha,$length);",
                tx,
                ("$entry", S(e, "Id")),
                ("$ordinal", i),
                ("$path", images[i]),
                ("$primary", isPrimary ? 1 : 0),
                ("$sha", sha),
                ("$length", length));
        }
    }

    private static void InsertCall(SqliteConnection db, SqliteTransaction tx, XElement e)
    {
        string[] text =
        [
            "Id", "CustomerNumber", "CallIdentifier", "AlternativePhone", "Phone1", "Phone2", "Phone3", "PhonePrivate", "PhoneDirect",
            "Mobile", "Email1", "Email2", "EmailTarget", "Fax", "Notes", "Reason", "ReasonRtf", "WorkPerformed", "WorkPerformedRtf",
            "Status", "ReceivedAt", "BillingStart", "BillingEnd", "CreatedAt", "UpdatedAt", "CompletedAt", "ReminderAt",
            "ReminderLastShown", "CallbackTargets",
        ];
        var parameters = text
            .Select((name, i) => ("$p" + i.ToString(CultureInfo.InvariantCulture), (object?)S(e, name, name == "Status" ? "Noch zu bearbeiten" : string.Empty)))
            .Append(("$billable", (object?)B(e, "IsBillable")))
            .Append(("$collapsed", (object?)B(e, "IsCollapsed")))
            .Append(("$reminder", (object?)B(e, "ReminderActive")))
            .Append(("$hash", (object?)SourceHash))
            .ToArray();
        var names = string.Join(",", parameters.Select(p => p.Item1));
        db.Execute("INSERT INTO calls VALUES(" + names + ");", tx, parameters);
    }

    private static long InsertInformation(SqliteConnection db, SqliteTransaction tx, XElement e)
    {
        db.Execute(
            "INSERT INTO information_entries VALUES($id,$workspace,$section,$title,$content,$rtf,$copy,$created,$updated,$sectionOrder,$order,$cursor,$line,$last,$collapsed,$protected,$hash);",
            tx,
            ("$id", S(e, "Id")),
            ("$workspace", S(e, "WorkspaceId")),
            ("$section", S(e, "Section", "Allgemein")),
            ("$title", S(e, "Title")),
            ("$content", S(e, "Content")),
            ("$rtf", S(e, "Rtf")),
            ("$copy", S(e, "CopyActions")),
            ("$created", S(e, "CreatedAt")),
            ("$updated", S(e, "UpdatedAt")),
            ("$sectionOrder", I(e, "SectionOrder")),
            ("$order", I(e, "Order")),
            ("$cursor", I(e, "CursorPosition")),
            ("$line", I(e, "FirstVisibleLine")),
            ("$last", B(e, "LastOpen")),
            ("$collapsed", B(e, "SectionCollapsed")),
            ("$protected", B(e, "Protected")),
            ("$hash", SourceHash));

        var pdfs = e.Element("PdfAttachments")?.Elements().ToList() ?? [];
        for (var i = 0; i < pdfs.Count; i++)
        {
            db.Execute(
                "INSERT INTO information_pdf_attachments VALUES($id,$entry,$name,$path,$added,$ordinal,$hash);",
                tx,
                ("$id", S(pdfs[i], "Id")),
                ("$entry", S(e, "Id")),
                ("$name", S(pdfs[i], "Name")),
                ("$path", S(pdfs[i], "RelativePath")),
                ("$added", S(pdfs[i], "AddedAt")),
                ("$ordinal", i),
                ("$hash", SourceHash));
        }

        return pdfs.Count;
    }

    /// <summary>Elemente einer XmlSerializer-Liste (<c>ArrayOfX</c>); fehlende Datei = leere Liste.</summary>
    private static List<XElement> Load(string dataDirectory, string fileName)
    {
        var file = Path.Combine(dataDirectory, fileName);
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            return XDocument.Load(file).Root?.Elements().ToList() ?? [];
        }
        catch (System.Xml.XmlException ex)
        {
            throw new LegacyImportException($"{fileName} ist beschädigt: {ex.Message}");
        }
    }

    /// <summary>Gelöschter Eintrag: das innere <c>Entry</c>-Element als eigenständiger Snapshot (wie XmlSerializer in 69.74.178).</summary>
    private static XElement Snapshot(XElement deleted, string rootName) =>
        new(rootName, deleted.Element("Entry")?.Elements() ?? []);

    private static string S(XElement e, string name, string fallback = "") =>
        e.Element(name) is { } child ? child.Value : fallback;

    private static long I(XElement e, string name) =>
        long.TryParse(e.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static int B(XElement e, string name) =>
        e.Element(name)?.Value.Trim() is "true" or "True" or "1" ? 1 : 0;
}
