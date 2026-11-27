using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Tests;

public sealed class KnowledgeRepositoryTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly PixFixDatabase database;
    private readonly KnowledgeRepository repository;
    private readonly KnowledgeDocumentService documents;

    public KnowledgeRepositoryTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        var assets = new AssetStore(paths);
        fixture.WithDefaultData();
        Assert.True(new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);
        repository = new KnowledgeRepository(database, assets);
        documents = new KnowledgeDocumentService(repository, paths);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void CreateEntryAddsSectionWhenNeeded()
    {
        var id = repository.CreateEntry("Neuer Abschnitt", "Test");

        var entry = Assert.Single(repository.LoadAll(), e => e.Id == id);
        Assert.Equal("Neuer Abschnitt", entry.SectionName);
        Assert.Contains("Neuer Abschnitt", repository.LoadSectionNames());
        Assert.Equal("xaml", documents.Load(id)!.Format);
    }

    [Fact]
    public void SaveContentSwitchesToXamlAndKeepsImageOcr()
    {
        var before = repository.LoadContent(LegacyFixture.EntryA)!;
        var keptImage = before.Images[0].AssetId;
        repository.AddImageOcr(LegacyFixture.EntryA, keptImage, "Fehler 4711");
        var newImage = repository.StoreImage([9, 8, 7], ".png");

        repository.SaveContent(LegacyFixture.EntryA, new KnowledgeContentUpdate("<FlowDocument />", "neuer Text", [keptImage, newImage]));

        var after = repository.LoadContent(LegacyFixture.EntryA)!;
        Assert.Equal("xaml", after.Format);
        Assert.Equal("<FlowDocument />", after.InlineContent);
        Assert.Null(after.ContentAssetId);
        Assert.Equal("neuer Text", after.PlainText);
        Assert.Equal(new[] { keptImage, newImage }, after.Images.Select(i => i.AssetId));
        Assert.Equal("Fehler 4711", after.Images[0].OcrText);
        Assert.Equal(string.Empty, after.Images[1].OcrText);
        Assert.Equal(newImage, Assert.Single(repository.ImagesWithoutOcr(LegacyFixture.EntryA)).AssetId);
    }

    [Fact]
    public void AddImageOcrAppendsToSearchableText()
    {
        var image = repository.LoadContent(LegacyFixture.EntryA)!.Images[0].AssetId;

        repository.AddImageOcr(LegacyFixture.EntryA, image, "Konnektor nicht erreichbar");
        repository.AddImageOcr(LegacyFixture.EntryA, image, "zweites Mal wird ignoriert");

        var entry = Assert.Single(repository.LoadAll(), e => e.Id == LegacyFixture.EntryA);
        Assert.Equal("OCR A Konnektor nicht erreichbar", entry.OcrText);
    }

    [Fact]
    public void ProtectedEntryCannotBeChangedOrDeleted()
    {
        repository.SetFlags(LegacyFixture.EntryA, favorite: true, isProtected: true, archived: false);

        Assert.Throws<InvalidOperationException>(() =>
            repository.SaveContent(LegacyFixture.EntryA, new KnowledgeContentUpdate("<FlowDocument />", "x", [])));
        Assert.Throws<InvalidOperationException>(() => repository.UpdateMetadata(LegacyFixture.EntryA, "x", "y", "z"));
        Assert.Throws<InvalidOperationException>(() => repository.MoveToTrash(LegacyFixture.EntryA));
    }

    [Fact]
    public void TrashAndRestoreKeepImagesAndCopyActions()
    {
        var imagesBefore = repository.LoadContent(LegacyFixture.EntryA)!.Images.Select(i => i.AssetId).ToList();

        repository.MoveToTrash(LegacyFixture.EntryA);

        Assert.DoesNotContain(repository.LoadAll(), e => e.Id == LegacyFixture.EntryA);
        var trashed = Assert.Single(repository.LoadTrash());
        Assert.Equal("Drucker", trashed.Title);

        var restoredId = repository.Restore(trashed.TrashId);

        Assert.Equal(LegacyFixture.EntryA, restoredId);
        Assert.Empty(repository.LoadTrash());
        Assert.Equal(imagesBefore, repository.LoadContent(restoredId)!.Images.Select(i => i.AssetId));
        using var db = database.Open();
        Assert.Equal(1L, db.Scalar($"SELECT COUNT(*) FROM copy_actions WHERE owner_id = '{restoredId}';"));
        Assert.Equal("rtf", documents.Load(restoredId)!.Format);
    }

    [Fact]
    public void LegacyTrashSnapshotCanBeRestored()
    {
        using (var db = database.Open())
        {
            db.Execute(
                "INSERT INTO trash(id, kind, original_id, deleted_utc, snapshot_json) VALUES('t1', 'knowledge', 'old', '2026-10-01T00:00:00.000Z', $json);",
                null,
                ("$json", """{"Format":"pixfix69-FixEntry","Values":{"Id":"old","Title":"Alter Eintrag","Category":"Archiv alt","Solution":"Lösungstext","Ocr":"ocr"}}"""));
        }

        var trashed = Assert.Single(repository.LoadTrash());
        Assert.Equal("Alter Eintrag", trashed.Title);

        var id = repository.Restore("t1");

        var entry = Assert.Single(repository.LoadAll(), e => e.Id == id);
        Assert.Equal("Archiv alt", entry.SectionName);
        var document = documents.Load(id)!;
        Assert.Equal("text", document.Format);
        Assert.Equal("Lösungstext", document.Text);
    }

    [Fact]
    public void DeletePermanentlyRemovesTrashItem()
    {
        repository.MoveToTrash(LegacyFixture.EntryA);
        var trashed = Assert.Single(repository.LoadTrash());

        repository.DeletePermanently(trashed.TrashId);

        Assert.Empty(repository.LoadTrash());
    }

    [Fact]
    public void LegacyDocumentReportsAssetIdsPerPicture()
    {
        var content = repository.LoadContent(LegacyFixture.EntryA)!;
        var markerImage = content.Images.Single(i => i.LegacyPath!.EndsWith("-1.png", StringComparison.Ordinal)).AssetId;

        var document = documents.Load(LegacyFixture.EntryA)!;
        var cached = documents.Load(LegacyFixture.EntryA)!;

        Assert.Equal(new[] { markerImage, null }, document.PictureAssetIds);
        Assert.Equal(document.PictureAssetIds, cached.PictureAssetIds);
    }

    [Fact]
    public void CopyActionsCanBeAddedAndDeleted()
    {
        Assert.Single(repository.LoadCopyActions(LegacyFixture.EntryA));

        Assert.True(repository.AddCopyAction(LegacyFixture.EntryA, new Core.Common.CopyAction("ping 8.8.8.8", 0)));
        Assert.False(repository.AddCopyAction(LegacyFixture.EntryA, new Core.Common.CopyAction("ping 8.8.8.8", 1)));
        Assert.Equal(new[] { "ipconfig /all", "ping 8.8.8.8" }, repository.LoadCopyActions(LegacyFixture.EntryA).Select(a => a.Text));

        repository.DeleteCopyAction(LegacyFixture.EntryA, "ipconfig /all");

        Assert.Equal("ping 8.8.8.8", Assert.Single(repository.LoadCopyActions(LegacyFixture.EntryA)).Text);
    }

    [Fact]
    public void SectionsCanBeRenamedMovedAndDeletedWhenEmpty()
    {
        var sections = repository.LoadSections();
        Assert.Equal(new[] { "Allgemeines", "Fehlermeldungen" }, sections.Select(s => s.Name));
        var errors = sections[1];

        repository.RenameSection(errors.Id, "Fehlerbilder");
        repository.MoveSection(errors.Id, -1);

        Assert.Equal(new[] { "Fehlerbilder", "Allgemeines" }, repository.LoadSections().Select(s => s.Name));
        Assert.Throws<InvalidOperationException>(() => repository.RenameSection(errors.Id, "Allgemeines"));
        Assert.Throws<InvalidOperationException>(() => repository.DeleteSection(errors.Id));

        var empty = repository.LoadSections().Single(s => s.Name == "Fehlerbilder");
        repository.MoveToTrash(LegacyFixture.EntryA);
        repository.DeleteSection(empty.Id);
        Assert.DoesNotContain(repository.LoadSections(), s => s.Id == empty.Id);
    }

    [Fact]
    public void EntriesCanBeMovedWithinSection()
    {
        var first = repository.CreateEntry("Fehlermeldungen", "Zweiter");
        var order = () => repository.LoadAll().Where(e => e.SectionName == "Fehlermeldungen").OrderBy(e => e.SortOrder).Select(e => e.Title).ToList();
        Assert.Equal(new[] { "Drucker", "Zweiter" }, order());

        repository.MoveEntry(first, -1);
        Assert.Equal(new[] { "Zweiter", "Drucker" }, order());

        repository.MoveEntry(first, -1); // schon oben: keine Änderung
        Assert.Equal(new[] { "Zweiter", "Drucker" }, order());
    }

    [Fact]
    public void EntryTemplatesAreAvailable()
    {
        var templates = new Templates.TemplateRepository(database).LoadByKind(Core.Templates.TemplateKind.Entry);

        Assert.Equal("Störung", Assert.Single(templates).Name);
    }
}
