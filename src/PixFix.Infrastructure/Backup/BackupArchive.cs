using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace PixFix.Infrastructure.Backup;

/// <summary>
/// Lesen und Prüfen von Sicherungen (4.0 sowie PixFix 69 Format 2.1/3.0). Jede Datei wird gegen Länge und SHA-256 aus dem
/// Manifest geprüft; Pfade aus dem Archiv werden nur übernommen, wenn sie sicher relativ sind (kein „..“, kein Laufwerk).
/// </summary>
public static class BackupArchive
{
    /// <summary>Manifestname der Sicherungen von PixFix 69.</summary>
    public const string LegacyManifestName = "PixFix-Sicherung.xml";

    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Liest nur das Manifest (schnell, ohne Dateien zu prüfen).</summary>
    public static BackupInfo Inspect(string archiveFile)
    {
        using var zip = OpenZip(archiveFile);
        return ReadInfo(zip, archiveFile);
    }

    /// <summary>Prüft jede Datei des Manifests (Vorhandensein, Länge, SHA-256). Wirft <see cref="BackupException"/>.</summary>
    public static BackupInfo Verify(string archiveFile, IProgress<(int Done, int Total)>? progress = null)
    {
        using var zip = OpenZip(archiveFile);
        var info = ReadInfo(zip, archiveFile);
        var done = 0;
        foreach (var file in info.Files)
        {
            _ = SafeRelativePath(file.Path);
            var entry = FindEntry(zip, file.Path);
            using var stream = entry.Open();
            CheckHash(file, HashStream(stream, Stream.Null));
            progress?.Report((++done, info.Files.Count));
        }

        return info;
    }

    internal static ZipArchive OpenZip(string archiveFile)
    {
        try
        {
            return ZipFile.OpenRead(archiveFile);
        }
        catch (InvalidDataException ex)
        {
            throw new BackupException("Die Datei ist kein gültiges Sicherungsarchiv (ZIP).", ex);
        }
    }

    internal static BackupInfo ReadInfo(ZipArchive zip, string archiveFile)
    {
        if (zip.GetEntry(BackupManifest.FileName) is { } json)
        {
            var manifest = ReadManifest(json);
            if (manifest.Format != BackupManifest.FormatName || !manifest.Version.StartsWith("4.", StringComparison.Ordinal))
            {
                throw new BackupException($"Unbekanntes Sicherungsformat „{manifest.Format} {manifest.Version}“. Bitte eine neuere PixFix-Version verwenden.");
            }

            if (!manifest.Files.Any(f => f.Path == BackupManifest.DatabaseEntry))
            {
                throw new BackupException("Die Sicherung enthält keine Datenbank.");
            }

            return new BackupInfo(
                archiveFile,
                BackupKind.Current,
                manifest.Version,
                manifest.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture),
                manifest.Counts,
                manifest.Files);
        }

        if (zip.GetEntry(LegacyManifestName) is { } xml)
        {
            return ReadLegacyInfo(xml, archiveFile);
        }

