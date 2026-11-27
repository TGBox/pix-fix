namespace PixFix.Infrastructure.Backup;

/// <summary>Eine Datei der Sicherung mit Länge und SHA-256 (Kleinbuchstaben, hex).</summary>
public sealed record BackupFileEntry(string Path, long Length, string Sha256);

/// <summary>
/// Inhaltsverzeichnis einer PixFix-Sicherung im Format 4.0 (<c>manifest.json</c> im ZIP). Abgeleitete Daten (Bildindex,
/// aufbereitete Dokumente) sind nicht enthalten und werden nach der Wiederherstellung neu aufgebaut.
/// </summary>
public sealed record BackupManifest(
    string Format,
    string Version,
    DateTimeOffset CreatedUtc,
    string ProgramVersion,
    int SchemaVersion,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<BackupFileEntry> Files)
{
    public const string FileName = "manifest.json";
    public const string FormatName = "PixFix-Sicherung";
    public const string CurrentVersion = "4.0";
    public const string DatabaseEntry = "pixfix.db";
    public const string AssetsPrefix = "assets/";
}

/// <summary>Art einer Sicherungsdatei.</summary>
public enum BackupKind
{
    /// <summary>PixFix 4.0 (dieses Programm).</summary>
    Current,

    /// <summary>PixFix 69 ab 69.74.178: SQLite (<c>pixfix.db</c>, Schema 1).</summary>
    Legacy30,

    /// <summary>PixFix 69 vor SQLite: XML-Dateien.</summary>
    Legacy21,
}

/// <summary>Kopfdaten einer geprüften oder gelesenen Sicherung.</summary>
public sealed record BackupInfo(
    string File,
    BackupKind Kind,
    string Version,
    string Created,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyList<BackupFileEntry> Files)
{
    public long TotalLength => Files.Sum(f => f.Length);

    public string KindText => Kind switch
    {
        BackupKind.Current => "PixFix-Sicherung " + Version,
        BackupKind.Legacy30 => "PixFix-69-Sicherung " + Version + " (SQLite)",
        _ => "PixFix-69-Sicherung " + Version + " (XML)",
    };
}

/// <summary>Sicherung unbrauchbar oder Vorgang nicht möglich; die Meldung ist für die Oberfläche gedacht.</summary>
public sealed class BackupException : Exception
{
    public BackupException()
    {
    }

    public BackupException(string message)
        : base(message)
    {
    }

    public BackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
