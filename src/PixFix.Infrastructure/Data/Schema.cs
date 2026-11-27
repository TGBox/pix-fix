namespace PixFix.Infrastructure.Data;

/// <summary>
/// Datenbankschema als nummerierte Migrationen. Eine Migration wird nie nachträglich geändert,
/// Änderungen kommen immer als neue Migration ans Ende. Die aktuelle Version steht in <c>PRAGMA user_version</c>.
/// Zeitpunkte sind UTC im Format ISO 8601 (<c>yyyy-MM-ddTHH:mm:ss.fffZ</c>).
/// </summary>
internal static class Schema
{
    public static readonly string[] Migrations =
    [
        // 1 – Grundschema (Phase 1)
        """
        CREATE TABLE assets(
            id TEXT PRIMARY KEY,                 -- SHA-256 des Dateiinhalts
            relative_path TEXT NOT NULL UNIQUE,
            media_type TEXT NOT NULL,
            length INTEGER NOT NULL,
            original_name TEXT,
            created_utc TEXT NOT NULL);

        CREATE TABLE knowledge_sections(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            sort_order INTEGER NOT NULL);

        CREATE TABLE knowledge_entries(
            id TEXT PRIMARY KEY,
            section_id TEXT NOT NULL REFERENCES knowledge_sections(id),
            title TEXT NOT NULL,
            tags TEXT NOT NULL DEFAULT '',
            ocr_text TEXT NOT NULL DEFAULT '',
            plain_text TEXT NOT NULL DEFAULT '',
            content_format TEXT NOT NULL,
            content TEXT,
            content_asset_id TEXT REFERENCES assets(id),
            sort_order INTEGER NOT NULL,
            is_favorite INTEGER NOT NULL DEFAULT 0,
            is_protected INTEGER NOT NULL DEFAULT 0,
            is_archived INTEGER NOT NULL DEFAULT 0,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL);
        CREATE INDEX ix_knowledge_entries_section ON knowledge_entries(section_id, sort_order);

        CREATE TABLE knowledge_entry_images(
            entry_id TEXT NOT NULL REFERENCES knowledge_entries(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            asset_id TEXT NOT NULL REFERENCES assets(id),
            is_primary INTEGER NOT NULL DEFAULT 0,
            legacy_path TEXT,
            PRIMARY KEY(entry_id, ordinal));
        CREATE INDEX ix_knowledge_entry_images_asset ON knowledge_entry_images(asset_id);

        CREATE TABLE workspaces(
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            icon TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL,
            is_protected INTEGER NOT NULL DEFAULT 0);

        CREATE TABLE information_entries(
            id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES workspaces(id),
            section TEXT NOT NULL,
            section_order INTEGER NOT NULL,
            section_collapsed INTEGER NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL,
            title TEXT NOT NULL,
            plain_text TEXT NOT NULL DEFAULT '',
            content_format TEXT NOT NULL,
            content TEXT,
            is_protected INTEGER NOT NULL DEFAULT 0,
            cursor_position INTEGER NOT NULL DEFAULT 0,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL);
        CREATE INDEX ix_information_entries_workspace ON information_entries(workspace_id, section_order, sort_order);

        CREATE TABLE information_attachments(
            entry_id TEXT NOT NULL REFERENCES information_entries(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            asset_id TEXT NOT NULL REFERENCES assets(id),
            display_name TEXT NOT NULL,
            added_utc TEXT NOT NULL,
            PRIMARY KEY(entry_id, ordinal));

        CREATE TABLE copy_actions(
            owner_kind TEXT NOT NULL,            -- 'knowledge' | 'information'
            owner_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            text TEXT NOT NULL,
            occurrence INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(owner_kind, owner_id, ordinal));

        CREATE TABLE calls(
            id TEXT PRIMARY KEY,
            customer_number TEXT NOT NULL DEFAULT '',
            customer_id TEXT NOT NULL DEFAULT '',
            phone1 TEXT NOT NULL DEFAULT '',
            phone2 TEXT NOT NULL DEFAULT '',
            phone3 TEXT NOT NULL DEFAULT '',
            phone_private TEXT NOT NULL DEFAULT '',
            phone_direct TEXT NOT NULL DEFAULT '',
            mobile TEXT NOT NULL DEFAULT '',
            alternative_phone TEXT NOT NULL DEFAULT '',
            email1 TEXT NOT NULL DEFAULT '',
            email2 TEXT NOT NULL DEFAULT '',
            email_target TEXT NOT NULL DEFAULT '',
            fax TEXT NOT NULL DEFAULT '',
            notes TEXT NOT NULL DEFAULT '',
            reason_text TEXT NOT NULL DEFAULT '',
            reason_rtf TEXT NOT NULL DEFAULT '',
            solution_text TEXT NOT NULL DEFAULT '',
            solution_rtf TEXT NOT NULL DEFAULT '',
            status TEXT NOT NULL,
            callback_target TEXT NOT NULL DEFAULT '',
            is_billable INTEGER NOT NULL DEFAULT 0,
            billing_start_utc TEXT,
            billing_end_utc TEXT,
            received_utc TEXT,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            completed_utc TEXT,
            reminder_utc TEXT,
            reminder_active INTEGER NOT NULL DEFAULT 0,
            reminder_last_shown_utc TEXT,
            is_collapsed INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX ix_calls_status ON calls(status);
        CREATE INDEX ix_calls_reminder ON calls(reminder_active, reminder_utc);

        CREATE TABLE templates(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,                  -- siehe TemplateKind
            name TEXT NOT NULL,
            subject TEXT NOT NULL DEFAULT '',
            body TEXT NOT NULL DEFAULT '',
            sort_order INTEGER NOT NULL);

        CREATE TABLE cobra_quick_texts(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            cobra_text TEXT NOT NULL DEFAULT '',
            notes_text TEXT NOT NULL DEFAULT '',
            work_text TEXT NOT NULL DEFAULT '',
            complete_call INTEGER NOT NULL DEFAULT 0,
            schedule_recall INTEGER NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL);

        CREATE TABLE fax_documents(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            asset_id TEXT NOT NULL REFERENCES assets(id),
            sort_order INTEGER NOT NULL);

        CREATE TABLE trash(
            id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,                  -- 'knowledge' | 'call'
            original_id TEXT,
            deleted_utc TEXT NOT NULL,
            snapshot_json TEXT NOT NULL);

        CREATE TABLE settings(
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL);

        CREATE TABLE import_log(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source TEXT NOT NULL,
            started_utc TEXT NOT NULL,
            finished_utc TEXT NOT NULL,
            succeeded INTEGER NOT NULL,
            report TEXT NOT NULL);
        """,

        // 2 – Texterkennung (OCR) je Bild, Grundlage für die Bildsuche (Phase 2b)
        """
        ALTER TABLE knowledge_entry_images ADD COLUMN ocr_text TEXT NOT NULL DEFAULT '';
        """,

        // 3 – Bildsuch-Index (Phase 3): Signaturen je Bilddatei. Abgeleitete Daten, jederzeit neu berechenbar.
        """
        CREATE TABLE image_signatures(
            asset_id TEXT PRIMARY KEY REFERENCES assets(id) ON DELETE CASCADE,
            algorithm INTEGER NOT NULL,
            full_average INTEGER NOT NULL,
            full_difference INTEGER NOT NULL,
            full_pixels BLOB NOT NULL,
            full_histogram BLOB NOT NULL,
            full_aspect REAL NOT NULL,
            center_average INTEGER NOT NULL,
            center_difference INTEGER NOT NULL,
            center_pixels BLOB NOT NULL,
            center_histogram BLOB NOT NULL,
            center_aspect REAL NOT NULL);
        """,

        // 4 – Bilder in Schreibbereichen (Phase 4): welche Dateien ein Reiter verwendet
        """
        CREATE TABLE information_entry_images(
            entry_id TEXT NOT NULL REFERENCES information_entries(id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            asset_id TEXT NOT NULL REFERENCES assets(id),
            PRIMARY KEY(entry_id, ordinal));
        """,
    ];

    /// <summary>Tabellen mit Nutzdaten – ein Import ist nur in eine Datenbank erlaubt, in der diese leer sind.</summary>
    public static readonly string[] UserDataTables =
    [
        "knowledge_entries", "information_entries", "workspaces", "calls", "templates", "cobra_quick_texts", "trash",
    ];
}
