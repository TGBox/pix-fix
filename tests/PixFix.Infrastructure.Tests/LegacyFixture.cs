using System.Text;
using Microsoft.Data.Sqlite;
using PixFix.Core.Legacy;

namespace PixFix.Infrastructure.Tests;

/// <summary>
/// Baut eine kleine, synthetische PixFix-69-Installation (Schema wie 69.74.178–194) in einem Temp-Ordner.
/// Enthält keine echten Kundendaten.
/// </summary>
internal sealed class LegacyFixture : IDisposable
{
    public const string EntryA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string EntryB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly string[] LegacySchema =
    [
        "CREATE TABLE schema_info(schema_version INTEGER NOT NULL,created_utc TEXT NOT NULL,migrated_from TEXT NOT NULL,migration_id TEXT NOT NULL,program_version TEXT NOT NULL)",
        "CREATE TABLE fix_entries(id TEXT PRIMARY KEY,title TEXT,category TEXT,ocr TEXT,solution TEXT,html TEXT,legacy_rtf TEXT,native_doc TEXT,image_primary TEXT,images_legacy TEXT,tags TEXT,copy_actions TEXT,created TEXT,updated TEXT,native_format INTEGER,native_marker_version INTEGER,category_order INTEGER,page_order INTEGER,scroll_x INTEGER,scroll_y INTEGER,last_open INTEGER,favorite INTEGER,protected INTEGER,archived INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE entry_images(id INTEGER PRIMARY KEY AUTOINCREMENT,entry_id TEXT NOT NULL,ordinal INTEGER NOT NULL,relative_path TEXT NOT NULL,is_primary INTEGER NOT NULL,sha256 TEXT,file_length INTEGER,FOREIGN KEY(entry_id) REFERENCES fix_entries(id),UNIQUE(entry_id,ordinal))",
        "CREATE TABLE calls(id TEXT PRIMARY KEY,customer_number TEXT,call_identifier TEXT,alternative_phone TEXT,phone1 TEXT,phone2 TEXT,phone3 TEXT,phone_private TEXT,phone_direct TEXT,mobile TEXT,email1 TEXT,email2 TEXT,email_target TEXT,fax TEXT,notes TEXT,reason TEXT,reason_rtf TEXT,work_performed TEXT,work_performed_rtf TEXT,status TEXT,received_at TEXT,billing_start TEXT,billing_end TEXT,created_at TEXT,updated_at TEXT,completed_at TEXT,reminder_at TEXT,reminder_last_shown TEXT,callback_targets TEXT,is_billable INTEGER,is_collapsed INTEGER,reminder_active INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE deleted_calls(id INTEGER PRIMARY KEY AUTOINCREMENT,source_ordinal INTEGER NOT NULL,call_id TEXT,deleted_at TEXT NOT NULL,snapshot_xml TEXT NOT NULL,source_hash TEXT NOT NULL)",
        "CREATE TABLE writing_workspaces(id TEXT PRIMARY KEY,title TEXT,icon_glyph TEXT,sort_order INTEGER,last_open INTEGER,protected INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE information_entries(id TEXT PRIMARY KEY,workspace_id TEXT,section TEXT,title TEXT,content TEXT,rtf TEXT,copy_actions TEXT,created_at TEXT,updated_at TEXT,section_order INTEGER,item_order INTEGER,cursor_position INTEGER,first_visible_line INTEGER,last_open INTEGER,section_collapsed INTEGER,protected INTEGER,source_hash TEXT NOT NULL)",
        "CREATE TABLE information_pdf_attachments(id TEXT NOT NULL,information_entry_id TEXT NOT NULL,name TEXT,relative_path TEXT,added_at TEXT,ordinal INTEGER NOT NULL,source_hash TEXT NOT NULL,PRIMARY KEY(information_entry_id,ordinal),FOREIGN KEY(information_entry_id) REFERENCES information_entries(id))",
        "CREATE TABLE deleted_fix_entries(trash_id TEXT PRIMARY KEY,entry_id TEXT,deleted_at TEXT NOT NULL,snapshot_xml TEXT NOT NULL,source_hash TEXT NOT NULL)",
    ];

