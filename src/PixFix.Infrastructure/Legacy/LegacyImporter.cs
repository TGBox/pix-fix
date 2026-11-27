using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using PixFix.Core.Calls;
using PixFix.Core.Common;
using PixFix.Core.Legacy;
using PixFix.Core.Templates;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Legacy;

/// <summary>
/// Übernimmt den Datenbestand aus PixFix 69 (SQLite-Speicher ab 69.74.178, <c>schema_info.schema_version = 1</c>)
/// in eine leere PixFix-Datenbank (SPEC F12).
/// <list type="bullet">
/// <item>Die Altdaten werden nie verändert: gelesen wird eine temporäre Kopie der Datenbank.</item>
/// <item>Alles läuft in einer Transaktion. Stimmt eine Soll/Ist-Anzahl nicht, wird nichts übernommen.</item>
/// <item>Fehlende Dateien (Bilder, PDFs) werden als Hinweis gemeldet und nicht als Abweichung gezählt.</item>
/// </list>
/// </summary>
public sealed class LegacyImporter
{
    public const int SupportedLegacySchemaVersion = 1;

    private const string KnowledgeOwner = "knowledge";
    private const string InformationOwner = "information";

    private readonly PixFixDatabase database;
    private readonly AssetStore assets;
    private readonly TimeZoneInfo legacyTimeZone;
    private readonly TimeProvider timeProvider;

    public LegacyImporter(PixFixDatabase database, AssetStore assets, TimeZoneInfo? legacyTimeZone = null, TimeProvider? timeProvider = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        this.legacyTimeZone = legacyTimeZone ?? TimeZoneInfo.Local;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ImportReport Import(string legacyFolder, IProgress<ImportProgress>? progress = null)
    {
        var tracker = new ImportProgressTracker(progress);
        var report = new ImportReport(legacyFolder);
        var startedUtc = timeProvider.GetUtcNow();
        string? tempDirectory = null;

        try
        {
            var layout = LegacyLayout.Resolve(legacyFolder);
            tracker.Report(0, "Datenbank wird vorbereitet …");
            database.Migrate();

            using var target = database.Open();
            if (!PixFixDatabase.HasNoUserData(target))
            {
                throw new LegacyImportException(
                    "Die PixFix-Datenbank enthält bereits Daten. Ein Import aus PixFix 69 ist nur in eine leere Datenbank möglich.");
            }

            tracker.Report(2, "PixFix-69-Datenbank wird kopiert und geprüft …");
            tempDirectory = Path.Combine(Path.GetTempPath(), "pixfix-import-" + Guid.NewGuid().ToString("N"));
            using (var source = OpenLegacyCopy(layout, tempDirectory))
            {
                CheckLegacyDatabase(source);
                RunImport(new Context(source, target, layout, report, startedUtc) { Zone = legacyTimeZone, Progress = tracker });
            }

            WriteImportLog(target, report, startedUtc);
        }
        catch (LegacyImportException ex)
        {
            report.Errors.Add(ex.Message);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException or JsonException)
        {
            report.Errors.Add("Unerwarteter Fehler: " + ex);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory, report);
        }

        tracker.Report(100, report.Succeeded ? "Import abgeschlossen." : "Import abgebrochen – es wurde nichts übernommen.");
        return report;
    }

    private void RunImport(Context ctx)
    {
        using var transaction = ctx.Target.BeginTransaction();
        ctx.Transaction = transaction;

        // Gesamtzahl der Arbeitsschritte für den Fortschrittsbalken (+1 für Vorlagen/Einstellungen).
        ctx.Progress.SetTotal(
            ctx.SourceCount("fix_entries") + ctx.SourceCount("entry_images") + ctx.SourceCount("information_entries") +
            ctx.SourceCount("information_pdf_attachments") + ctx.SourceCount("calls") +
            ctx.SourceCount("deleted_calls") + ctx.SourceCount("deleted_fix_entries") + 1);

        ImportKnowledge(ctx);
        ImportWorkspaces(ctx);
        ImportCalls(ctx);
        ImportTrash(ctx);
        ImportSettings(ctx);
        ctx.Progress.Report(99, "Soll/Ist-Abgleich und Speichern …");

        if (ctx.Report.Errors.Count > 0 || ctx.Report.Counts.Exists(c => !c.Matches))
        {
            if (!ctx.Report.Counts.TrueForAll(c => c.Matches))
            {
                ctx.Report.Errors.Add("Soll/Ist-Abgleich fehlgeschlagen – siehe Tabelle.");
            }

            transaction.Rollback();
            return;
        }

        transaction.Commit();
        ctx.Report.Committed = true;
    }

