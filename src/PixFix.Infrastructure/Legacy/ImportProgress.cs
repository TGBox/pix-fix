using System.Globalization;

namespace PixFix.Infrastructure.Legacy;

/// <summary>Fortschritt eines Imports: 0–100 % und ein Text für die Oberfläche.</summary>
public sealed record ImportProgress(int Percent, string Message);

/// <summary>
/// Rechnet die verarbeiteten Datensätze in Prozent um. 0–5 % sind für Vorbereitung (Kopie, Prüfung),
/// 5–99 % für die Datensätze, 100 % erst nach dem Abschluss. Gemeldet wird nur bei Änderung von Prozent
/// oder Abschnitt, damit die Oberfläche nicht mit Meldungen überflutet wird.
/// </summary>
internal sealed class ImportProgressTracker(IProgress<ImportProgress>? progress)
{
    private const int ItemsStart = 5;
    private const int ItemsEnd = 99;

    private long total = 1;
    private long done;
    private string phase = string.Empty;
    private long phaseCount;
    private long phaseIndex;
    private int lastPercent = -1;

    public void Report(int percent, string message) => progress?.Report(new ImportProgress(percent, message));

    public void SetTotal(long totalItems) => total = Math.Max(1, totalItems);

    public void BeginPhase(string name, long count)
    {
        phase = name;
        phaseCount = count;
        phaseIndex = 0;
        lastPercent = -1;
        Report(CurrentPercent(), name + " …");
    }

    public void Step()
    {
        done++;
        phaseIndex++;
        var percent = CurrentPercent();
        if (percent == lastPercent && phaseIndex != phaseCount)
        {
            return;
        }

        lastPercent = percent;
        Report(percent, string.Format(CultureInfo.CurrentCulture, "{0} ({1} von {2}) …", phase, phaseIndex, phaseCount));
    }

    private int CurrentPercent() =>
        ItemsStart + (int)Math.Min(ItemsEnd - ItemsStart, (ItemsEnd - ItemsStart) * done / total);
}
