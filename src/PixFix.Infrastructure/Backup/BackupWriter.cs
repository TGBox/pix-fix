using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Backup;

/// <summary>Ergebnis einer erstellten Sicherung.</summary>
public sealed record BackupResult(string File, int FileCount, long Length, IReadOnlyDictionary<string, long> Counts);

/// <summary>
/// Komplettsicherung im Format 4.0 (SPEC F11): konsistenter SQLite-Snapshot per <c>VACUUM INTO</c> (ohne Bildindex) plus alle
/// Dateien der Ablage, Manifest mit Länge und SHA-256 je Datei. Das Archiv entsteht als <c>.partial</c>, wird vollständig
/// geprüft und erst dann unter dem endgültigen Namen abgelegt.
/// </summary>
public sealed class BackupWriter(AppPaths paths, PixFixDatabase database, TimeProvider? timeProvider = null)
{
    /// <summary>Tabellen, deren Anzahl im Manifest steht (Anzeige und Abgleich bei der Wiederherstellung).</summary>
    internal static readonly string[] CountedTables =
    [
        "knowledge_entries", "knowledge_entry_images", "workspaces", "information_entries", "information_attachments",
        "calls", "templates", "cobra_quick_texts", "fax_documents", "trash", "assets",
    ];

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public BackupResult Create(string targetFile, IProgress<ImportProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFile);
        var target = Path.GetFullPath(targetFile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var partial = target + ".partial";
        var work = Path.Combine(paths.CacheDirectory, "backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            progress?.Report(new ImportProgress(2, "Datenbank-Snapshot wird erstellt …"));
            var snapshot = Path.Combine(work, BackupManifest.DatabaseEntry);
            using (var db = database.Open())
            {
                db.Execute("VACUUM INTO $path;", null, ("$path", snapshot));
            }

            var (schemaVersion, counts, assets) = PrepareSnapshot(snapshot);

            var files = new List<BackupFileEntry>();
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }

            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                files.Add(AddFile(zip, snapshot, BackupManifest.DatabaseEntry, CompressionLevel.Optimal));
                var done = 0;
                foreach (var relative in assets)
                {
                    var source = Path.Combine(paths.DataDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(source))
                    {
                        throw new BackupException($"Die Datei „{relative}“ fehlt im Datenordner. Bitte „Datenbestand prüfen“ ausführen.");
                    }

                    // Bilder und PDFs sind bereits komprimiert.
                    files.Add(AddFile(zip, source, relative, CompressionLevel.Fastest));
                    done++;
                    progress?.Report(new ImportProgress(5 + (int)(80L * done / Math.Max(1, assets.Count)), $"Dateien werden gesichert ({done} von {assets.Count}) …"));
                }

                var manifest = new BackupManifest(
                    BackupManifest.FormatName,
                    BackupManifest.CurrentVersion,
                    time.GetUtcNow(),
                    ProgramVersion,
                    schemaVersion,
                    counts,
                    files);
                var entry = zip.CreateEntry(BackupManifest.FileName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                JsonSerializer.Serialize(stream, manifest, BackupArchive.JsonOptions);
            }

            progress?.Report(new ImportProgress(88, "Sicherung wird geprüft …"));
            BackupArchive.Verify(partial);
            File.Move(partial, target, overwrite: true);
            progress?.Report(new ImportProgress(100, "Sicherung erstellt."));
            return new BackupResult(target, files.Count, new FileInfo(target).Length, counts);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    internal static string ProgramVersion => typeof(BackupWriter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Snapshot ohne abgeleitete Daten; liefert Schemaversion, Anzahlen und die Dateien der Ablage.</summary>
    private static (int SchemaVersion, Dictionary<string, long> Counts, List<string> Assets) PrepareSnapshot(string snapshot)
    {
        using var db = OpenUnpooled(snapshot);
        db.Execute("DELETE FROM image_signatures;");
        var schemaVersion = PixFixDatabase.GetSchemaVersion(db);
        var counts = CountedTables.ToDictionary(t => t, t => db.Count(t));
        var assets = new List<string>();
        using (var command = SqliteExtensions.Create(db, "SELECT relative_path FROM assets ORDER BY relative_path;", null))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                assets.Add(BackupArchive.SafeRelativePath(reader.GetString(0)));
            }
        }

        db.Execute("VACUUM;");
        return (schemaVersion, counts, assets);
    }

    internal static SqliteConnection OpenUnpooled(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static BackupFileEntry AddFile(ZipArchive zip, string source, string relative, CompressionLevel level)
    {
        var entry = zip.CreateEntry(relative, level);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = entry.Open();
        var (length, sha256) = BackupArchive.HashStream(input, output);
        return new BackupFileEntry(relative, length, sha256);
    }

    internal static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Aufräumen ist nicht kritisch.
        }
    }

    internal static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Aufräumen ist nicht kritisch.
        }
    }
}
