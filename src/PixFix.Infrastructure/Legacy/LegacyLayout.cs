namespace PixFix.Infrastructure.Legacy;

/// <summary>
/// Ordnerstruktur einer PixFix-69-Installation:
/// <code>
/// &lt;Programmordner&gt;\PixFix-Daten\pixfix.db, settings.xml, documents\, images\
/// &lt;Programmordner&gt;\PDF-Dokumente\, Fax-Dokumente\
/// </code>
/// Gewählt werden darf der Programmordner oder direkt <c>PixFix-Daten</c>.
/// </summary>
public sealed record LegacyLayout(string ProgramDirectory, string DataDirectory)
{
    public const string DataFolderName = "PixFix-Daten";

    public string DatabaseFile => Path.Combine(DataDirectory, "pixfix.db");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.xml");

    public string PdfDirectory => Path.Combine(ProgramDirectory, "PDF-Dokumente");

    public string FaxDirectory => Path.Combine(ProgramDirectory, "Fax-Dokumente");

    public static LegacyLayout Resolve(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var full = Path.GetFullPath(folder);

        if (File.Exists(Path.Combine(full, DataFolderName, "pixfix.db")))
        {
            return new LegacyLayout(full, Path.Combine(full, DataFolderName));
        }

        if (File.Exists(Path.Combine(full, "pixfix.db")))
        {
            var parent = Directory.GetParent(full)?.FullName ?? full;
            return new LegacyLayout(parent, full);
        }

        throw new LegacyImportException(
            $"Im Ordner '{full}' wurde keine PixFix-69-Datenbank gefunden. " +
            $"Bitte den PixFix-Programmordner oder den Ordner '{DataFolderName}' auswählen.");
    }
}