    // ---------------------------------------------------------------- Wissensdatenbank

    private void ImportKnowledge(Context ctx)
    {
        var sectionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var reader = ctx.Read(
            "SELECT COALESCE(category, '') AS category, MIN(COALESCE(category_order, 0)) AS sort_order " +
            "FROM fix_entries GROUP BY COALESCE(category, '') ORDER BY sort_order, category;"))
        {
            var order = 0;
            while (reader.Read())
            {
                var name = reader.GetText("category");
                if (name.Length == 0)
                {
                    name = "Allgemein";
                }

                if (sectionIds.ContainsKey(name))
                {
                    continue;
                }

                var id = NewId();
                sectionIds[name] = id;
                ctx.Write(
                    "INSERT INTO knowledge_sections(id, name, sort_order) VALUES($id, $name, $order);",
                    ("$id", id), ("$name", name), ("$order", order++));
            }
        }

        var copyActionsExpected = 0;
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        ctx.Progress.BeginPhase("Einträge und Dokumente werden übernommen", ctx.SourceCount("fix_entries"));
        using (var reader = ctx.Read("SELECT * FROM fix_entries ORDER BY category_order, page_order, id;"))
        {
            while (reader.Read())
            {
                ctx.Progress.Step();
                var id = reader.GetText("id");
                var title = reader.GetText("title");
                var category = reader.GetText("category");
                var (format, content, contentAssetId) = ReadKnowledgeContent(ctx, reader, title);
                var copyActions = CopyActionCodec.DecodeLegacy(reader.GetText("copy_actions"));
                copyActionsExpected += copyActions.Count;

                ctx.Write(
                    """
                    INSERT INTO knowledge_entries(id, section_id, title, tags, ocr_text, plain_text, content_format, content,
                        content_asset_id, sort_order, is_favorite, is_protected, is_archived, created_utc, updated_utc)
                    VALUES($id, $section, $title, $tags, $ocr, $plain, $format, $content,
                        $asset, $order, $favorite, $protected, $archived, $created, $updated);
                    """,
                    ("$id", id),
                    ("$section", sectionIds[category.Length == 0 ? "Allgemein" : category]),
                    ("$title", title),
                    ("$tags", reader.GetText("tags")),
                    ("$ocr", reader.GetText("ocr")),
                    ("$plain", reader.GetText("solution")),
                    ("$format", format),
                    ("$content", content),
                    ("$asset", contentAssetId),
                    ("$order", reader.GetInteger("page_order")),
                    ("$favorite", reader.GetFlag("favorite").ToDb()),
                    ("$protected", reader.GetFlag("protected").ToDb()),
                    ("$archived", reader.GetFlag("archived").ToDb()),
                    ("$created", ctx.Time(reader.GetText("created"))),
                    ("$updated", ctx.Time(reader.GetText("updated"), reader.GetText("created"))));

                WriteCopyActions(ctx, KnowledgeOwner, id, copyActions);
                entryIds.Add(id);
            }
        }

        var missingImages = 0L;
        ctx.Progress.BeginPhase("Bilder werden übernommen", ctx.SourceCount("entry_images"));
        using (var reader = ctx.Read("SELECT entry_id, ordinal, relative_path, is_primary FROM entry_images ORDER BY entry_id, ordinal;"))
        {
            while (reader.Read())
            {
                ctx.Progress.Step();
                var entryId = reader.GetText("entry_id");
                var relative = reader.GetText("relative_path");
                if (!entryIds.Contains(entryId))
                {
                    ctx.Report.Errors.Add($"Bild '{relative}' gehört zu einem unbekannten Eintrag '{entryId}'.");
                    continue;
                }

                var asset = ctx.ImportFile(assets, relative, "Bild", ctx.Layout.DataDirectory);
                if (asset is null)
                {
                    missingImages++;
                    continue;
                }

                ctx.Write(
                    """
                    INSERT INTO knowledge_entry_images(entry_id, ordinal, asset_id, is_primary, legacy_path)
                    VALUES($entry, $ordinal, $asset, $primary, $legacy);
                    """,
                    ("$entry", entryId),
                    ("$ordinal", reader.GetInteger("ordinal")),
                    ("$asset", asset.Id),
                    ("$primary", reader.GetFlag("is_primary").ToDb()),
                    ("$legacy", relative));
            }
        }

