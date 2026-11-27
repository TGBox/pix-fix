using System.Globalization;
using System.Text;

namespace PixFix.Infrastructure.Legacy;

/// <summary>Soll/Ist einer Objektart. <see cref="Missing"/> zählt Datensätze, deren Datei in PixFix 69 fehlt.</summary>
public sealed record ImportCount(string Name, long Expected, long Imported, long Missing = 0)
{
    public bool Matches => Imported + Missing == Expected;
}

/// <summary>Ergebnis eines Imports. Erfolgreich nur, wenn alle Anzahlen stimmen und übernommen wurde.</summary>
public sealed class ImportReport
{
    public ImportReport(string source)
    {
        Source = source;
    }

    public string Source { get; }

    public List<ImportCount> Counts { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public bool Committed { get; internal set; }

    public bool Succeeded => Committed && Errors.Count == 0;

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine(Succeeded ? "Import erfolgreich." : "Import NICHT durchgeführt – es wurde nichts übernommen.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Quelle: {Source}");
        text.AppendLine();

        if (Counts.Count > 0)
        {
            text.AppendLine("Objektart                     Soll     Ist  fehlt  Status");
            foreach (var count in Counts)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"{count.Name,-26} {count.Expected,7} {count.Imported,7} {count.Missing,6}  {(count.Matches ? "OK" : "ABWEICHUNG")}");
            }

            text.AppendLine();
        }

        AppendList(text, "Fehler", Errors);
        AppendList(text, "Hinweise", Warnings);
        return text.ToString();
    }

    private static void AppendList(StringBuilder text, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{title} ({items.Count}):");
        foreach (var item in items)
        {
            text.Append("  - ").AppendLine(item);
        }

        text.AppendLine();
    }
}

/// <summary>Erwarteter Abbruchgrund beim Import (falscher Ordner, unbekannte Version, Ziel nicht leer …).</summary>
public sealed class LegacyImportException : Exception
{
    public LegacyImportException()
    {
    }

    public LegacyImportException(string message)
        : base(message)
    {
    }

    public LegacyImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
