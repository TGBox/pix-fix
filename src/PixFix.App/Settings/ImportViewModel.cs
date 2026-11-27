using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixFix.App.Backup;
using PixFix.Infrastructure.Backup;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;

namespace PixFix.App.Settings;

/// <summary>
/// Übernahme des Datenbestands aus einem PixFix-69-Ordner (SPEC F12). Sind schon Daten vorhanden, werden sie nach Rückfrage
/// ersetzt; der bisherige Stand wird vorher gesichert.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly RestoreService restore;
    private readonly BackupCoordinator coordinator;
    private readonly PixFixDatabase database;
    private readonly ILogger<ImportViewModel> logger;
    private bool isBusy;
    private string status = "Noch kein Import durchgeführt.";
    private string reportText = string.Empty;
    private int progressPercent;
    private CancellationTokenSource? cancellation;

    public ImportViewModel(RestoreService restore, BackupCoordinator coordinator, PixFixDatabase database, ILogger<ImportViewModel> logger)
    {
        this.restore = restore;
        this.coordinator = coordinator;
        this.database = database;
        this.logger = logger;
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
    }

    public IAsyncRelayCommand ImportCommand { get; }

    /// <summary>Import abbrechen, solange die Daten noch nicht ausgetauscht werden.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Nach erfolgreichem Import, damit andere Bereiche ihre Daten neu laden.</summary>
    public event EventHandler? ImportSucceeded;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                ImportCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>Fortschritt 0–100 für den Ladebalken.</summary>
    public int ProgressPercent
    {
        get => progressPercent;
        private set => SetProperty(ref progressPercent, value);
    }

    public string ReportText
    {
        get => reportText;
        private set => SetProperty(ref reportText, value);
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "PixFix-69-Ordner auswählen (Programmordner oder „PixFix-Daten“)",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folder = dialog.FolderName;
        if (HasData()
            && System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow!,
                "PixFix enthält bereits Daten. Sollen sie durch den Stand aus PixFix 69 ersetzt werden?\n\nDer aktuelle Stand wird vorher automatisch gesichert in:\n" + coordinator.Folder,
                "PixFix – Import aus PixFix 69",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        if (!coordinator.TryEnter())
        {
            Status = "Gerade läuft eine Sicherung – bitte gleich noch einmal versuchen.";
            return;
        }

        var cts = new CancellationTokenSource();
        cancellation = cts;
        IsBusy = true;
        ReportText = string.Empty;
        ProgressPercent = 0;
        Status = "Import wird gestartet …";
        try
        {
            logger.LogInformation("Import aus PixFix 69 gestartet: {Folder}", folder);
            var progress = new Progress<ImportProgress>(p =>
            {
                ProgressPercent = p.Percent;
                Status = p.Message;
            });
            var report = await Task.Run(() => restore.ImportLegacyFolder(folder, coordinator.Folder, progress, cts.Token)).ConfigureAwait(true);

            ReportText = report.ToText();
            Status = report.Succeeded ? "Import erfolgreich abgeschlossen." : "Import abgebrochen – es wurde nichts übernommen.";
            if (report.Succeeded)
            {
                logger.LogInformation("Import erfolgreich, {WarningCount} Hinweise", report.Warnings.Count);
                ImportSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                logger.LogWarning("Import fehlgeschlagen: {Errors}", string.Join(" | ", report.Errors));
            }
        }
        finally
        {
            cancellation = null;
            cts.Dispose();
            coordinator.Exit();
            IsBusy = false;
        }
    }

    private bool HasData()
    {
        using var db = database.Open();
        return PixFixDatabase.GetSchemaVersion(db) > 0 && !PixFixDatabase.HasNoUserData(db);
    }
}
