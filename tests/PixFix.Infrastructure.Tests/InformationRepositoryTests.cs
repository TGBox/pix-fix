using PixFix.Core.Common;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Workspaces;

namespace PixFix.Infrastructure.Tests;

public sealed class InformationRepositoryTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly PixFixDatabase database;
    private readonly InformationRepository repository;

    public InformationRepositoryTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        var assets = new AssetStore(paths);
        fixture.WithDefaultData();
        Assert.True(new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);
        repository = new InformationRepository(database, assets);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void LoadsImportedWorkspaceEntriesAndAttachments()
    {
        var workspace = Assert.Single(repository.LoadWorkspaces());
        Assert.Equal("Werkzeuge", workspace.Title);

        var entries = repository.LoadEntries(workspace.Id);
        Assert.Equal(new[] { "KIM öffnen", "Nur Text" }, entries.Select(e => e.Title));
        Assert.All(entries, e => Assert.Equal("KIM", e.Section));

        Assert.Equal("rtf", repository.LoadDocument("i1")!.Format);
        Assert.Equal("text", repository.LoadDocument("i2")!.Format);
        Assert.Equal(2, repository.LoadCopyActions("i1").Count);
        var pdf = Assert.Single(repository.LoadAttachments("i1"));
        Assert.Equal("Anleitung.pdf", pdf.DisplayName);
        Assert.True(File.Exists(pdf.File));
    }

    [Fact]
    public void CreateSaveAndDeleteEntry()
    {
        var id = repository.CreateEntry("werkzeuge", "Neuer Abschnitt", "Reiter 1");
        var image = repository.StoreImage([1, 2, 3], ".png");

        repository.SaveContent(id, "<FlowDocument />", "Inhalt", [image]);

        var entry = Assert.Single(repository.LoadEntries("werkzeuge"), e => e.Id == id);
        Assert.Equal("Inhalt", entry.PlainText);
        Assert.True(entry.SectionOrder > 0, "Neuer Abschnitt kommt ans Ende.");
        Assert.Equal("xaml", repository.LoadDocument(id)!.Format);

        repository.DeleteEntry(id);

        Assert.DoesNotContain(repository.LoadEntries("werkzeuge"), e => e.Id == id);
        using var db = database.Open();
        Assert.Equal(1L, db.Scalar("SELECT COUNT(*) FROM trash WHERE kind = 'information';"));
    }

    [Fact]
    public void DeletedEntryCanBeRestoredWithCopyActionsAttachmentsAndImages()
    {
        var image = repository.StoreImage([4, 5, 6], ".png");
        repository.SaveContent("i1", $"<FlowDocument><Paragraph><Image Tag=\"pixfix-asset:{image}\" /></Paragraph></FlowDocument>", "KIM", [image]);

        var trashId = repository.DeleteEntry("i1");
        Assert.DoesNotContain(repository.LoadEntries("werkzeuge"), e => e.Id == "i1");
        var trashed = Assert.Single(repository.LoadTrash());
        Assert.Equal("KIM öffnen", trashed.Title);
        Assert.Equal("Werkzeuge", trashed.Workspace);

        var restored = repository.RestoreEntry(trashId);

        Assert.Equal("i1", restored);
        var entry = Assert.Single(repository.LoadEntries("werkzeuge"), e => e.Id == "i1");
        Assert.Equal("KIM", entry.Section);
        Assert.Equal("KIM öffnen", entry.Title);
        Assert.Equal(2, repository.LoadCopyActions("i1").Count);
        Assert.Single(repository.LoadAttachments("i1"));
        using (var db = database.Open())
        {
            Assert.Equal(image, db.Scalar("SELECT asset_id FROM information_entry_images WHERE entry_id = 'i1';"));
        }

        Assert.Empty(repository.LoadTrash());
        Assert.Throws<InvalidOperationException>(() => repository.RestoreEntry(trashId));
    }

    [Fact]
    public void TrashEntryCanBeDeletedPermanently()
    {
        var trashId = repository.DeleteEntry("i2");
        repository.DeleteFromTrash(trashId);
        Assert.Empty(repository.LoadTrash());
    }

    [Fact]
    public void ProtectedEntryOrWorkspaceBlocksChanges()
    {
        repository.SetProtected("i1", true);
        Assert.Throws<InvalidOperationException>(() => repository.SaveContent("i1", "<FlowDocument />", "x", []));
        Assert.Throws<InvalidOperationException>(() => repository.DeleteEntry("i1"));
        repository.SetProtected("i1", false);

        var workspace = repository.LoadWorkspaces()[0];
        repository.UpdateWorkspace(workspace.Id, workspace.Title, workspace.Icon, isProtected: true);
        Assert.Throws<InvalidOperationException>(() => repository.UpdateEntry("i2", "neu", "KIM"));
    }

    [Fact]
    public void SectionsCollapseAndMove()
    {
        repository.CreateEntry("werkzeuge", "Zweiter Abschnitt", "X");

        repository.SetSectionCollapsed("werkzeuge", "KIM", false);
        repository.MoveSection("werkzeuge", "Zweiter Abschnitt", -1);

        var entries = repository.LoadEntries("werkzeuge");
        Assert.Equal("Zweiter Abschnitt", entries[0].Section);
        Assert.All(entries.Where(e => e.Section == "KIM"), e => Assert.False(e.SectionCollapsed));
    }

    [Fact]
    public void EntriesMoveWithinSection()
    {
        repository.MoveEntry("i2", -1);

        Assert.Equal(new[] { "Nur Text", "KIM öffnen" }, repository.LoadEntries("werkzeuge").Select(e => e.Title));
    }

    [Fact]
    public void CopyActionsAndAttachmentsCanBeChanged()
    {
        Assert.True(repository.AddCopyAction("i2", new CopyAction("ipconfig", 0)));
        Assert.False(repository.AddCopyAction("i2", new CopyAction("ipconfig", 0)));
        repository.DeleteCopyAction("i1", "ping 8.8.8.8");
        Assert.Single(repository.LoadCopyActions("i1"));

        var file = Path.Combine(fixture.Root, "neu.pdf");
        File.WriteAllText(file, "%PDF-1.4 test");
        repository.AddAttachment("i2", file);
        var attachment = Assert.Single(repository.LoadAttachments("i2"));
        Assert.Equal("neu.pdf", attachment.DisplayName);

        repository.RemoveAttachment("i2", attachment.Ordinal);
        Assert.Empty(repository.LoadAttachments("i2"));
    }

    [Fact]
    public void WorkspacesCanBeCreatedMovedAndDeletedWhenEmpty()
    {
        var id = repository.CreateWorkspace("Notizen", "📝");
        repository.MoveWorkspace(id, -1);

        Assert.Equal(new[] { "Notizen", "Werkzeuge" }, repository.LoadWorkspaces().Select(w => w.Title));
        Assert.Throws<InvalidOperationException>(() => repository.DeleteWorkspace("werkzeuge"));

        repository.DeleteWorkspace(id);
        Assert.Single(repository.LoadWorkspaces());
    }
}
