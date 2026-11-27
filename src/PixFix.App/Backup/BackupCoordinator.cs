using PixFix.Infrastructure;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Backup;

/// <summary>
/// Gemeinsamer Zustand der Sicherungsfunktionen: Sicherungsordner aus den Einstellungen und eine Sperre, damit automatische
/// Sicherung, manuelle Sicherung, Wiederherstellung und Import nie gleichzeitig laufen.
/// </summary>
public sealed class BackupCoordinator(SettingsRepository settings, AppPaths paths)
{
    private int busy;

    /// <summary>Nach jeder automatischen Sicherung (erfolgreich oder nicht).</summary>
    public event EventHandler? AutoBackupFinished;

    public string DefaultFolder => paths.BackupsDirectory;

    public string Folder => settings.Get(SettingKeys.BackupDirectory).Trim() is { Length: > 0 } folder ? folder : DefaultFolder;

    public bool IsBusy => Volatile.Read(ref busy) == 1;

    public bool TryEnter() => Interlocked.CompareExchange(ref busy, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref busy, 0);

    internal void RaiseAutoBackupFinished() => AutoBackupFinished?.Invoke(this, EventArgs.Empty);
}
