using System.Diagnostics;

namespace PixFix.Infrastructure.Communication;

/// <summary>Öffnet Dateien aus der Ablage mit dem Standardprogramm (z. B. Fax-Vorlage in Word, PDF).</summary>
public static class ShellOpener
{
    /// <summary>
    /// Kopiert die Datei unter ihrem lesbaren Namen in einen Temp-Ordner und öffnet die Kopie – die Datei in der Ablage
    /// heißt nach ihrer Prüfsumme und bleibt unverändert. Neutrales Arbeitsverzeichnis, damit nichts den Datenordner sperrt.
    /// </summary>
    public static void OpenCopy(string file, string displayName)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PixFix-Dokumente");
        Directory.CreateDirectory(folder);
        var safeName = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars()));
        if (Path.GetExtension(safeName).Length == 0)
        {
            safeName += Path.GetExtension(file);
        }

        var copy = Path.Combine(folder, safeName);
        File.Copy(file, copy, overwrite: true);
        Process.Start(new ProcessStartInfo(copy) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
    }
}
