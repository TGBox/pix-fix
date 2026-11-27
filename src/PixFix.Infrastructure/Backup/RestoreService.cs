using System.Globalization;
using Microsoft.Data.Sqlite;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;

namespace PixFix.Infrastructure.Backup;

/// <summary>
/// Ersetzt den Datenbestand durch eine Sicherung (4.0 oder PixFix 69 Format 3.0) oder einen PixFix-69-Ordner (SPEC F11/F12).
/// Ablauf: in einen Stage-Ordner entpacken bzw. importieren und vollständig prüfen → aktuellen Stand sichern → Datenbank und
/// Ablage austauschen; schlägt der Austausch fehl, wird der vorherige Stand zurückgelegt. Bis zum Austausch bleiben die
/// aktuellen Daten unberührt.
/// </summary>
public sealed class RestoreService(
    AppPaths paths,
    BackupWriter writer,
    TimeZoneInfo? legacyTimeZone = null,
    TimeProvider? timeProvider = null)
{
    private const string StageFolder = "restore-stage";
    private const string LegacyFolder = "restore-legacy";
    private const string OldFolder = "restore-old";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    /// <summary>Sicherung wiederherstellen. Vorher wird der aktuelle Stand nach <paramref name="safetyFolder"/> gesichert.</summary>
    /// <remarks>
    /// Mit <paramref name="cancellationToken"/> lässt sich bis kurz vor dem Austausch abbrechen; die Daten bleiben dann unverändert.
    /// </remarks>
    public ImportReport RestoreArchive(
        string archiveFile,
        string safetyFolder,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new ImportReport(archiveFile);
        var reporter = progress;
        progress = Guard(progress, cancellationToken);
        Run(report, safetyFolder, reporter, progress, cancellationToken, stage =>
        {
            progress?.Report(new ImportProgress(1, "Sicherung wird gelesen …"));
            using var zip = BackupArchive.OpenZip(archiveFile);
            var info = BackupArchive.ReadInfo(zip, archiveFile);
            switch (info.Kind)
            {
                case BackupKind.Current:
                    StageCurrent(zip, info, stage, report, progress);
                    break;
                case BackupKind.Legacy30:
                    ImportLegacy(ExtractLegacy(zip, info, progress), stage, report, progress, 20);
                    break;
                default:
                    StageLegacy21(zip, info, stage, report, progress);
                    break;
            }
        });
        return report;
    }

    /// <summary>Datenbestand durch den Inhalt eines PixFix-69-Ordners ersetzen (auch wenn schon Daten vorhanden sind).</summary>
    public ImportReport ImportLegacyFolder(
        string legacyFolder,
        string safetyFolder,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new ImportReport(legacyFolder);
        var guarded = Guard(progress, cancellationToken);
        Run(report, safetyFolder, progress, guarded, cancellationToken, stage =>
            ImportLegacy(PrepareXmlOnlyFolder(legacyFolder, guarded), stage, report, guarded, 5));
        return report;
    }

    /// <summary>
    /// PixFix 69 vor der SQLite-Umstellung hat nur XML-Dateien (<c>pixfix.xml</c> …) und keine <c>pixfix.db</c>. Dann wird der
    /// Datenordner (und Fax-/PDF-Dokumente) kopiert und die Kopie wie eine 2.1-Sicherung umgewandelt; das Original bleibt
    /// unverändert. Ordner mit <c>pixfix.db</c> werden unverändert zurückgegeben.
    /// </summary>
    private string PrepareXmlOnlyFolder(string folder, IProgress<ImportProgress> progress)
    {
        var full = Path.GetFullPath(folder);
        if (File.Exists(Path.Combine(full, LegacyLayout.DataFolderName, "pixfix.db")) || File.Exists(Path.Combine(full, "pixfix.db")))
        {
            return folder;
        }

        string programDirectory, dataDirectory;
        if (File.Exists(Path.Combine(full, LegacyLayout.DataFolderName, "pixfix.xml")))
        {
            programDirectory = full;
            dataDirectory = Path.Combine(full, LegacyLayout.DataFolderName);
        }
        else if (File.Exists(Path.Combine(full, "pixfix.xml")))
        {
            dataDirectory = full;
            programDirectory = Directory.GetParent(full)?.FullName ?? full;
        }
        else
        {
            return folder;
        }

        progress.Report(new ImportProgress(1, "PixFix 69 mit XML-Daten erkannt – Daten werden kopiert …"));
        var legacyRoot = Path.Combine(paths.DataDirectory, LegacyFolder);
        CopyDirectory(dataDirectory, Path.Combine(legacyRoot, LegacyLayout.DataFolderName));
        foreach (var name in new[] { "Fax-Dokumente", "PDF-Dokumente" })
        {
            var source = Path.Combine(programDirectory, name);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(legacyRoot, name));
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(legacyRoot, name));
            }
        }

        progress.Report(new ImportProgress(4, "XML-Daten aus PixFix 69 werden umgewandelt …"));
        try
        {
            Legacy21Converter.CreateDatabase(Path.Combine(legacyRoot, LegacyLayout.DataFolderName));
        }
        catch (LegacyImportException ex)
        {
            throw new BackupException(ex.Message, ex);
        }

        return legacyRoot;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Jede Fortschrittsmeldung prüft auf Abbruch – so endet auch der Import aus PixFix 69 mitten in den Datensätzen
    /// (seine Transaktion in der Stage-Datenbank wird verworfen).
    /// </summary>
    private static IProgress<ImportProgress> Guard(IProgress<ImportProgress>? progress, CancellationToken cancellationToken) =>
        new SyncProgress(p =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(p);
        });

    // ------------------------------------------------------------------ Ablauf

    private void Run(
        ImportReport report,
        string safetyFolder,
        IProgress<ImportProgress>? reporter,
        IProgress<ImportProgress> progress,
        CancellationToken cancellationToken,
        Action<AppPaths> prepare)
    {
        var stage = new AppPaths(Path.Combine(paths.DataDirectory, StageFolder));
        try
        {
            Cleanup();
            stage.EnsureCreated();
            prepare(stage);
            if (report.Errors.Count > 0)
            {
                return;
            }

            FinishStage(stage);
            report.Warnings.AddRange(SafetyBackup(safetyFolder, progress, cancellationToken));

            // Letzte Gelegenheit zum Abbrechen – ab hier wird ausgetauscht.
            progress.Report(new ImportProgress(97, "Daten werden ausgetauscht …"));
            Activate(stage);
            report.Committed = true;
            reporter?.Report(new ImportProgress(100, "Daten wurden übernommen."));
        }
        catch (OperationCanceledException)
        {
            report.Errors.Add("Vom Benutzer abgebrochen – es wurde nichts verändert.");
        }
        catch (BackupException ex)
        {
            report.Errors.Add(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            report.Errors.Add("Unerwarteter Fehler: " + ex.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Cleanup();
            if (!report.Succeeded)
            {
                reporter?.Report(new ImportProgress(100, "Abgebrochen – die bisherigen Daten sind unverändert."));
            }
        }
    }

    private void StageCurrent(System.IO.Compression.ZipArchive zip, BackupInfo info, AppPaths stage, ImportReport report, IProgress<ImportProgress>? progress)
    {
        BackupArchive.ExtractVerified(
            zip,
            info.Files,
            stage.DataDirectory,
            relative => relative == BackupManifest.DatabaseEntry || relative.StartsWith(BackupManifest.AssetsPrefix, StringComparison.Ordinal)
                ? Path.Combine(stage.DataDirectory, relative.Replace('/', Path.DirectorySeparatorChar))
                : throw new BackupException($"Unerwartete Datei in der Sicherung: {relative}"),
            (done, total) => progress?.Report(new ImportProgress(2 + (60 * done / Math.Max(1, total)), $"Sicherung wird entpackt und geprüft ({done} von {total}) …")));

        progress?.Report(new ImportProgress(65, "Datenbank wird geprüft …"));
        using (var db = BackupWriter.OpenUnpooled(stage.DatabaseFile))
        {
            if (db.Scalar("PRAGMA integrity_check;") as string != "ok")
            {
                throw new BackupException("Die Datenbank in der Sicherung ist beschädigt (integrity_check).");
            }
        }

        var database = new PixFixDatabase(stage);
        database.Migrate();

        using (var db = database.Open())
        {
            foreach (var (table, expected) in info.Counts)
            {
                if (Array.IndexOf(BackupWriter.CountedTables, table) >= 0)
                {
                    report.Counts.Add(new ImportCount(table, expected, db.Count(table)));
                }
            }

            var missing = 0;
            using var command = SqliteExtensions.Create(db, "SELECT relative_path FROM assets;", null);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!File.Exists(Path.Combine(stage.DataDirectory, reader.GetString(0).Replace('/', Path.DirectorySeparatorChar))))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                report.Errors.Add($"{missing} Datei(en) der Ablage fehlen in der Sicherung.");
            }
        }

        foreach (var count in report.Counts.Where(c => !c.Matches))
        {
            report.Errors.Add($"Anzahl „{count.Name}“ stimmt nicht: erwartet {count.Expected}, vorhanden {count.Imported}.");
        }
    }

    /// <summary>
    /// PixFix-69-Sicherung (2.1/3.0) als PixFix-69-Ordner auspacken: Datenordner im Wurzelverzeichnis des Archivs,
    /// Programmordner (Fax-/PDF-Dokumente) unter <c>__PixFixProgram/</c>.
    /// </summary>
    private string ExtractLegacy(System.IO.Compression.ZipArchive zip, BackupInfo info, IProgress<ImportProgress>? progress)
    {
        const string ProgramPrefix = "__PixFixProgram/";
        var legacyRoot = Path.Combine(paths.DataDirectory, LegacyFolder);
        BackupArchive.ExtractVerified(
            zip,
            info.Files,
            legacyRoot,
            relative => relative.StartsWith(ProgramPrefix, StringComparison.Ordinal)
                ? Path.Combine(legacyRoot, relative[ProgramPrefix.Length..].Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(legacyRoot, LegacyLayout.DataFolderName, relative.Replace('/', Path.DirectorySeparatorChar)),
            (done, total) => progress?.Report(new ImportProgress(1 + (19 * done / Math.Max(1, total)), $"Sicherung wird entpackt und geprüft ({done} von {total}) …")));
        Directory.CreateDirectory(Path.Combine(legacyRoot, LegacyLayout.DataFolderName));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "Fax-Dokumente"));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "PDF-Dokumente"));
        return legacyRoot;
    }

    /// <summary>
    /// PixFix-69-Sicherung 2.1 (XML): auspacken, daraus eine PixFix-69-Datenbank (Schema 1) erzeugen wie die
    /// SQLite-Erstmigration von 69.74.178, Anzahlen mit dem Manifest abgleichen und dann wie 3.0 importieren.
    /// </summary>
    private void StageLegacy21(System.IO.Compression.ZipArchive zip, BackupInfo info, AppPaths stage, ImportReport report, IProgress<ImportProgress>? progress)
    {
        var legacyRoot = ExtractLegacy(zip, info, progress);
        progress?.Report(new ImportProgress(20, "XML-Daten aus PixFix 69 werden umgewandelt …"));
        Dictionary<string, long> converted;
        try
        {
            converted = Legacy21Converter.CreateDatabase(Path.Combine(legacyRoot, LegacyLayout.DataFolderName));
        }
        catch (LegacyImportException ex)
        {
            throw new BackupException(ex.Message, ex);
        }

        foreach (var (name, expected) in info.Counts)
        {
            if (converted.TryGetValue(name, out var actual))
            {
                report.Counts.Add(new ImportCount("Sicherung: " + name, expected, actual));
                if (actual != expected)
                {
                    report.Errors.Add($"Anzahl „{name}“ im Manifest ({expected}) passt nicht zu den XML-Daten ({actual}).");
                }
            }
        }

        if (report.Errors.Count == 0)
        {
            ImportLegacy(legacyRoot, stage, report, progress, 25);
        }
    }

    private void ImportLegacy(string folder, AppPaths stage, ImportReport report, IProgress<ImportProgress>? progress, int startPercent)
    {
        var database = new PixFixDatabase(stage);
        var importer = new LegacyImporter(database, new AssetStore(stage), legacyTimeZone, time);
        var scaled = progress is null
            ? null
            : new SyncProgress(p => progress!.Report(new ImportProgress(startPercent + ((90 - startPercent) * p.Percent / 100), p.Message)));
        var result = importer.Import(folder, scaled);
        report.Counts.AddRange(result.Counts);
        report.Warnings.AddRange(result.Warnings);
        report.Errors.AddRange(result.Errors);
        if (!result.Succeeded && result.Errors.Count == 0)
        {
            report.Errors.Add("Der Import aus PixFix 69 wurde nicht abgeschlossen.");
        }
    }

    /// <summary>Stage-Datenbank ohne WAL-Dateien abschließen, damit nur <c>pixfix.db</c> verschoben werden muss.</summary>
    private static void FinishStage(AppPaths stage)
    {
        SqliteConnection.ClearAllPools();
        using var db = BackupWriter.OpenUnpooled(stage.DatabaseFile);
        db.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        db.Execute("PRAGMA journal_mode = DELETE;");
    }

    /// <summary>Sicherung des aktuellen Stands vor dem Austausch (nur wenn Daten vorhanden sind).</summary>
    private IEnumerable<string> SafetyBackup(string safetyFolder, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.DatabaseFile))
        {
            return [];
        }

        using (var db = BackupWriter.OpenUnpooled(paths.DatabaseFile))
        {
            if (PixFixDatabase.GetSchemaVersion(db) == 0 || PixFixDatabase.HasNoUserData(db))
            {
                return [];
            }
        }

        progress?.Report(new ImportProgress(91, "Aktueller Stand wird vorher gesichert …"));
        var stamp = time.GetLocalNow().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var result = writer.Create(Path.Combine(safetyFolder, $"Vor-Wiederherstellung-{stamp}.pixfix"), null, cancellationToken);
        return [$"Der vorherige Stand wurde gesichert: {result.File}"];
    }

    /// <summary>Tauscht Datenbank und Ablage aus; bei einem Fehler wird der vorherige Stand zurückgelegt.</summary>
    private void Activate(AppPaths stage)
    {
        SqliteConnection.ClearAllPools();
        var old = Path.Combine(paths.DataDirectory, OldFolder);
        Directory.CreateDirectory(old);
        var moved = new List<(string From, string To)>();

        void Move(string from, string to, bool directory)
        {
            if (directory ? Directory.Exists(from) : File.Exists(from))
            {
                if (directory)
                {
                    Directory.Move(from, to);
                }
                else
                {
                    File.Move(from, to);
                }

                moved.Add((from, to));
            }
        }

        try
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                Move(paths.DatabaseFile + suffix, Path.Combine(old, "pixfix.db" + suffix), directory: false);
            }

            Move(paths.AssetsDirectory, Path.Combine(old, "assets"), directory: true);
            Move(stage.DatabaseFile, paths.DatabaseFile, directory: false);
            Move(stage.AssetsDirectory, paths.AssetsDirectory, directory: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rückwärts zurücklegen: zuerst die neuen Dateien zurück in die Stage, dann den alten Stand an seinen Platz.
            for (var i = moved.Count - 1; i >= 0; i--)
            {
                var (from, to) = moved[i];
                if (Directory.Exists(to))
                {
                    Directory.Move(to, from);
                }
                else if (File.Exists(to))
                {
                    File.Move(to, from);
                }
            }

            throw new BackupException(
                "Die Daten konnten nicht ausgetauscht werden (Datei in Benutzung?). Der bisherige Stand ist unverändert.\n\n" + ex.Message, ex);
        }
    }

    private void Cleanup()
    {
        foreach (var folder in new[] { StageFolder, LegacyFolder, OldFolder })
        {
            BackupWriter.TryDeleteDirectory(Path.Combine(paths.DataDirectory, folder));
        }
    }

    /// <summary>Synchroner Fortschritt (<see cref="Progress{T}"/> würde auf den Thread-Pool umleiten).</summary>
    private sealed class SyncProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
