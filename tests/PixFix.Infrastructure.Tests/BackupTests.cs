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
    private void CreateLegacy30Backup(string file)
    {
        var files = new List<(string Path, string Source)>();
        foreach (var source in Directory.GetFiles(fixture.DataDirectory, "*", SearchOption.AllDirectories))
        {
            files.Add((Path.GetRelativePath(fixture.DataDirectory, source).Replace('\\', '/'), source));
        }

        foreach (var folder in new[] { "Fax-Dokumente", "PDF-Dokumente" })
        {
            foreach (var source in Directory.GetFiles(Path.Combine(fixture.ProgramDirectory, folder)))
            {
                files.Add(($"__PixFixProgram/{folder}/{Path.GetFileName(source)}", source));
            }
        }

        var manifest = new StringBuilder("<?xml version=\"1.0\"?><PixFixBackupManifest><Format>PixFix-Komplettsicherung</Format><Version>3.0</Version>"
            + "<Created>2026-11-05T14:55:23</Created><StorageMode>Sqlite</StorageMode><SchemaVersion>1</SchemaVersion><Calls>2</Calls><Files>");
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
