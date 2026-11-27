using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixFix.Infrastructure.Legacy;

namespace PixFix.App.Settings;

/// <summary>Übernahme des Datenbestands aus PixFix 69 (SPEC F12).</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly LegacyImporter importer;
    private readonly ILogger<ImportViewModel> logger;
    private bool isBusy;
    private string status = "Noch kein Import durchgeführt.";
    private string reportText = string.Empty;
    private int progressPercent;

    public ImportViewModel(LegacyImporter importer, ILogger<ImportViewModel> logger)
    {
        this.importer = importer;
        this.logger = logger;
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
    }

    public IAsyncRelayCommand ImportCommand { get; }

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
            var report = await Task.Run(() => importer.Import(folder, progress)).ConfigureAwait(true);

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
            IsBusy = false;
        }
    }
}