    public LegacyFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "pixfix-tests-" + Guid.NewGuid().ToString("N"));
        ProgramDirectory = Path.Combine(Root, "PixFix-69.74.194");
        DataDirectory = Path.Combine(ProgramDirectory, "PixFix-Daten");
        TargetDirectory = Path.Combine(Root, "neu");
        Directory.CreateDirectory(Path.Combine(DataDirectory, "documents"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "images"));
        Directory.CreateDirectory(Path.Combine(ProgramDirectory, "PDF-Dokumente"));
        Directory.CreateDirectory(Path.Combine(ProgramDirectory, "Fax-Dokumente"));
    }

    public string Root { get; }
    public string ProgramDirectory { get; }
    public string DataDirectory { get; }
    public string TargetDirectory { get; }
    public string DatabaseFile => Path.Combine(DataDirectory, "pixfix.db");

    /// <summary>Standardbestand: 2 Einträge (3 Bilder, davon 2 identisch), 1 Bereich mit 2 Reitern, 2 Anrufe, Papierkorb, Vorlagen.</summary>
    public LegacyFixture WithDefaultData(int schemaVersion = 1)
    {
        File.WriteAllBytes(Path.Combine(DataDirectory, "images", EntryA + "-1.png"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(DataDirectory, "images", EntryA + "-2.png"), [5, 6, 7, 8]);
        File.WriteAllBytes(Path.Combine(DataDirectory, "images", EntryB + "-1.png"), [1, 2, 3, 4]);
        File.WriteAllText(
            Path.Combine(DataDirectory, "documents", EntryA + ".rtf"),
            "{\\rtf1 Drucker " + LegacyImageMarker.Encode(@"images\" + EntryA + "-1.png")
            + "{\\pict\\wmetafile8\\picwgoal1500\\pichgoal750 0000}"
            + " und ein Bild ohne Marker {\\pict\\wmetafile8\\picwgoal100\\pichgoal100 "
            + Convert.ToHexString(WmfSample.Create()) + "}}");
        File.WriteAllBytes(Path.Combine(ProgramDirectory, "PDF-Dokumente", "Anleitung.pdf"), Encoding.ASCII.GetBytes("%PDF-1.4"));
        File.WriteAllBytes(Path.Combine(ProgramDirectory, "Fax-Dokumente", "Fax.doc"), [9, 9, 9]);

        using var db = OpenLegacy();
        foreach (var statement in LegacySchema)
        {
            Exec(db, statement);
        }

        Exec(db, $"INSERT INTO schema_info VALUES({schemaVersion},'2026-10-19T07:19:30Z','LegacyBackup2.1','x','69.74.178')");
        Exec(db, "INSERT INTO fix_entries(id,title,category,ocr,solution,html,legacy_rtf,native_doc,tags,copy_actions,created,updated,category_order,page_order,favorite,protected,archived,source_hash) " +
            $"VALUES('{EntryA}','Drucker','Fehlermeldungen','OCR A','Drucker Lösung','<p>a</p>','','documents\\{EntryA}.rtf','druck','{Copy("@PFCP:0\nipconfig /all")}','2026-09-23T16:12:06','2026-11-05T09:19:28',2,1,1,0,0,'h')");
        Exec(db, "INSERT INTO fix_entries(id,title,category,ocr,solution,html,legacy_rtf,native_doc,tags,copy_actions,created,updated,category_order,page_order,favorite,protected,archived,source_hash) " +
            $"VALUES('{EntryB}','Nur HTML','Allgemeines','','Text B','<p>b</p>','','documents\\fehlt.rtf','','','2026-09-24T10:00:00','',1,0,0,1,1,'h')");
        Exec(db, $"INSERT INTO entry_images(entry_id,ordinal,relative_path,is_primary) VALUES('{EntryA}',0,'images\\{EntryA}-1.png',1)");
        Exec(db, $"INSERT INTO entry_images(entry_id,ordinal,relative_path,is_primary) VALUES('{EntryA}',1,'images\\{EntryA}-2.png',0)");
        Exec(db, $"INSERT INTO entry_images(entry_id,ordinal,relative_path,is_primary) VALUES('{EntryB}',0,'images\\{EntryB}-1.png',1)");

        Exec(db, "INSERT INTO writing_workspaces VALUES('werkzeuge','Werkzeuge','🛠',2,0,0,'h')");
        Exec(db, "INSERT INTO information_entries VALUES('i1','werkzeuge','KIM','KIM öffnen','https://localhost:8000','{\\rtf1 KIM}'," +
            $"'{Copy("@PFCP:0\nhttps://localhost:8000")}|{Copy("@PFCP:0\nping 8.8.8.8")}','2026-10-01T11:01:35','2026-10-16T08:35:44',2,1,0,0,0,1,0,'h')");
        Exec(db, "INSERT INTO information_entries VALUES('i2','werkzeuge','KIM','Nur Text','Hallo','','','2026-10-01T11:01:35','',2,2,0,0,0,1,0,'h')");
        Exec(db, "INSERT INTO information_pdf_attachments VALUES('p1','i1','Anleitung.pdf','Anleitung.pdf','2026-10-13T10:53:43',0,'h')");

        Exec(db, "INSERT INTO calls(id,customer_number,call_identifier,phone1,email1,reason,reason_rtf,work_performed,status,received_at,created_at,updated_at,completed_at,callback_targets,is_billable,billing_start,billing_end,reminder_active,reminder_at,source_hash) " +
            "VALUES('c1','00074','1888','0351000000','praxis@example.invalid','Frage KIM','{\\rtf1 Frage KIM}','Erklärt','Erledigt','2026-11-06T14:01:22','2026-11-06T14:01:22','2026-11-06T14:08:33','2026-11-06T14:08:33','Tel.',1,'2026-11-06T14:01:00','2026-11-06T14:08:00',0,'','h')");
        Exec(db, "INSERT INTO calls(id,customer_number,status,created_at,updated_at,reminder_active,reminder_at,callback_targets,source_hash) " +
            "VALUES('c2','00075','Später erneut anrufen','2026-11-07T09:00:00','2026-11-07T09:00:00',1,'2026-11-07T11:00:00','Mobil;Tel.','h')");
        Exec(db, "INSERT INTO deleted_calls(source_ordinal,call_id,deleted_at,snapshot_xml,source_hash) VALUES(0,'c0','2026-10-09T10:29:34'," +
            "'<?xml version=\"1.0\" encoding=\"utf-16\"?><CallEntry><Id>c0</Id><CustomerNumber>00001</CustomerNumber><Status>Erledigt</Status></CallEntry>','h')");

        File.WriteAllText(Path.Combine(DataDirectory, "settings.xml"), SettingsXml, Encoding.UTF8);
        return this;
    }

    public SqliteConnection OpenLegacy()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabaseFile, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Copy(string raw) => Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

    private const string SettingsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <PixFixSettings xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <WindowWidth>1748</WindowWidth>
          <SpellCheckEnabled>true</SpellCheckEnabled>
          <CallDialMethod>Windows-Standard (tel:)</CallDialMethod>
          <EmailProgram>Outlook Classic</EmailProgram>
          <BillableSupportRecipient>rechnung@example.invalid</BillableSupportRecipient>
          <MainWorkspaceOrder>werkzeuge|n</MainWorkspaceOrder>
          <EmailTemplates>
            <EmailTemplate><Name>Nicht erreicht</Name><Subject>Ihr Anruf</Subject><Body>Hallo {LNUM}</Body></EmailTemplate>
          </EmailTemplates>
          <FaxTemplates />
          <FaxDocuments>
            <FaxDocument><Name>Fax nicht erreicht</Name><RelativePath>Fax.doc</RelativePath></FaxDocument>
          </FaxDocuments>
          <QuickTextSnippets>
            <QuickTextSnippet><Name>ipconfig</Name><Body>ipconfig /all</Body><Code>true</Code></QuickTextSnippet>
          </QuickTextSnippets>
          <EntryTemplates>
            <EntryTemplateDefinition><Name>Störung</Name><Body>Kurzbeschreibung:</Body></EntryTemplateDefinition>
          </EntryTemplates>
          <CallSolutionPresets>
            <CallSolutionPreset><Name>Bekannter Fehler</Name><Body>Entwicklung informiert.</Body></CallSolutionPreset>
          </CallSolutionPresets>
          <CallReasonPresets>
            <CallReasonPreset><Name>Nicht erreicht</Name><Body>Praxis nicht erreicht.</Body></CallReasonPreset>
          </CallReasonPresets>
          <CobraQuickTransferPresets>
            <CobraQuickTransferPreset><Name>NE</Name><CobraText>Praxis nicht erreicht</CobraText><NotesText>NE,</NotesText><WorkText /><CompleteCall>false</CompleteCall><ScheduleRecall>true</ScheduleRecall></CobraQuickTransferPreset>
          </CobraQuickTransferPresets>
          <RecentSearches><string>drucker</string><string>kim</string></RecentSearches>
        </PixFixSettings>
        """;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
