using PixFix.App.Backup;

namespace PixFix.App.Settings;

/// <summary>Bereich „Einstellungen“: Anruf-Einstellungen, Sicherung und Import aus PixFix 69.</summary>
public sealed class SettingsViewModel
{
    public SettingsViewModel(CallSettingsViewModel calls, BackupViewModel backup, ImportViewModel import)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(import);
        Calls = calls;
        Backup = backup;
        Import = import;
        backup.RestoreSucceeded += (_, _) => DataReplaced?.Invoke(this, EventArgs.Empty);
        import.ImportSucceeded += (_, _) => DataReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Der gesamte Datenbestand wurde ersetzt (Wiederherstellung oder Import) – alle Bereiche neu laden.</summary>
    public event EventHandler? DataReplaced;

    public CallSettingsViewModel Calls { get; }

    public BackupViewModel Backup { get; }

    public ImportViewModel Import { get; }
}
