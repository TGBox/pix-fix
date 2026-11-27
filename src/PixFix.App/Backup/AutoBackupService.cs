using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PixFix.App.Common;
using PixFix.Infrastructure.Backup;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Backup;

/// <summary>
/// Stille automatische Sicherung (SPEC F11): prüft alle 2 Minuten, ob eine Tagessicherung fällig ist und der Benutzer
/// mindestens eine Minute nichts getan hat, und sichert dann im Hintergrund. Fehler landen im Log und in den Einstellungen,
/// nie als Meldungsfenster.
/// </summary>
public sealed class AutoBackupService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

    private readonly BackupCoordinator coordinator;
    private readonly BackupWriter writer;
    private readonly SettingsRepository settings;
    private readonly ILogger<AutoBackupService> logger;
    private readonly DispatcherTimer timer;
    private readonly DateTime startedLocal = DateTime.Now;

    public AutoBackupService(BackupCoordinator coordinator, BackupWriter writer, SettingsRepository settings, ILogger<AutoBackupService> logger)
    {
        this.coordinator = coordinator;
        this.writer = writer;
        this.settings = settings;
        this.logger = logger;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
        timer.Tick += (_, _) => _ = RunIfDueAsync();
    }

    public bool IsEnabled => settings.Get(SettingKeys.AutoBackupEnabled) != "0";

    public void Start() => timer.Start();

    private async Task RunIfDueAsync()
    {
        var folder = coordinator.Folder;
        if (!IsEnabled
            || coordinator.IsBusy
            || !AutoBackupPlan.IsDue(SafeLastBackup(folder), DateTime.Now, startedLocal, UserIdle.Current)
            || !coordinator.TryEnter())
        {
            return;
        }

        try
        {
            var file = Path.Combine(folder, AutoBackupPlan.FileName(DateTime.Today));
            var result = await Task.Run(() =>
            {
                var created = writer.Create(file);
                AutoBackupPlan.Cleanup(folder, DateTime.Today);
                return created;
            }).ConfigureAwait(true);
            settings.Set(SettingKeys.AutoBackupLastError, string.Empty);
            logger.LogInformation("Automatische Sicherung erstellt: {File} ({Length} Bytes)", result.File, result.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BackupException or SqliteException or InvalidDataException)
        {
            logger.LogWarning(ex, "Automatische Sicherung fehlgeschlagen");
            settings.Set(SettingKeys.AutoBackupLastError, $"{DateTime.Now:dd.MM.yyyy HH:mm}: {ex.Message}");
        }
        finally
        {
            coordinator.Exit();
            coordinator.RaiseAutoBackupFinished();
        }
    }

    private static DateTime? SafeLastBackup(string folder)
    {
        try
        {
            return AutoBackupPlan.LastBackup(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
