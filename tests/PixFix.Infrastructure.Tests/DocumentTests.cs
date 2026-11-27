using PixFix.Core.Documents;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Documents;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Tests;

public sealed class WmfTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void ExtractsBitmapFromStretchDibRecord()
    {
        var bmp = WmfBitmapExtractor.TryExtractBmp(WmfSample.Create());

        Assert.NotNull(bmp);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(54, BitConverter.ToInt32(bmp, 10));
        Assert.Equal(14 + 44, bmp.Length);
    }

    [Fact]
    public void NoBitmapGivesNull()
    {
        var header = new byte[18 + 6];
        header[2] = 9;
        header[18] = 3;

        Assert.Null(WmfBitmapExtractor.TryExtractBmp(header));
    }

    [Fact]
    public void ConverterTurnsWmfIntoPng()
    {
        var result = LegacyPictureConverter.Convert(new RtfPicture("wmetafile", WmfSample.Create(), 15, 15, null), _ => null);

        Assert.NotNull(result);
        Assert.Equal("pngblip", result.Format);
        Assert.Equal(PngSignature, result.Data.Take(4));
    }

    [Fact]
    public void ConverterKeepsPngAndJpeg()
    {
        Assert.Null(LegacyPictureConverter.Convert(new RtfPicture("pngblip", [1, 2], null, null, null), _ => null));
        Assert.Null(LegacyPictureConverter.Convert(new RtfPicture("jpegblip", [1, 2], null, null, null), _ => null));
    }
}

public sealed class KnowledgeDocumentTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly KnowledgeRepository repository;
    private readonly KnowledgeDocumentService documents;
    private readonly AppPaths paths;

    public KnowledgeDocumentTests()
    {
        paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        var database = new PixFixDatabase(paths);
        var assets = new AssetStore(paths);
        fixture.WithDefaultData();
        var report = new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory);
        Assert.True(report.Succeeded, report.ToText());
        repository = new KnowledgeRepository(database, assets);
        documents = new KnowledgeDocumentService(repository, paths);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void LoadAllReturnsEntriesWithSectionNames()
    {
        var entries = repository.LoadAll();

        Assert.Equal(2, entries.Count);
        var entry = Assert.Single(entries, e => e.Id == LegacyFixture.EntryA);
        Assert.Equal("Fehlermeldungen", entry.SectionName);
        Assert.Equal("Drucker Lösung", entry.PlainText);
        Assert.True(entry.IsFavorite);
    }

    [Fact]
    public void RtfImagesAreReplacedAndMarkersRemoved()
    {
        var document = documents.Load(LegacyFixture.EntryA);

        Assert.NotNull(document);
        Assert.Equal("rtf", document.Format);
        Assert.DoesNotContain("PFIMG", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("wmetafile", document.Text, StringComparison.Ordinal);
        // Bild mit Marker: Originaldatei aus der Ablage (Inhalt 01 02 03 04)
        Assert.Contains("\\pngblip\\picwgoal1500\\pichgoal750\n01020304\n", document.Text, StringComparison.Ordinal);
        // Bild ohne Marker: aus dem Metafile erzeugtes PNG
        Assert.Contains("\\pngblip\\picwgoal100\\pichgoal100\n89504E47", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparedDocumentIsCached()
    {
        var first = documents.Load(LegacyFixture.EntryA);
        var cacheFiles = Directory.GetFiles(Path.Combine(paths.CacheDirectory, "documents"), "*.rtf");

        var second = documents.Load(LegacyFixture.EntryA);

        Assert.Single(cacheFiles);
        Assert.Equal(first!.Text, second!.Text);
    }

    [Fact]
    public void EntryWithoutRtfShowsPlainText()
    {
        var document = documents.Load(LegacyFixture.EntryB);

        Assert.NotNull(document);
        Assert.Equal("text", document.Format);
        Assert.Equal("Text B", document.Text);
    }
}
