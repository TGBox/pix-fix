using System.Text;
using PixFix.Core.Common;
using PixFix.Core.Documents;
using PixFix.Infrastructure.Documents;

namespace PixFix.Infrastructure.Knowledge;

/// <summary>Anzeigefertiger Inhalt.</summary>
/// <param name="Format"><c>xaml</c> (eigenes Format), <c>rtf</c> (übernommen aus PixFix 69) oder <c>text</c>.</param>
/// <param name="Text">Der Inhalt im jeweiligen Format.</param>
/// <param name="PictureAssetIds">
/// Nur bei RTF: Asset-Id je Bild in Dokumentreihenfolge (<c>null</c> = Bild ohne Originaldatei). Damit werden beim ersten
/// Speichern die Originaldateien weiterverwendet statt neu kodiert.
/// </param>
public sealed record KnowledgeDocument(string Format, string Text, IReadOnlyList<string?> PictureAssetIds);

/// <summary>
/// Bereitet Einträge zur Anzeige auf. Übernommene PixFix-69-Dokumente werden einmal umgeschrieben
/// (Metafile-Bilder → PNG, Marker entfernt) und im Cache abgelegt; das Original in der Ablage bleibt unverändert.
/// </summary>
public sealed class KnowledgeDocumentService(KnowledgeRepository repository, AppPaths paths)
{
    /// <summary>Bei Änderungen an der Aufbereitung erhöhen – alte Cache-Dateien werden dann nicht mehr verwendet.</summary>
    private const string CacheVersion = "v2";

    /// <summary>Platzhalter in der Id-Liste für Bilder ohne Originaldatei.</summary>
    private const string NoAsset = "-";

    public KnowledgeDocument? Load(string entryId)
    {
        var content = repository.LoadContent(entryId);
        if (content is null)
        {
            return null;
        }

        return content.Format switch
        {
            ContentFormat.Xaml => new KnowledgeDocument(ContentFormat.Xaml, content.InlineContent ?? string.Empty, []),
            ContentFormat.Rtf => LoadRtf(content),
            ContentFormat.PlainText => new KnowledgeDocument(ContentFormat.PlainText, content.InlineContent ?? content.PlainText, []),
            // HTML gibt es nur bei Einträgen ohne RTF-Dokument; angezeigt wird der Klartext.
            _ => new KnowledgeDocument(ContentFormat.PlainText, content.PlainText, []),
        };
    }

    private KnowledgeDocument LoadRtf(KnowledgeContent content)
    {
        string? cacheFile = null;
        if (content.ContentAssetId is not null)
        {
            cacheFile = Path.Combine(paths.CacheDirectory, "documents", CacheVersion + "-" + content.ContentAssetId + ".rtf");
            var idsFile = cacheFile + ".ids";
            if (File.Exists(cacheFile) && File.Exists(idsFile))
            {
                var cachedIds = File.ReadAllLines(idsFile).Select(l => l == NoAsset ? null : l).ToList();
                return new KnowledgeDocument(ContentFormat.Rtf, File.ReadAllText(cacheFile, Encoding.Latin1), cachedIds);
            }
        }

        // RTF ist 7-Bit-Text mit Escapes; Latin1 bildet Bytes 1:1 auf Zeichen ab.
        var original = content.InlineContent
            ?? (content.ContentFile is not null && File.Exists(content.ContentFile)
                ? File.ReadAllText(content.ContentFile, Encoding.Latin1)
                : string.Empty);

        var byLegacyPath = new Dictionary<string, KnowledgeImageRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in content.Images.Where(i => i.LegacyPath is not null))
        {
            byLegacyPath.TryAdd(image.LegacyPath!, image);
        }

        var pictureAssetIds = new List<string?>();
        var rewritten = RtfPictureRewriter.Rewrite(original, picture =>
        {
            KnowledgeImageRef? image = null;
            if (picture.LegacyPath is not null)
            {
                byLegacyPath.TryGetValue(picture.LegacyPath, out image);
            }

            pictureAssetIds.Add(image?.AssetId);
            return LegacyPictureConverter.Convert(picture, _ => image?.File);
        });

        if (cacheFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            WriteAtomic(cacheFile + ".ids", string.Join('\n', pictureAssetIds.Select(id => id ?? NoAsset)));
            WriteAtomic(cacheFile, rewritten);
        }

        return new KnowledgeDocument(ContentFormat.Rtf, rewritten, pictureAssetIds);
    }

    private static void WriteAtomic(string file, string text)
    {
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, text, Encoding.Latin1);
        File.Move(temp, file, overwrite: true);
    }
}
