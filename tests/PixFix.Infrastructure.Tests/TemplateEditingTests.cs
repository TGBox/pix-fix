using PixFix.Core.Templates;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Templates;

namespace PixFix.Infrastructure.Tests;

/// <summary>Pflege von Vorlagen, Cobra-Schnelltexten und Fax-Dokumenten (Phase 5b).</summary>
public sealed class TemplateEditingTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly PixFixDatabase database;
    private readonly AssetStore assets;

    public TemplateEditingTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        assets = new AssetStore(paths);
        fixture.WithDefaultData();
        Assert.True(new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void TemplatesCanBeCreatedEditedMovedAndDeleted()
    {
        var templates = new TemplateRepository(database);
        var imported = Assert.Single(templates.LoadByKind(TemplateKind.Email));

        var created = templates.Create(TemplateKind.Email, "Rückruf");
        templates.Save(created with { Subject = "Ihr Anruf {LNUM}", Body = "Hallo" });
        Assert.Equal(new[] { imported.Id, created.Id }, templates.LoadByKind(TemplateKind.Email).Select(t => t.Id));

        Assert.True(templates.Move(created.Id, -1));
        Assert.False(templates.Move(created.Id, -1));
        var reordered = templates.LoadByKind(TemplateKind.Email);
        Assert.Equal(new[] { created.Id, imported.Id }, reordered.Select(t => t.Id));
        Assert.Equal("Ihr Anruf {LNUM}", reordered[0].Subject);

        // Andere Arten bleiben unberührt.
        Assert.Single(templates.LoadByKind(TemplateKind.CallReason));

        templates.Delete(created.Id);
        Assert.Equal(imported.Id, Assert.Single(templates.LoadByKind(TemplateKind.Email)).Id);
    }

    [Fact]
    public void CobraQuickTextsCanBeEdited()
    {
        var repository = new CobraQuickTextRepository(database);
        var imported = Assert.Single(repository.LoadAll());

        var created = repository.Create("Erledigt");
        repository.Save(created with { CobraText = "Problem gelöst", CompleteCall = true });
        Assert.True(repository.Move(created.Id, -1));

        var all = repository.LoadAll();
        Assert.Equal(new[] { created.Id, imported.Id }, all.Select(q => q.Id));
        Assert.Equal("Problem gelöst", all[0].CobraText);
        Assert.True(all[0].CompleteCall);
        Assert.False(all[0].ScheduleRecall);

        repository.Delete(created.Id);
        Assert.Single(repository.LoadAll());
    }

    [Fact]
    public void FaxDocumentsCanBeAddedRenamedAndMoved()
    {
        var repository = new FaxDocumentRepository(database, assets);
        var imported = Assert.Single(repository.LoadAll());
        var file = Path.Combine(fixture.TargetDirectory, "Fax Rückruf.docx");
        File.WriteAllBytes(file, [1, 2, 3, 4]);

        repository.Add(file);
        var added = repository.LoadAll()[1];
        Assert.Equal("Fax Rückruf.docx", added.Name);
        Assert.True(File.Exists(added.File));

        repository.Rename(added.Id, "  Fax Rückruf  ");
        Assert.True(repository.Move(added.Id, -1));
        var all = repository.LoadAll();
        Assert.Equal(new[] { added.Id, imported.Id }, all.Select(d => d.Id));
        Assert.Equal("Fax Rückruf", all[0].Name);
    }
}
