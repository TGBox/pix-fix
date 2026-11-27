using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Backup;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Tests;

/// <summary>Sicherung 4.0, Wiederherstellung, PixFix-69-Sicherung 3.0 und Regeln der automatischen Sicherung (SPEC F11).</summary>
public sealed class BackupTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly AppPaths paths;
    private readonly PixFixDatabase database;
    private readonly BackupWriter writer;
    private readonly RestoreService restore;

    public BackupTests()
    {
        paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        writer = new BackupWriter(paths, database);
        restore = new RestoreService(paths, writer, TimeZoneInfo.Utc);
        fixture.WithDefaultData();
    }

    private string BackupFolder => Path.Combine(fixture.Root, "sicherungen");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        fixture.Dispose();
    }

    private void ImportFixture() =>
        Assert.True(new LegacyImporter(database, new AssetStore(paths), TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);

    [Fact]
    public void BackupCanBeVerifiedAndRestored()
    {
        ImportFixture();
        var file = Path.Combine(BackupFolder, "test.pixfix");

        var result = writer.Create(file);
        var info = BackupArchive.Verify(file);

        Assert.Equal(BackupKind.Current, info.Kind);
        Assert.Equal(2, result.Counts["knowledge_entries"]);
        Assert.Equal(2, result.Counts["calls"]);
        Assert.Contains(info.Files, f => f.Path == "pixfix.db");
        Assert.Equal(result.Counts["assets"], info.Files.Count(f => f.Path.StartsWith("assets/", StringComparison.Ordinal)));
        Assert.False(File.Exists(file + ".partial"));

        // Nach der Sicherung geänderte Daten verschwinden mit der Wiederherstellung wieder.
        var calls = new CallRepository(database);
        calls.Create();
        Assert.Equal(3, calls.LoadAll().Count);

        var report = restore.RestoreArchive(file, BackupFolder);

        Assert.True(report.Succeeded, report.ToText());
        Assert.Equal(2, new CallRepository(database).LoadAll().Count);
        Assert.Single(Directory.GetFiles(BackupFolder, "Vor-Wiederherstellung-*.pixfix"));
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "restore-stage")));
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "restore-old")));
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT relative_path FROM assets;", null);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            Assert.True(File.Exists(Path.Combine(paths.DataDirectory, reader.GetString(0))));
        }
    }

    [Fact]
    public void DamagedBackupIsRejectedAndDataStaysUnchanged()
    {
        ImportFixture();
        var file = Path.Combine(BackupFolder, "test.pixfix");
        writer.Create(file);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Update))
        {
            var asset = zip.Entries.First(e => e.FullName.StartsWith("assets/", StringComparison.Ordinal));
            var name = asset.FullName;
            asset.Delete();
            using var stream = zip.CreateEntry(name).Open();
            stream.Write([0xFF, 0xFE]);
        }

        Assert.Throws<BackupException>(() => BackupArchive.Verify(file));
        new CallRepository(database).Create();

        var report = restore.RestoreArchive(file, BackupFolder);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Errors, e => e.Contains("beschädigt", StringComparison.Ordinal));
        Assert.Equal(3, new CallRepository(database).LoadAll().Count);
        Assert.False(Directory.Exists(BackupFolder) && Directory.GetFiles(BackupFolder, "Vor-*").Length > 0);
    }

    [Theory]
    [InlineData("../x.png")]
    [InlineData("assets/../../x.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x.dll")]
    [InlineData("assets//x.png")]
    [InlineData("")]
    public void UnsafePathsAreRejected(string path) =>
        Assert.Throws<BackupException>(() => BackupArchive.SafeRelativePath(path));

    [Fact]
    public void SafePathsAreNormalized() =>
        Assert.Equal("assets/ab/cd.png", BackupArchive.SafeRelativePath(@"assets\ab\cd.png"));

    [Fact]
    public void PixFix69BackupFormat30IsImported()
    {
        var file = Path.Combine(fixture.Root, "PixFix69.pixfix");
        CreateLegacy30Backup(file);

        var info = BackupArchive.Verify(file);
        Assert.Equal(BackupKind.Legacy30, info.Kind);

        var report = restore.RestoreArchive(file, BackupFolder);

        Assert.True(report.Succeeded, report.ToText());
        Assert.Equal(2, new CallRepository(database).LoadAll().Count);
        Assert.Single(new Communication.FaxDocumentRepository(database, new AssetStore(paths)).LoadAll());
    }

    [Fact]
    public void PixFix69BackupFormat21IsImported()
    {
        var file = Path.Combine(fixture.Root, "PixFix69-xml.pixfix");
        CreateLegacy21Backup(file);

        var info = BackupArchive.Verify(file);
        Assert.Equal(BackupKind.Legacy21, info.Kind);

        var report = restore.RestoreArchive(file, BackupFolder);

        Assert.True(report.Succeeded, report.ToText());
        using var db = database.Open();
        Assert.Equal(1, db.Count("knowledge_entries"));
        Assert.Equal(2, db.Count("knowledge_entry_images"));
        Assert.Equal(1, db.Count("calls"));
        Assert.Equal(1, db.Count("trash"));
        Assert.Equal(1, db.Count("workspaces"));
        Assert.Equal(1, db.Count("information_entries"));
        Assert.Equal(1, db.Count("information_attachments"));
        Assert.Equal(1, db.Count("fax_documents"));
        var call = Assert.Single(new CallRepository(database).LoadAll());
        Assert.Equal("00074", call.CustomerNumber);
        Assert.Equal(Core.Calls.CallStatus.Done, call.Status);
        Assert.True(call.IsBillable);
    }

    [Fact]
    public void Format21WithWrongCountsIsRejected()
    {
        var file = Path.Combine(fixture.Root, "PixFix69-xml.pixfix");
        CreateLegacy21Backup(file, manifestCalls: 5);

        var report = restore.RestoreArchive(file, BackupFolder);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Errors, e => e.Contains("Anrufe", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyFolderCanReplaceExistingData()
    {
        ImportFixture();
        new CallRepository(database).Create();

        var report = restore.ImportLegacyFolder(fixture.ProgramDirectory, BackupFolder);

        Assert.True(report.Succeeded, report.ToText());
        Assert.Equal(2, new CallRepository(database).LoadAll().Count);
        Assert.Single(Directory.GetFiles(BackupFolder, "Vor-Wiederherstellung-*.pixfix"));
    }

    [Fact]
    public void AutoBackupRules()
    {
        var start = new DateTime(2026, 11, 27, 8, 0, 0);
        var idle = TimeSpan.FromMinutes(2);

        Assert.False(AutoBackupPlan.IsDue(null, start.AddMinutes(5), start, idle));
        Assert.True(AutoBackupPlan.IsDue(null, start.AddMinutes(15), start, idle));
        Assert.False(AutoBackupPlan.IsDue(null, start.AddMinutes(15), start, TimeSpan.FromSeconds(10)));
        Assert.False(AutoBackupPlan.IsDue(start.AddHours(1), start.AddHours(3), start, idle));
        Assert.True(AutoBackupPlan.IsDue(start.AddHours(1), start.AddHours(5), start, idle));
        Assert.True(AutoBackupPlan.IsDue(new DateTime(2026, 11, 26, 23, 0, 0), start.AddMinutes(30), start, idle));
        Assert.Equal("AUTO-2026-11-27.pixfix", AutoBackupPlan.FileName(start));

        Directory.CreateDirectory(BackupFolder);
        foreach (var name in new[] { "AUTO-2026-11-27.pixfix", "AUTO-2026-11-21.pixfix", "AUTO-2026-11-20.pixfix", "AUTO-x.pixfix.partial", "Sicherung.pixfix" })
        {
            File.WriteAllText(Path.Combine(BackupFolder, name), "x");
        }

        var deleted = AutoBackupPlan.Cleanup(BackupFolder, start);

        Assert.Equal(new[] { "AUTO-2026-11-20.pixfix", "AUTO-x.pixfix.partial" }, deleted.Order());
        Assert.Equal(3, Directory.GetFiles(BackupFolder).Length);
    }

    /// <summary>Sicherung wie PixFix 69 sie schreibt: Datenordner im Wurzelverzeichnis, Programmordner unter __PixFixProgram.</summary>
    private void CreateLegacy30Backup(string file) =>
        CreateLegacyBackup(file, "3.0", "Sqlite", "<Calls>2</Calls>", _ => true);

    /// <summary>Sicherung 2.1: dieselben Dateien, aber XML statt pixfix.db (Aufbau wie XmlSerializer in PixFix 69).</summary>
    private void CreateLegacy21Backup(string file, int manifestCalls = 1)
    {
        var a = LegacyFixture.EntryA;
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "pixfix.xml"), $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfFixEntry xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <FixEntry><Id>{{a}}</Id><Title>Drucker</Title><Category>Fehlermeldungen</Category><Ocr>OCR A</Ocr><Solution>Drucker Lösung</Solution>
                <Html>&lt;p&gt;a&lt;/p&gt;</Html><Rtf /><NativeDoc>documents\{{a}}.rtf</NativeDoc><Image>images\{{a}}-1.png</Image>
                <Images>images\{{a}}-1.png|images\{{a}}-2.png</Images><Tags>druck</Tags><CopyActions /><Created>2026-09-23T16:12:06</Created>
                <Updated /><NativeFormat>3</NativeFormat><NativeMarkerVersion>1</NativeMarkerVersion><CategoryOrder>0</CategoryOrder><PageOrder>0</PageOrder>
                <ScrollX>0</ScrollX><ScrollY>0</ScrollY><LastOpen>false</LastOpen><Favorite>true</Favorite><Protected>false</Protected><Archived>false</Archived></FixEntry>
            </ArrayOfFixEntry>
            """);
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "anrufe.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfCallEntry>
              <CallEntry><Id>c1</Id><CustomerNumber>00074</CustomerNumber><CallIdentifier>1888</CallIdentifier><Phone1>0351000000</Phone1>
                <Reason>Frage KIM</Reason><WorkPerformed>Erklärt</WorkPerformed><Status>Erledigt</Status><ReceivedAt>2026-11-06T14:01:22</ReceivedAt>
                <BillingStart>2026-11-06T14:01:00</BillingStart><BillingEnd>2026-11-06T14:08:00</BillingEnd><CreatedAt>2026-11-06T14:01:22</CreatedAt>
                <CallbackTargets>Tel.</CallbackTargets><IsBillable>true</IsBillable><IsCollapsed>false</IsCollapsed><ReminderActive>false</ReminderActive></CallEntry>
            </ArrayOfCallEntry>
            """);
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "anrufe-geloescht.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfDeletedCallEntry>
              <DeletedCallEntry><Entry><Id>c0</Id><CustomerNumber>00001</CustomerNumber><Status>Erledigt</Status></Entry><DeletedAt>2026-10-09T10:29:34</DeletedAt></DeletedCallEntry>
            </ArrayOfDeletedCallEntry>
            """);
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "schreibbereiche.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfWritingWorkspace><WritingWorkspace><Id>werkzeuge</Id><Title>Werkzeuge</Title><IconGlyph>🛠</IconGlyph><Order>2</Order></WritingWorkspace></ArrayOfWritingWorkspace>
            """);
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "informationen.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfInformationEntry>
              <InformationEntry><Id>i1</Id><WorkspaceId>werkzeuge</WorkspaceId><Section>KIM</Section><Title>KIM öffnen</Title><Content>https://localhost:8000</Content>
                <Rtf>{\rtf1 KIM}</Rtf><CreatedAt>2026-10-01T11:01:35</CreatedAt><SectionOrder>2</SectionOrder><Order>1</Order>
                <PdfAttachments><InformationPdfAttachment><Id>p1</Id><Name>Anleitung.pdf</Name><RelativePath>Anleitung.pdf</RelativePath><AddedAt>2026-10-13T10:53:43</AddedAt></InformationPdfAttachment></PdfAttachments>
              </InformationEntry>
            </ArrayOfInformationEntry>
            """);

        CreateLegacyBackup(
            file,
            "2.1",
            "LegacyXml",
            $"<Entries>1</Entries><Calls>{manifestCalls}</Calls><DeletedCalls>1</DeletedCalls><WritingWorkspaces>1</WritingWorkspaces><InformationEntries>1</InformationEntries><PdfAttachments>1</PdfAttachments>",
            path => path != "pixfix.db");
    }

    private void CreateLegacyBackup(string file, string version, string storageMode, string counts, Func<string, bool> include)
    {
        var files = new List<(string Path, string Source)>();
        foreach (var source in Directory.GetFiles(fixture.DataDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fixture.DataDirectory, source).Replace('\\', '/');
            if (include(relative) && (version != "3.0" || !relative.EndsWith(".xml", StringComparison.Ordinal) || relative == "settings.xml"))
            {
                files.Add((relative, source));
            }
        }

        foreach (var folder in new[] { "Fax-Dokumente", "PDF-Dokumente" })
        {
            foreach (var source in Directory.GetFiles(Path.Combine(fixture.ProgramDirectory, folder)))
            {
                files.Add(($"__PixFixProgram/{folder}/{Path.GetFileName(source)}", source));
            }
        }

        var manifest = new StringBuilder($"<?xml version=\"1.0\"?><PixFixBackupManifest><Format>PixFix-Komplettsicherung</Format><Version>{version}</Version>"
            + $"<Created>2026-11-05T14:55:23</Created><StorageMode>{storageMode}</StorageMode>{counts}<Files>");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var (path, source) in files)
        {
            zip.CreateEntryFromFile(source, path);
            var bytes = File.ReadAllBytes(source);
            manifest.Append($"<PixFixBackupFile><Path>{path}</Path><Sha256>{Convert.ToHexStringLower(SHA256.HashData(bytes))}</Sha256><Length>{bytes.Length}</Length></PixFixBackupFile>");
        }

        manifest.Append("</Files></PixFixBackupManifest>");
        using var stream = zip.CreateEntry(BackupArchive.LegacyManifestName).Open();
        stream.Write(Encoding.UTF8.GetBytes(manifest.ToString()));
    }
}
