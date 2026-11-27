using Microsoft.Data.Sqlite;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Tests;

public sealed class LegacyImporterTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly LegacyFixture fixture = new();
    private readonly PixFixDatabase database;
    private readonly LegacyImporter importer;

    public LegacyImporterTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        importer = new LegacyImporter(database, new AssetStore(paths), Berlin);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void ImportsAllObjectsWithMatchingCounts()
    {
        fixture.WithDefaultData();

        var report = importer.Import(fixture.ProgramDirectory);

        Assert.True(report.Succeeded, report.ToText());
        Assert.All(report.Counts, c => Assert.True(c.Matches, c.ToString()));
        using var db = database.Open();
        Assert.Equal(2, db.Count("knowledge_sections"));
        Assert.Equal(2, db.Count("knowledge_entries"));
        Assert.Equal(3, db.Count("knowledge_entry_images"));
        Assert.Equal(1, db.Count("workspaces"));
        Assert.Equal(2, db.Count("information_entries"));
        Assert.Equal(1, db.Count("information_attachments"));
        Assert.Equal(3, db.Count("copy_actions"));
        Assert.Equal(2, db.Count("calls"));
        Assert.Equal(1, db.Count("trash"));
        Assert.Equal(5, db.Count("templates"));
        Assert.Equal(1, db.Count("cobra_quick_texts"));
        Assert.Equal(1, db.Count("fax_documents"));
        Assert.Equal(1, db.Count("import_log"));
    }

    [Fact]
    public void ReportsSteadyProgressUpTo100Percent()
    {
        fixture.WithDefaultData();
        var progress = new RecordingProgress();

        importer.Import(fixture.ProgramDirectory, progress);

        Assert.NotEmpty(progress.Reports);
        var percents = progress.Reports.Select(p => p.Percent).ToList();
        Assert.Equal(percents.OrderBy(p => p), percents);
        Assert.Equal(100, percents[^1]);
        Assert.Contains(progress.Reports, p => p.Message.StartsWith("Bilder werden übernommen (3 von 3)", StringComparison.Ordinal));
    }

    /// <summary>Synchroner Empfänger – <see cref="Progress{T}"/> würde asynchron zustellen.</summary>
    private sealed class RecordingProgress : IProgress<ImportProgress>
    {
        public List<ImportProgress> Reports { get; } = [];

        public void Report(ImportProgress value) => Reports.Add(value);
    }

    [Fact]
    public void IdenticalFilesAreStoredOnce()
    {
        fixture.WithDefaultData();

        importer.Import(fixture.ProgramDirectory);

        using var db = database.Open();
        // 2 verschiedene Bilder (eines doppelt) + RTF-Dokument + PDF + Fax-Dokument
        Assert.Equal(5, db.Count("assets"));
        var assetFiles = Directory.GetFiles(Path.Combine(fixture.TargetDirectory, "assets"), "*", SearchOption.AllDirectories);
        Assert.Equal(5, assetFiles.Length);
    }

    [Fact]
    public void MapsFieldsAndConvertsTimesToUtc()
    {
        fixture.WithDefaultData();

        importer.Import(fixture.ProgramDirectory);

        using var db = database.Open();
        Assert.Equal("Done", db.Scalar("SELECT status FROM calls WHERE id = 'c1';"));
        Assert.Equal("2026-11-06T13:01:22.000Z", db.Scalar("SELECT created_utc FROM calls WHERE id = 'c1';"));
        Assert.Equal("1888", db.Scalar("SELECT customer_id FROM calls WHERE id = 'c1';"));
        Assert.Equal("CallBackLater", db.Scalar("SELECT status FROM calls WHERE id = 'c2';"));
        Assert.Equal("Mobil", db.Scalar("SELECT callback_target FROM calls WHERE id = 'c2';"));
        Assert.Equal(1L, db.Scalar("SELECT reminder_active FROM calls WHERE id = 'c2';"));

        Assert.Equal("rtf", db.Scalar($"SELECT content_format FROM knowledge_entries WHERE id = '{LegacyFixture.EntryA}';"));
        Assert.NotNull(db.Scalar($"SELECT content_asset_id FROM knowledge_entries WHERE id = '{LegacyFixture.EntryA}';"));
        Assert.Equal("html", db.Scalar($"SELECT content_format FROM knowledge_entries WHERE id = '{LegacyFixture.EntryB}';"));
        Assert.Equal(1L, db.Scalar($"SELECT is_archived FROM knowledge_entries WHERE id = '{LegacyFixture.EntryB}';"));

        Assert.Equal("snippet-code", db.Scalar("SELECT kind FROM templates WHERE name = 'ipconfig';"));
        Assert.Equal("Outlook Classic", db.Scalar("SELECT value FROM settings WHERE key = 'mail.program';"));
        Assert.Equal("drucker\nkim", db.Scalar("SELECT value FROM settings WHERE key = 'search.recent';"));
    }

    [Fact]
    public void MissingFilesAreReportedAsWarnings()
    {
        fixture.WithDefaultData();
        File.Delete(Path.Combine(fixture.DataDirectory, "images", LegacyFixture.EntryA + "-2.png"));

        var report = importer.Import(fixture.ProgramDirectory);

        Assert.True(report.Succeeded, report.ToText());
        var images = Assert.Single(report.Counts, c => c.Name == "Bilder (PixFix)");
        Assert.Equal(3, images.Expected);
        Assert.Equal(2, images.Imported);
        Assert.Equal(1, images.Missing);
        Assert.Contains(report.Warnings, w => w.Contains("-2.png", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, w => w.Contains("HTML-Fassung", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyDatabaseIsNotModified()
    {
        fixture.WithDefaultData();
        var before = AssetStore.ComputeSha256(fixture.DatabaseFile);

        importer.Import(fixture.ProgramDirectory);

        Assert.Equal(before, AssetStore.ComputeSha256(fixture.DatabaseFile));
    }

    [Fact]
    public void SecondImportIsRefusedAndChangesNothing()
    {
        fixture.WithDefaultData();
        Assert.True(importer.Import(fixture.ProgramDirectory).Succeeded);

        var second = importer.Import(fixture.ProgramDirectory);

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Contains("bereits Daten", StringComparison.Ordinal));
        using var db = database.Open();
        Assert.Equal(2, db.Count("calls"));
    }

    [Fact]
    public void UnsupportedSchemaVersionImportsNothing()
    {
        fixture.WithDefaultData(schemaVersion: 2);

        var report = importer.Import(fixture.ProgramDirectory);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Errors, e => e.Contains("Schemaversion 2", StringComparison.Ordinal));
        using var db = database.Open();
        Assert.True(PixFixDatabase.HasNoUserData(db));
    }

    [Fact]
    public void DataFolderCanBeSelectedDirectly()
    {
        fixture.WithDefaultData();

        var report = importer.Import(fixture.DataDirectory);

        Assert.True(report.Succeeded, report.ToText());
    }

    [Fact]
    public void WrongFolderGivesClearError()
    {
        var report = importer.Import(fixture.Root);

        Assert.False(report.Succeeded);
        Assert.Contains(report.Errors, e => e.Contains("keine PixFix-69-Datenbank", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenReferenceRollsBackEverything()
    {
        fixture.WithDefaultData();
        using (var legacy = fixture.OpenLegacy())
        {
            LegacyFixture.Exec(legacy, "INSERT INTO information_entries(id,workspace_id,section,title,source_hash) VALUES('i9','gibt-es-nicht','X','Verwaist','h')");
        }

        SqliteConnection.ClearAllPools();
        var report = importer.Import(fixture.ProgramDirectory);

        Assert.False(report.Succeeded);
        using var db = database.Open();
        Assert.True(PixFixDatabase.HasNoUserData(db));
    }
}
