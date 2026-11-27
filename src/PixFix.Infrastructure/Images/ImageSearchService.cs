using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using PixFix.Core.Images;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Ocr;

namespace PixFix.Infrastructure.Images;

/// <summary>Ergebnis einer Bildsuche; <see cref="RecognizedText"/> ist der per OCR erkannte Text des Suchbilds.</summary>
public sealed record ImageSearchResult(string RecognizedText, IReadOnlyList<ImageSearchResultHit> Hits);

public sealed record ImageSearchResultHit(ImageSearchHit Hit, string ImageFile);

/// <summary>
/// Bildsuche (SPEC F2): Signatur und OCR des Suchbilds gegen alle Bilder der Wissensdatenbank.
/// Die Signaturen der gespeicherten Bilder liegen als Index in <c>image_signatures</c> und werden bei Bedarf ergänzt.
/// </summary>
public sealed class ImageSearchService(PixFixDatabase database, AssetStore assets, KnowledgeRepository knowledge, OcrService ocr)
{
    /// <summary>Wie PixFix 69: höchstens 4 Bilder gleichzeitig berechnen.</summary>
    private static readonly ParallelOptions Parallelism = new() { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount)) };

    private readonly SemaphoreSlim indexLock = new(1, 1);

    public async Task<ImageSearchResult> SearchAsync(string queryFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryFile);

        string recognized;
        try
        {
            recognized = await ocr.RecognizeAsync(queryFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            // Ohne Bildtext zählt nur die visuelle Ähnlichkeit. (PixFix 69 hat hier den Fehlertext als Suchtext verwendet.)
            recognized = string.Empty;
        }

        var query = ImageSignatureBuilder.Build(queryFile);
        var images = LoadImages();
        var signatures = await EnsureSignaturesAsync(images.Select(i => (i.AssetId, i.File)).Distinct().ToList(), cancellationToken).ConfigureAwait(false);

        var entries = knowledge.LoadAll().ToDictionary(e => e.Id, StringComparer.Ordinal);
        var candidates = new List<ImageCandidate>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (entries.TryGetValue(image.EntryId, out var entry) && signatures.TryGetValue(image.AssetId, out var signature))
            {
                candidates.Add(new ImageCandidate(entry, image.AssetId, signature));
                files[image.AssetId] = image.File;
            }
        }

        var hits = ImageSearch.Rank(candidates, query, recognized)
            .Select(h => new ImageSearchResultHit(h, files[h.AssetId]))
            .ToList();
        return new ImageSearchResult(recognized, hits);
    }

    /// <summary>Berechnet fehlende Signaturen vorab (z. B. nach dem Start oder einem Import). Liefert die Anzahl neuer Signaturen.</summary>
    public async Task<int> WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var images = LoadImages().Select(i => (i.AssetId, i.File)).Distinct().ToList();
        var before = CountSignatures();
        await EnsureSignaturesAsync(images, cancellationToken).ConfigureAwait(false);
        return CountSignatures() - before;
    }

    private sealed record StoredImage(string EntryId, string AssetId, string File);

    /// <summary>Alle Bilder aller Einträge – wie PixFix 69 einschließlich archivierter Einträge.</summary>
    private List<StoredImage> LoadImages()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            """
            SELECT i.entry_id, i.asset_id, a.relative_path
            FROM knowledge_entry_images i JOIN assets a ON a.id = i.asset_id
            ORDER BY i.entry_id, i.ordinal;
            """,
            null);
        using var reader = command.ExecuteReader();
        var result = new List<StoredImage>();
        while (reader.Read())
        {
            var file = assets.GetFullPath(reader.GetText("relative_path"));
            if (File.Exists(file))
            {
                result.Add(new StoredImage(reader.GetText("entry_id"), reader.GetText("asset_id"), file));
            }
        }

        return result;
    }

    private int CountSignatures()
    {
        using var db = database.Open();
        return (int)db.Count("image_signatures");
    }

    private async Task<Dictionary<string, ImageSignaturePair>> EnsureSignaturesAsync(
        IReadOnlyList<(string AssetId, string File)> images, CancellationToken cancellationToken)
    {
        await indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var known = LoadSignatures();
            var missing = images.Where(i => !known.ContainsKey(i.AssetId)).ToList();
            if (missing.Count == 0)
            {
                return known;
            }

            var built = new ConcurrentDictionary<string, ImageSignaturePair>(StringComparer.Ordinal);
            await Task.Run(
                () => Parallel.ForEach(missing, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Parallelism.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken,
                }, image =>
                {
                    try
                    {
                        built[image.AssetId] = ImageSignatureBuilder.Build(image.File);
                    }
                    catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException or IOException)
                    {
                        // Wie PixFix 69: Bilder, die GDI+ nicht lesen kann, werden bei der Bildsuche übersprungen.
                    }
                }),
                cancellationToken).ConfigureAwait(false);

            SaveSignatures(built);
            foreach (var (assetId, signature) in built)
            {
                known[assetId] = signature;
            }

            return known;
        }
        finally
        {
            indexLock.Release();
        }
    }

    private Dictionary<string, ImageSignaturePair> LoadSignatures()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT * FROM image_signatures WHERE algorithm = $algorithm;", null, ("$algorithm", ImageSignatureBuilder.Algorithm));
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, ImageSignaturePair>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result[reader.GetText("asset_id")] = new ImageSignaturePair(Read(reader, "full"), Read(reader, "center"));
        }

        return result;
    }

    private static ImageSignature Read(SqliteDataReader reader, string prefix)
    {
        var pixels = (byte[])reader[prefix + "_pixels"];
        var histogramBytes = (byte[])reader[prefix + "_histogram"];
        var histogram = MemoryMarshal.Cast<byte, int>(histogramBytes.AsSpan()).ToArray();
        return new ImageSignature(
            unchecked((ulong)reader.GetInteger(prefix + "_average")),
            unchecked((ulong)reader.GetInteger(prefix + "_difference")),
            pixels,
            histogram,
            reader.GetDouble(reader.GetOrdinal(prefix + "_aspect")));
    }

    private void SaveSignatures(IReadOnlyDictionary<string, ImageSignaturePair> signatures)
    {
        if (signatures.Count == 0)
        {
            return;
        }

        using var db = database.Open();
        using var tx = db.BeginTransaction();
        foreach (var (assetId, pair) in signatures)
        {
            db.Execute(
                """
                INSERT INTO image_signatures(asset_id, algorithm, full_average, full_difference, full_pixels, full_histogram, full_aspect,
                    center_average, center_difference, center_pixels, center_histogram, center_aspect)
                VALUES($id, $algorithm, $fa, $fd, $fp, $fh, $fx, $ca, $cd, $cp, $ch, $cx)
                ON CONFLICT(asset_id) DO UPDATE SET
                    algorithm = excluded.algorithm,
                    full_average = excluded.full_average, full_difference = excluded.full_difference,
                    full_pixels = excluded.full_pixels, full_histogram = excluded.full_histogram, full_aspect = excluded.full_aspect,
                    center_average = excluded.center_average, center_difference = excluded.center_difference,
                    center_pixels = excluded.center_pixels, center_histogram = excluded.center_histogram, center_aspect = excluded.center_aspect;
                """,
                tx,
                ("$id", assetId),
                ("$algorithm", ImageSignatureBuilder.Algorithm),
                ("$fa", unchecked((long)pair.Full.AverageHash)),
                ("$fd", unchecked((long)pair.Full.DifferenceHash)),
                ("$fp", pair.Full.Pixels),
                ("$fh", MemoryMarshal.AsBytes(pair.Full.Histogram.AsSpan()).ToArray()),
                ("$fx", pair.Full.Aspect),
                ("$ca", unchecked((long)pair.Center.AverageHash)),
                ("$cd", unchecked((long)pair.Center.DifferenceHash)),
                ("$cp", pair.Center.Pixels),
                ("$ch", MemoryMarshal.AsBytes(pair.Center.Histogram.AsSpan()).ToArray()),
                ("$cx", pair.Center.Aspect));
        }

        tx.Commit();
    }
}
