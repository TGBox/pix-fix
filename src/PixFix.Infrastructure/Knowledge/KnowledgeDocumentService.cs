using System.Text;
using PixFix.Core.Common;
using PixFix.Core.Documents;
using PixFix.Infrastructure.Documents;

namespace PixFix.Infrastructure.Knowledge;

/// <summary>Anzeigefertiger Inhalt: RTF für den WPF-Editor oder Klartext.</summary>
public sealed record KnowledgeDocument(string Format, string Text);

/// <summary>
/// Bereitet Einträge zur Anzeige auf. Übernommene PixFix-69-Dokumente werden einmal umgeschrieben
/// (Metafile-Bilder → PNG, Marker entfernt) und im Cache abgelegt; das Original in der Ablage bleibt unverändert.
/// </summary>
public sealed class KnowledgeDocumentService(KnowledgeRepository repository, AppPaths paths)
{
    /// <summary>Bei Änderungen an der Aufbereitung erhöhen – alte Cache-Dateien werden dann nicht mehr verwendet.</summary>
    private const string CacheVersion = "v1";

    public KnowledgeDocument? Load(string entryId)
    {
        var content = repository.LoadContent(entryId);
        if (content is null)
        {
            return null;
        }

        return content.Format switch
        {
            ContentFormat.Rtf => new KnowledgeDocument(ContentFormat.Rtf, LoadRtf(content)),
            ContentFormat.PlainText => new KnowledgeDocument(ContentFormat.PlainText, content.InlineContent ?? content.PlainText),
            // HTML gibt es nur bei Einträgen ohne RTF-Dokument; angezeigt wird der Klartext.
            _ => new KnowledgeDocument(ContentFormat.PlainText, content.PlainText),
        };
    }

    private string LoadRtf(KnowledgeContent content)
    {
        string? cacheFile = null;
        if (content.ContentAssetId is not null)
        {
            cacheFile = Path.Combine(paths.CacheDirectory, "documents", CacheVersion + "-" + content.ContentAssetId + ".rtf");
            if (File.Exists(cacheFile))
            {
                return File.ReadAllText(cacheFile, Encoding.Latin1);
            }
        }

        // RTF ist 7-Bit-Text mit Escapes; Latin1 bildet Bytes 1:1 auf Zeichen ab.
        var original = content.InlineContent
            ?? (content.ContentFile is not null && File.Exists(content.ContentFile)
                ? File.ReadAllText(content.ContentFile, Encoding.Latin1)
                : string.Empty);

        var rewritten = RtfPictureRewriter.Rewrite(
            original,
            picture => LegacyPictureConverter.Convert(
                picture,
                legacyPath => content.ImageFilesByLegacyPath.TryGetValue(legacyPath, out var file) ? file : null));

        if (cacheFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            var temp = cacheFile + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, rewritten, Encoding.Latin1);
            File.Move(temp, cacheFile, overwrite: true);
        }

        return rewritten;
    }
}
