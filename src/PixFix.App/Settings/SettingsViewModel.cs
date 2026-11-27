using PixFix.App.Backup;
using PixFix.App.Ti;

namespace PixFix.App.Settings;

/// <summary>Bereich „Einstellungen“: Anruf-Einstellungen, Sicherung, TI-Monitor und Import aus PixFix 69.</summary>
public sealed class SettingsViewModel
{
    public SettingsViewModel(GeneralSettingsViewModel general, CallSettingsViewModel calls, BackupViewModel backup, ImportViewModel import, TiMonitorViewModel ti)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(import);
        General = general;
        Calls = calls;
        Backup = backup;
        Import = import;
        Ti = ti;
        backup.RestoreSucceeded += (_, _) => DataReplaced?.Invoke(this, EventArgs.Empty);
        import.ImportSucceeded += (_, _) => DataReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Der gesamte Datenbestand wurde ersetzt (Wiederherstellung oder Import) – alle Bereiche neu laden.</summary>
    public event EventHandler? DataReplaced;

    public GeneralSettingsViewModel General { get; }

    public CallSettingsViewModel Calls { get; }

    public BackupViewModel Backup { get; }

    public ImportViewModel Import { get; }

    public TiMonitorViewModel Ti { get; }
}