        throw new BackupException("Die Datei enthält kein PixFix-Sicherungsmanifest.");
    }

    internal static BackupManifest ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, JsonOptions)
                ?? throw new BackupException("Das Sicherungsmanifest ist leer.");
        }
        catch (JsonException ex)
        {
            throw new BackupException("Das Sicherungsmanifest ist beschädigt.", ex);
        }
    }

    /// <summary>Manifest von PixFix 69 (<c>PixFixBackupManifest</c>, XmlSerializer).</summary>
    private static BackupInfo ReadLegacyInfo(ZipArchiveEntry entry, string archiveFile)
    {
        XElement root;
        try
        {
            using var stream = entry.Open();
            root = XDocument.Load(stream).Root ?? throw new BackupException("Das Sicherungsmanifest ist leer.");
        }
        catch (System.Xml.XmlException ex)
        {
            throw new BackupException("Das Sicherungsmanifest von PixFix 69 ist beschädigt.", ex);
        }

        string Text(string name) => root.Element(name)?.Value.Trim() ?? string.Empty;

        var version = Text("Version");
        var sqlite = string.Equals(Text("StorageMode"), "Sqlite", StringComparison.OrdinalIgnoreCase) || version.StartsWith("3.", StringComparison.Ordinal);
        if (!sqlite && !version.StartsWith("2.", StringComparison.Ordinal))
        {
            throw new BackupException($"Unbekannte PixFix-69-Sicherungsversion „{version}“.");
        }

        var counts = new Dictionary<string, long>();
        foreach (var (name, label) in new[]
        {
            ("Entries", "Einträge"), ("Calls", "Anrufe"), ("DeletedCalls", "Gelöschte Anrufe"),
            ("WritingWorkspaces", "Schreibbereiche"), ("InformationEntries", "Reiter"), ("PdfAttachments", "PDF-Anhänge"),
        })
        {
            if (long.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                counts[label] = value;
            }
        }

        var files = root.Element("Files")?.Elements("PixFixBackupFile")
            .Select(f => new BackupFileEntry(
                f.Element("Path")?.Value ?? string.Empty,
                long.TryParse(f.Element("Length")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : -1,
                (f.Element("Sha256")?.Value ?? string.Empty).Trim().ToLowerInvariant()))
            .ToList() ?? [];

        var created = DateTime.TryParse(Text("Created"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)
            : Text("Created");
        return new BackupInfo(archiveFile, sqlite ? BackupKind.Legacy30 : BackupKind.Legacy21, version, created, counts, files);
    }

    /// <summary>
    /// Entpackt alle Dateien des Manifests nach <paramref name="mapTarget"/> und prüft sie dabei. <paramref name="mapTarget"/>
    /// erhält den geprüften relativen Pfad (mit „/“) und liefert das Ziel – oder wirft, wenn der Pfad dort nicht hingehört.
    /// </summary>
    internal static void ExtractVerified(
        ZipArchive zip,
        IReadOnlyList<BackupFileEntry> files,
        string targetRoot,
        Func<string, string> mapTarget,
        Action<int, int>? progress = null)
    {
        var root = Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var done = 0;
        foreach (var file in files)
        {
            var relative = SafeRelativePath(file.Path);
            var target = Path.GetFullPath(mapTarget(relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new BackupException($"Unzulässiger Pfad in der Sicherung: {file.Path}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var entry = FindEntry(zip, file.Path);
            using (var input = entry.Open())
            using (var output = File.Create(target))
            {
                CheckHash(file, HashStream(input, output));
            }

            progress?.Invoke(++done, files.Count);
        }
    }

    /// <summary>Prüft einen Pfad aus dem Archiv und liefert ihn normalisiert mit „/“. Wirft bei absoluten Pfaden oder „..“.</summary>
    public static string SafeRelativePath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized.Length == 0
            || normalized.StartsWith('/')
            || normalized.Contains(':', StringComparison.Ordinal)
            || segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new BackupException($"Unzulässiger Pfad in der Sicherung: {path}");
        }

        return normalized;
    }

    private static ZipArchiveEntry FindEntry(ZipArchive zip, string path) =>
        zip.GetEntry(path)
        ?? zip.GetEntry(path.Replace('/', '\\'))
        ?? throw new BackupException($"Die Datei „{path}“ fehlt in der Sicherung.");

    private static void CheckHash(BackupFileEntry expected, (long Length, string Sha256) actual)
    {
        if ((expected.Length >= 0 && actual.Length != expected.Length) || !string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupException($"Die Datei „{expected.Path}“ ist beschädigt (Prüfsumme stimmt nicht).");
        }
    }

    /// <summary>Kopiert <paramref name="input"/> nach <paramref name="output"/> und berechnet dabei Länge und SHA-256.</summary>
    internal static (long Length, string Sha256) HashStream(Stream input, Stream output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
            length += read;
        }

        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
