using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Xml.Linq;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Images;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Ocr;

namespace PixFix.Infrastructure.Tests;

public sealed class ImageSearchTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly KnowledgeRepository repository;
    private readonly ImageSearchService search;

    public ImageSearchTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        var database = new PixFixDatabase(paths);
        var assets = new AssetStore(paths);
        fixture.WithDefaultData();
        Assert.True(new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);
        repository = new KnowledgeRepository(database, assets);
        search = new ImageSearchService(database, assets, repository, new OcrService());
    }

    public void Dispose() => fixture.Dispose();

    /// <summary>Testbild: Fläche mit Rechteck an wechselnder Stelle, damit sich die Bilder deutlich unterscheiden.</summary>
    private static byte[] DrawPng(Color background, Color box, int boxX)
    {
        using var bitmap = new Bitmap(200, 120);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(background);
            using var brush = new SolidBrush(box);
            g.FillRectangle(brush, boxX, 20, 60, 80);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private string AddEntryWithImage(string title, byte[] png)
    {
        var id = repository.CreateEntry("Bilder", title);
        var asset = repository.StoreImage(png, ".png");
        repository.SaveContent(id, new KnowledgeContentUpdate("<FlowDocument />", title, [asset]));
        return id;
    }

    [Fact]
    public async Task SameImageIsFoundFirst()
    {
        var white = DrawPng(Color.White, Color.DarkBlue, 10);
        var black = DrawPng(Color.Black, Color.Yellow, 120);
        var target = AddEntryWithImage("Ziel", white);
        AddEntryWithImage("Anderes", black);
        var queryFile = Path.Combine(fixture.Root, "query.png");
        File.WriteAllBytes(queryFile, white);

        var result = await search.SearchAsync(queryFile);

        var first = result.Hits[0];
        Assert.Equal(target, first.Hit.Entry.Id);
        Assert.Equal(100, first.Hit.Visual);
        Assert.True(File.Exists(first.ImageFile));
    }

    [Fact]
    public async Task WarmUpStoresSignaturesOnce()
    {
        AddEntryWithImage("A", DrawPng(Color.White, Color.Red, 30));

        var firstRun = await search.WarmUpAsync();
        var secondRun = await search.WarmUpAsync();

        // Nur das neue PNG ist lesbar; die Platzhalterbilder aus dem Testbestand werden übersprungen.
        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);
    }

    /// <summary>
    /// Vergleich mit PixFix 69: berechnet die Signaturen aller Bilder neu und vergleicht sie mit dem Bildindex von PixFix 69
    /// (<c>search-index/image-index.xml</c>). Läuft nur, wenn <c>PIXFIX_LEGACY_DATA</c> auf einen <c>PixFix-Daten</c>-Ordner zeigt.
    /// </summary>
    [Fact]
    public void SignaturesMatchPixFix69Index()
    {
        var legacyData = Environment.GetEnvironmentVariable("PIXFIX_LEGACY_DATA");
        if (string.IsNullOrWhiteSpace(legacyData))
        {
            return;
        }

        var index = XDocument.Load(Path.Combine(legacyData, "search-index", "image-index.xml"));
        var mismatches = new List<string>();
        var compared = 0;
        foreach (var record in index.Descendants("ImageIndexRecord"))
        {
            var relative = record.Element("Relative")!.Value;
            var file = Path.Combine(legacyData, relative);
            if (!File.Exists(file) || new FileInfo(file).Length != long.Parse(record.Element("Length")!.Value, CultureInfo.InvariantCulture))
            {
                continue; // Indexeintrag veraltet
            }

            var actual = ImageSignatureBuilder.Build(file);
            Compare(relative, "Full", actual.Full, record, string.Empty, mismatches);
            Compare(relative, "Center", actual.Center, record, "Center", mismatches);
            compared++;
        }

        Assert.True(compared > 0, "Keine Bilder verglichen.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} Abweichungen bei {compared} Bildern:\n" + string.Join('\n', mismatches.Take(20)));
    }

    private static void Compare(string relative, string label, Core.Images.ImageSignature actual, XElement record, string prefix, List<string> mismatches)
    {
        var average = ulong.Parse(record.Element(prefix + "AverageHash")!.Value, CultureInfo.InvariantCulture);
        var difference = ulong.Parse(record.Element(prefix + "DifferenceHash")!.Value, CultureInfo.InvariantCulture);
        var pixels = Convert.FromBase64String(record.Element(prefix + "Pixels")!.Value);
        var histogram = record.Element(prefix + "Histogram")!.Elements("int").Select(e => int.Parse(e.Value, CultureInfo.InvariantCulture)).ToArray();
        var aspect = double.Parse(record.Element(prefix + "Aspect")!.Value, CultureInfo.InvariantCulture);

        if (actual.AverageHash != average || actual.DifferenceHash != difference || !actual.Pixels.SequenceEqual(pixels)
            || !actual.Histogram.SequenceEqual(histogram) || Math.Abs(actual.Aspect - aspect) > 1e-9)
        {
            mismatches.Add($"{relative} ({label})");
        }
    }
}