        ctx.Compare("Abschnitte (PixFix)", sectionIds.Count, "knowledge_sections");
        ctx.Compare("Einträge (PixFix)", ctx.SourceCount("fix_entries"), "knowledge_entries");
        ctx.Compare("Bilder (PixFix)", ctx.SourceCount("entry_images"), "knowledge_entry_images", missingImages);
        ctx.Compare("Copy-Actions (PixFix)", copyActionsExpected, ctx.TargetCount("copy_actions", "owner_kind = 'knowledge'"));
    }

    private (string Format, string? Content, string? AssetId) ReadKnowledgeContent(Context ctx, SqliteDataReader reader, string title)
    {
        var nativeDoc = reader.GetText("native_doc");
        if (nativeDoc.Length > 0)
        {
            var asset = ctx.ImportFile(assets, nativeDoc, $"Dokument von „{title}“", ctx.Layout.DataDirectory);
            if (asset is not null)
            {
                return (ContentFormat.Rtf, null, asset.Id);
            }
        }

        var legacyRtf = reader.GetText("legacy_rtf");
        if (legacyRtf.Length > 0)
        {
            return (ContentFormat.Rtf, legacyRtf, null);
        }

        var html = reader.GetText("html");
        if (html.Length > 0)
        {
            ctx.Report.Warnings.Add($"„{title}“: kein RTF-Dokument vorhanden, HTML-Fassung übernommen.");
            return (ContentFormat.Html, html, null);
        }

        ctx.Report.Warnings.Add($"„{title}“: weder RTF noch HTML vorhanden, nur Klartext übernommen.");
        return (ContentFormat.PlainText, reader.GetText("solution"), null);
    }

    // ---------------------------------------------------------------- Schreibbereiche

    private void ImportWorkspaces(Context ctx)
    {
        var workspaceIds = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = ctx.Read("SELECT * FROM writing_workspaces ORDER BY sort_order, id;"))
        {
            while (reader.Read())
            {
                var id = reader.GetText("id");
                workspaceIds.Add(id);
                ctx.Write(
                    "INSERT INTO workspaces(id, title, icon, sort_order, is_protected) VALUES($id, $title, $icon, $order, $protected);",
                    ("$id", id),
                    ("$title", reader.GetText("title")),
                    ("$icon", reader.GetText("icon_glyph")),
                    ("$order", reader.GetInteger("sort_order")),
                    ("$protected", reader.GetFlag("protected").ToDb()));
            }
        }

        ctx.Compare("Schreibbereiche", ctx.SourceCount("writing_workspaces"), "workspaces");

        var copyActionsExpected = 0;
        ctx.Progress.BeginPhase("Schreibbereiche werden übernommen", ctx.SourceCount("information_entries"));
        using (var reader = ctx.Read("SELECT * FROM information_entries ORDER BY workspace_id, section_order, item_order, id;"))
        {
            while (reader.Read())
            {
                ctx.Progress.Step();
                var id = reader.GetText("id");
                var workspaceId = reader.GetText("workspace_id");
                if (!workspaceIds.Contains(workspaceId))
                {
                    ctx.Report.Errors.Add($"Reiter „{reader.GetText("title")}“ gehört zu einem unbekannten Schreibbereich '{workspaceId}'.");
                    continue;
                }

                var rtf = reader.GetText("rtf");
                var plain = reader.GetText("content");
                var copyActions = CopyActionCodec.DecodeLegacy(reader.GetText("copy_actions"));
                copyActionsExpected += copyActions.Count;

                ctx.Write(
                    """
                    INSERT INTO information_entries(id, workspace_id, section, section_order, section_collapsed, sort_order, title,
                        plain_text, content_format, content, is_protected, cursor_position, created_utc, updated_utc)
                    VALUES($id, $workspace, $section, $sectionOrder, $collapsed, $order, $title,
                        $plain, $format, $content, $protected, $cursor, $created, $updated);
                    """,
                    ("$id", id),
                    ("$workspace", workspaceId),
                    ("$section", reader.GetText("section")),
                    ("$sectionOrder", reader.GetInteger("section_order")),
                    ("$collapsed", reader.GetFlag("section_collapsed").ToDb()),
                    ("$order", reader.GetInteger("item_order")),
                    ("$title", reader.GetText("title")),
                    ("$plain", plain),
                    ("$format", rtf.Length > 0 ? ContentFormat.Rtf : ContentFormat.PlainText),
                    ("$content", rtf.Length > 0 ? rtf : plain),
                    ("$protected", reader.GetFlag("protected").ToDb()),
                    ("$cursor", reader.GetInteger("cursor_position")),
                    ("$created", ctx.Time(reader.GetText("created_at"))),
                    ("$updated", ctx.Time(reader.GetText("updated_at"), reader.GetText("created_at"))));

                WriteCopyActions(ctx, InformationOwner, id, copyActions);
            }
        }

        var missingPdfs = 0L;
        ctx.Progress.BeginPhase("PDF-Anhänge werden übernommen", ctx.SourceCount("information_pdf_attachments"));
        using (var reader = ctx.Read("SELECT * FROM information_pdf_attachments ORDER BY information_entry_id, ordinal;"))
        {
            while (reader.Read())
            {
                ctx.Progress.Step();
                var relative = reader.GetText("relative_path");
                var name = reader.GetText("name");
                var asset = ctx.ImportFile(
                    assets, relative, $"PDF „{name}“", ctx.Layout.PdfDirectory, ctx.Layout.ProgramDirectory, ctx.Layout.DataDirectory);
                if (asset is null)
                {
                    missingPdfs++;
                    continue;
                }

                ctx.Write(
                    """
                    INSERT INTO information_attachments(entry_id, ordinal, asset_id, display_name, added_utc)
                    VALUES($entry, $ordinal, $asset, $name, $added);
                    """,
                    ("$entry", reader.GetText("information_entry_id")),
                    ("$ordinal", reader.GetInteger("ordinal")),
                    ("$asset", asset.Id),
                    ("$name", name.Length > 0 ? name : Path.GetFileName(LegacyPath.Normalize(relative))),
                    ("$added", ctx.Time(reader.GetText("added_at"))));
            }
        }

        ctx.Compare("Reiter (Schreibbereiche)", ctx.SourceCount("information_entries"), "information_entries");
        ctx.Compare("PDF-Anhänge", ctx.SourceCount("information_pdf_attachments"), "information_attachments", missingPdfs);
        ctx.Compare("Copy-Actions (Bereiche)", copyActionsExpected, ctx.TargetCount("copy_actions", "owner_kind = 'information'"));
    }

    private static void WriteCopyActions(Context ctx, string ownerKind, string ownerId, IReadOnlyList<CopyAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            ctx.Write(
                "INSERT INTO copy_actions(owner_kind, owner_id, ordinal, text, occurrence) VALUES($kind, $owner, $ordinal, $text, $occurrence);",
                ("$kind", ownerKind),
                ("$owner", ownerId),
                ("$ordinal", i),
                ("$text", actions[i].Text),
                ("$occurrence", actions[i].Occurrence));
        }
    }

    // ---------------------------------------------------------------- Anrufe

    private static void ImportCalls(Context ctx)
    {
        ctx.Progress.BeginPhase("Anrufe werden übernommen", ctx.SourceCount("calls"));
        using (var reader = ctx.Read("SELECT * FROM calls ORDER BY created_at, id;"))
        {
            while (reader.Read())
            {
                ctx.Progress.Step();
                var legacyStatus = reader.GetText("status");
                var status = CallStatusText.ParseLegacy(legacyStatus);
                if (status == CallStatus.Open && legacyStatus.Length > 0 && legacyStatus != CallStatus.Open.ToDisplayName())
                {
                    ctx.Report.Warnings.Add($"Anruf {reader.GetText("customer_number")}: unbekannter Status „{legacyStatus}“ → „{status.ToDisplayName()}“.");
                }

                ctx.Write(
                    """
                    INSERT INTO calls(id, customer_number, customer_id, phone1, phone2, phone3, phone_private, phone_direct, mobile,
                        alternative_phone, email1, email2, email_target, fax, notes, reason_text, reason_rtf, solution_text, solution_rtf,
                        status, callback_target, is_billable, billing_start_utc, billing_end_utc, received_utc, created_utc, updated_utc,
                        completed_utc, reminder_utc, reminder_active, reminder_last_shown_utc, is_collapsed)
                    VALUES($id, $customer, $customerId, $phone1, $phone2, $phone3, $phonePrivate, $phoneDirect, $mobile,
                        $alternative, $email1, $email2, $emailTarget, $fax, $notes, $reason, $reasonRtf, $solution, $solutionRtf,
                        $status, $callback, $billable, $billingStart, $billingEnd, $received, $created, $updated,
                        $completed, $reminder, $reminderActive, $reminderShown, $collapsed);
                    """,
                    ("$id", reader.GetText("id")),
                    ("$customer", reader.GetText("customer_number")),
                    ("$customerId", reader.GetText("call_identifier")),
                    ("$phone1", reader.GetText("phone1")),
                    ("$phone2", reader.GetText("phone2")),
                    ("$phone3", reader.GetText("phone3")),
                    ("$phonePrivate", reader.GetText("phone_private")),
                    ("$phoneDirect", reader.GetText("phone_direct")),
                    ("$mobile", reader.GetText("mobile")),
                    ("$alternative", reader.GetText("alternative_phone")),
                    ("$email1", reader.GetText("email1")),
                    ("$email2", reader.GetText("email2")),
                    ("$emailTarget", reader.GetText("email_target")),
                    ("$fax", reader.GetText("fax")),
                    ("$notes", reader.GetText("notes")),
                    ("$reason", reader.GetText("reason")),
                    ("$reasonRtf", reader.GetText("reason_rtf")),
                    ("$solution", reader.GetText("work_performed")),
                    ("$solutionRtf", reader.GetText("work_performed_rtf")),
                    ("$status", status.ToString()),
                    ("$callback", reader.GetText("callback_targets").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty),
                    ("$billable", reader.GetFlag("is_billable").ToDb()),
                    ("$billingStart", ctx.OptionalTime(reader.GetText("billing_start"))),
                    ("$billingEnd", ctx.OptionalTime(reader.GetText("billing_end"))),
                    ("$received", ctx.OptionalTime(reader.GetText("received_at"))),
                    ("$created", ctx.Time(reader.GetText("created_at"), reader.GetText("received_at"))),
                    ("$updated", ctx.Time(reader.GetText("updated_at"), reader.GetText("created_at"))),
                    ("$completed", ctx.OptionalTime(reader.GetText("completed_at"))),
                    ("$reminder", ctx.OptionalTime(reader.GetText("reminder_at"))),
                    ("$reminderActive", reader.GetFlag("reminder_active").ToDb()),
                    ("$reminderShown", ctx.OptionalTime(reader.GetText("reminder_last_shown"))),
                    ("$collapsed", reader.GetFlag("is_collapsed").ToDb()));
            }
        }

        ctx.Compare("Anrufe", ctx.SourceCount("calls"), "calls");
        ctx.Compare(
            "Aktive Erinnerungen",
            Convert.ToInt64(ctx.Source.Scalar("SELECT COUNT(*) FROM calls WHERE reminder_active = 1;"), CultureInfo.InvariantCulture),
            ctx.TargetCount("calls", "reminder_active = 1"));
    }

    // ---------------------------------------------------------------- Papierkorb

    private static void ImportTrash(Context ctx)
    {
        ctx.Progress.BeginPhase("Papierkorb wird übernommen", ctx.SourceCount("deleted_calls") + ctx.SourceCount("deleted_fix_entries"));
        ImportTrashTable(ctx, "SELECT call_id AS original_id, deleted_at, snapshot_xml FROM deleted_calls ORDER BY id;", "call");
        ImportTrashTable(ctx, "SELECT entry_id AS original_id, deleted_at, snapshot_xml FROM deleted_fix_entries ORDER BY deleted_at;", "knowledge");
        ctx.Compare("Papierkorb", ctx.SourceCount("deleted_calls") + ctx.SourceCount("deleted_fix_entries"), "trash");
    }

    /// <summary>
    /// Gelöschte Objekte liegen in PixFix 69 als XmlSerializer-Snapshot vor. Sie werden verlustfrei als
    /// Feld/Wert-Liste (JSON) gespeichert und erst beim Wiederherstellen in das neue Modell überführt.
    /// </summary>
    private static void ImportTrashTable(Context ctx, string sql, string kind)
    {
        using var reader = ctx.Read(sql);
        while (reader.Read())
        {
            ctx.Progress.Step();
            var xml = XDocument.Parse(reader.GetText("snapshot_xml"));
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var element in xml.Root?.Elements() ?? [])
            {
                values[element.Name.LocalName] = element.Value;
            }

            var snapshot = JsonSerializer.Serialize(new LegacySnapshot("pixfix69-" + (xml.Root?.Name.LocalName ?? kind), values));
            ctx.Write(
                "INSERT INTO trash(id, kind, original_id, deleted_utc, snapshot_json) VALUES($id, $kind, $original, $deleted, $snapshot);",
                ("$id", NewId()),
                ("$kind", kind),
                ("$original", reader.GetText("original_id")),
                ("$deleted", ctx.Time(reader.GetText("deleted_at"))),
                ("$snapshot", snapshot));
        }
    }

    private sealed record LegacySnapshot(string Format, Dictionary<string, string> Values);

    // ---------------------------------------------------------------- Vorlagen und Einstellungen

    private static readonly (string Legacy, string Key)[] SettingKeys =
    [
        ("CallDialMethod", "telephony.dial_method"),
        ("EmailProgram", "mail.program"),
        ("FaxPrinter", "fax.printer"),
        ("AnyDeskShortcutPath", "remote.anydesk_shortcut"),
        ("BillableSupportRecipient", "billing_mail.recipient"),
        ("BillableSupportSubject", "billing_mail.subject"),
        ("BillableSupportBody", "billing_mail.body"),
        ("SpellCheckEnabled", "editor.spellcheck_enabled"),
        ("SpellLanguage", "editor.spell_language"),
        ("TiMonitorEnabled", "ti_monitor.enabled"),
        ("TiMonitorAutoCheck", "ti_monitor.auto_check"),
        ("TiMonitorBlink", "ti_monitor.blink"),
        ("MainWorkspaceOrder", "ui.workspace_order"),
    ];

    private void ImportSettings(Context ctx)
    {
        ctx.Progress.BeginPhase("Vorlagen und Einstellungen werden übernommen", 1);
        ctx.Progress.Step();
        if (!File.Exists(ctx.Layout.SettingsFile))
        {
            ctx.Report.Warnings.Add("settings.xml nicht gefunden – Vorlagen und Einstellungen wurden nicht übernommen.");
            return;
        }

        var root = XDocument.Load(ctx.Layout.SettingsFile).Root
            ?? throw new LegacyImportException("settings.xml ist leer.");

        foreach (var (legacy, key) in SettingKeys)
        {
            var element = root.Element(legacy);
            if (element is not null && !element.HasElements)
            {
                WriteSetting(ctx, key, element.Value);
            }
        }

        var recent = root.Element("RecentSearches")?.Elements("string").Select(e => e.Value).Where(v => v.Length > 0).ToList() ?? [];
        if (recent.Count > 0)
        {
            WriteSetting(ctx, "search.recent", string.Join('\n', recent));
        }

        var expectedTemplates = 0;
        var order = 0;
        expectedTemplates += ImportTemplates(ctx, root, "EmailTemplates", "EmailTemplate", _ => TemplateKind.Email, ref order);
        expectedTemplates += ImportTemplates(ctx, root, "FaxTemplates", "FaxTemplate", _ => TemplateKind.Fax, ref order);
        expectedTemplates += ImportTemplates(ctx, root, "QuickTextSnippets", "QuickTextSnippet",
            e => IsTrue(e, "Code") ? TemplateKind.CodeSnippet : TemplateKind.Snippet, ref order);
        expectedTemplates += ImportTemplates(ctx, root, "EntryTemplates", "EntryTemplateDefinition", _ => TemplateKind.Entry, ref order);
        expectedTemplates += ImportTemplates(ctx, root, "CallReasonPresets", "CallReasonPreset", _ => TemplateKind.CallReason, ref order);
        expectedTemplates += ImportTemplates(ctx, root, "CallSolutionPresets", "CallSolutionPreset", _ => TemplateKind.CallSolution, ref order);
        ctx.Compare("Vorlagen", expectedTemplates, "templates");

        var cobra = Items(root, "CobraQuickTransferPresets", "CobraQuickTransferPreset");
        for (var i = 0; i < cobra.Count; i++)
        {
            var e = cobra[i];
            ctx.Write(
                """
                INSERT INTO cobra_quick_texts(name, cobra_text, notes_text, work_text, complete_call, schedule_recall, sort_order)
                VALUES($name, $cobra, $notes, $work, $complete, $recall, $order);
                """,
                ("$name", Text(e, "Name")),
                ("$cobra", Text(e, "CobraText")),
                ("$notes", Text(e, "NotesText")),
                ("$work", Text(e, "WorkText")),
                ("$complete", IsTrue(e, "CompleteCall").ToDb()),
                ("$recall", IsTrue(e, "ScheduleRecall").ToDb()),
                ("$order", i));
        }

        ctx.Compare("Cobra-Schnelltexte", cobra.Count, "cobra_quick_texts");

        var faxDocuments = Items(root, "FaxDocuments", "FaxDocument");
        var missingFax = 0L;
        for (var i = 0; i < faxDocuments.Count; i++)
        {
            var relative = Text(faxDocuments[i], "RelativePath");
            var name = Text(faxDocuments[i], "Name");
            var asset = ctx.ImportFile(
                assets, relative, $"Fax-Dokument „{name}“", ctx.Layout.FaxDirectory, ctx.Layout.ProgramDirectory, ctx.Layout.DataDirectory);
            if (asset is null)
            {
                missingFax++;
                continue;
            }

            ctx.Write(
                "INSERT INTO fax_documents(name, asset_id, sort_order) VALUES($name, $asset, $order);",
                ("$name", name.Length > 0 ? name : Path.GetFileName(LegacyPath.Normalize(relative))),
                ("$asset", asset.Id),
                ("$order", i));
        }

        ctx.Compare("Fax-Dokumente", faxDocuments.Count, "fax_documents", missingFax);
    }

    private static int ImportTemplates(
        Context ctx, XElement root, string listName, string itemName, Func<XElement, string> kind, ref int order)
    {
        var items = Items(root, listName, itemName);
        foreach (var e in items)
        {
            ctx.Write(
                "INSERT INTO templates(kind, name, subject, body, sort_order) VALUES($kind, $name, $subject, $body, $order);",
                ("$kind", kind(e)),
                ("$name", Text(e, "Name")),
                ("$subject", Text(e, "Subject")),
                ("$body", Text(e, "Body")),
                ("$order", order++));
        }

        return items.Count;
    }

    private static void WriteSetting(Context ctx, string key, string value) =>
        ctx.Write("INSERT INTO settings(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$key", key), ("$value", value));

    private static List<XElement> Items(XElement root, string listName, string itemName) =>
        root.Element(listName)?.Elements(itemName).ToList() ?? [];

    private static string Text(XElement element, string name) => element.Element(name)?.Value ?? string.Empty;

    private static bool IsTrue(XElement element, string name) =>
        string.Equals(Text(element, name).Trim(), "true", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- Hilfsfunktionen

    private static SqliteConnection OpenLegacyCopy(LegacyLayout layout, string tempDirectory)
    {
        Directory.CreateDirectory(tempDirectory);
        var copy = Path.Combine(tempDirectory, "pixfix.db");
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = layout.DatabaseFile + suffix;
            if (File.Exists(file))
            {
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = File.Create(copy + suffix);
                input.CopyTo(output);
            }
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = copy,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void CheckLegacyDatabase(SqliteConnection source)
    {
        var integrity = source.Scalar("PRAGMA integrity_check;") as string;
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyImportException($"Die PixFix-69-Datenbank ist beschädigt (integrity_check: {integrity}).");
        }

        var hasSchemaInfo = source.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_info';");
        if (Convert.ToInt64(hasSchemaInfo, CultureInfo.InvariantCulture) == 0)
        {
            throw new LegacyImportException("Die Datenbank enthält keine Tabelle schema_info – ist das wirklich eine PixFix-69-Datenbank?");
        }

        var version = Convert.ToInt64(source.Scalar("SELECT MAX(schema_version) FROM schema_info;"), CultureInfo.InvariantCulture);
        if (version != SupportedLegacySchemaVersion)
        {
            throw new LegacyImportException(
                $"PixFix-69-Schemaversion {version} wird nicht unterstützt (erwartet: {SupportedLegacySchemaVersion}).");
        }
    }

    private void WriteImportLog(SqliteConnection target, ImportReport report, DateTimeOffset startedUtc)
    {
        target.Execute(
            "INSERT INTO import_log(source, started_utc, finished_utc, succeeded, report) VALUES($source, $started, $finished, $ok, $report);",
            null,
            ("$source", report.Source),
            ("$started", startedUtc.ToDb()),
            ("$finished", timeProvider.GetUtcNow().ToDb()),
            ("$ok", report.Succeeded.ToDb()),
            ("$report", report.ToText()));
    }

    private static void DeleteTempDirectory(string? directory, ImportReport report)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            report.Warnings.Add($"Temporärer Ordner konnte nicht gelöscht werden: {directory} ({ex.Message})");
        }
        catch (UnauthorizedAccessException ex)
        {
            report.Warnings.Add($"Temporärer Ordner konnte nicht gelöscht werden: {directory} ({ex.Message})");
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Gemeinsamer Zustand eines Importlaufs.</summary>
    private sealed class Context(SqliteConnection source, SqliteConnection target, LegacyLayout layout, ImportReport report, DateTimeOffset nowUtc)
    {
        private readonly Dictionary<string, Asset> importedFiles = new(StringComparer.OrdinalIgnoreCase);

        public SqliteConnection Source { get; } = source;
        public SqliteConnection Target { get; } = target;
        public LegacyLayout Layout { get; } = layout;
        public ImportReport Report { get; } = report;
        public DateTimeOffset NowUtc { get; } = nowUtc;
        public SqliteTransaction? Transaction { get; set; }
        public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;
        public ImportProgressTracker Progress { get; init; } = new(null);

        public SqliteDataReader Read(string sql)
        {
            // Der Befehl bleibt bis zum Ende des Readers am Leben; er wird mit der Verbindung aufgeräumt.
            var command = SqliteExtensions.Create(Source, sql, null);
            return command.ExecuteReader();
        }

        public void Write(string sql, params (string Name, object? Value)[] parameters) =>
            Target.Execute(sql, Transaction, parameters);

        public long SourceCount(string table) => Source.Count(table);

        public long TargetCount(string table, string? where = null) =>
            Convert.ToInt64(
                Target.Scalar("SELECT COUNT(*) FROM " + table + (where is null ? string.Empty : " WHERE " + where) + ";", Transaction),
                CultureInfo.InvariantCulture);

        public void Compare(string name, long expected, string targetTable, long missing = 0) =>
            Report.Counts.Add(new ImportCount(name, expected, TargetCount(targetTable), missing));

        public void Compare(string name, long expected, long imported) =>
            Report.Counts.Add(new ImportCount(name, expected, imported));

        /// <summary>Pflichtzeitpunkt; leer → Ersatzwert, sonst Importzeitpunkt.</summary>
        public string Time(string legacy, string? fallback = null) =>
            (LegacyTimestamp.ToUtc(legacy, Zone) ?? LegacyTimestamp.ToUtc(fallback, Zone) ?? NowUtc).ToDb();

        public string? OptionalTime(string legacy) => LegacyTimestamp.ToUtc(legacy, Zone).ToDb();

        /// <summary>
        /// Sucht die Datei in den angegebenen Ordnern und übernimmt sie in die Ablage.
        /// Fehlt sie, wird ein Hinweis notiert und <c>null</c> zurückgegeben.
        /// </summary>
        public Asset? ImportFile(AssetStore store, string relativePath, string description, params string[] baseDirectories)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                Report.Warnings.Add($"{description}: kein Dateipfad hinterlegt.");
                return null;
            }

            foreach (var baseDirectory in baseDirectories)
            {
                var full = LegacyPath.CombineWithin(baseDirectory, relativePath);
                if (full is null || !File.Exists(full))
                {
                    continue;
                }

                if (!importedFiles.TryGetValue(full, out var asset))
                {
                    asset = store.Import(full, Target, Transaction, NowUtc);
                    importedFiles[full] = asset;
                }

                return asset;
            }

            Report.Warnings.Add($"{description}: Datei '{relativePath}' fehlt in PixFix 69.");
            return null;
        }
    }
}
