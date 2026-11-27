using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixFix.Infrastructure.Backup;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Backup;

/// <summary>Reiter „Sicherung“: Sicherungsordner, automatische Sicherung, sichern, prüfen und wiederherstellen (SPEC F11).</summary>
public sealed class BackupViewModel : ObservableObject
{
    private const string FileFilter = "PixFix-Sicherung (*.pixfix)|*.pixfix|ZIP-Dateien (*.zip)|*.zip|Alle Dateien (*.*)|*.*";

    private readonly BackupCoordinator coordinator;
    private readonly BackupWriter writer;
    private readonly RestoreService restore;
    private readonly SettingsRepository settings;
    private readonly ILogger<BackupViewModel> logger;
    private bool isBusy;
    private int progressPercent;
    private string status = string.Empty;
    private string reportText = string.Empty;

    public BackupViewModel(
        BackupCoordinator coordinator,
        BackupWriter writer,
        RestoreService restore,
        SettingsRepository settings,
        ILogger<BackupViewModel> logger)
    {
        this.coordinator = coordinator;
        this.writer = writer;
        this.restore = restore;
        this.settings = settings;
        this.logger = logger;

        BackupNowCommand = new AsyncRelayCommand(() => CreateAsync(null), CanRun);
        BackupAsCommand = new AsyncRelayCommand(BackupAsAsync, CanRun);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, CanRun);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, CanRun);
        ChangeFolderCommand = new RelayCommand(ChangeFolder, CanRun);
        ResetFolderCommand = new RelayCommand(() => SetFolder(string.Empty), () => CanRun() && !IsDefaultFolder);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        coordinator.AutoBackupFinished += (_, _) => OnPropertyChanged(nameof(LastAutoBackupText));
    }

    /// <summary>Nach einer erfolgreichen Wiederherstellung: alle Bereiche müssen ihre Daten neu laden.</summary>
    public event EventHandler? RestoreSucceeded;

    public AsyncRelayCommand BackupNowCommand { get; }
    public AsyncRelayCommand BackupAsCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public RelayCommand ChangeFolderCommand { get; }
    public RelayCommand ResetFolderCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public string BackupFolder => coordinator.Folder;

    public bool IsDefaultFolder => string.Equals(coordinator.Folder, coordinator.DefaultFolder, StringComparison.OrdinalIgnoreCase);

    public bool AutoBackupEnabled
    {
        get => settings.Get(SettingKeys.AutoBackupEnabled) != "0";
        set
        {
            settings.Set(SettingKeys.AutoBackupEnabled, value ? "1" : "0");
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastAutoBackupText));
        }
    }

    public string LastAutoBackupText
    {
        get
        {
            var error = settings.Get(SettingKeys.AutoBackupLastError);
            DateTime? last;
            try
            {
                last = AutoBackupPlan.LastBackup(coordinator.Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "Der Sicherungsordner ist nicht erreichbar: " + ex.Message;
            }

            var text = last is { } at
                ? "Letzte automatische Sicherung: " + at.ToString("dddd, dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)
                : "Noch keine automatische Sicherung in diesem Ordner.";
            return error.Length == 0 ? text : text + "\nLetzter Fehler: " + error;
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                BackupNowCommand.NotifyCanExecuteChanged();
                BackupAsCommand.NotifyCanExecuteChanged();
                VerifyCommand.NotifyCanExecuteChanged();
                RestoreCommand.NotifyCanExecuteChanged();
                ChangeFolderCommand.NotifyCanExecuteChanged();
                ResetFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int ProgressPercent
    {
        get => progressPercent;
        private set => SetProperty(ref progressPercent, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string ReportText
    {
        get => reportText;
        private set => SetProperty(ref reportText, value);
    }

    /// <summary>Nach einem Import o. Ä. Anzeige aktualisieren.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private bool CanRun() => !IsBusy;

    // ------------------------------------------------------------------ Sichern

    private async Task BackupAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "PixFix-Sicherung speichern",
            Filter = FileFilter,
            FileName = DefaultName(),
            DefaultExt = ".pixfix",
        };
        if (dialog.ShowDialog(Owner) == true)
        {
            await CreateAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private async Task CreateAsync(string? file)
    {
        file ??= Path.Combine(coordinator.Folder, DefaultName());
        await RunAsync("Sicherung wird erstellt …", async progress =>
        {
            var result = await Task.Run(() => writer.Create(file, progress)).ConfigureAwait(true);
            Status = "Sicherung erstellt und geprüft.";
            ReportText = new StringBuilder()
                .AppendLine("Sicherung erstellt und vollständig geprüft.")
                .AppendLine(result.File)
                .AppendLine(CultureInfo.CurrentCulture, $"{result.FileCount} Dateien, {result.Length / 1024.0 / 1024.0:0.0} MB")
                .AppendLine()
                .Append(CountsText(result.Counts))
                .ToString();
            logger.LogInformation("Sicherung erstellt: {File}", result.File);
        }).ConfigureAwait(true);
    }

    private static string DefaultName() =>
        "Sicherung-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".pixfix";

    // ------------------------------------------------------------------ Prüfen

    private async Task VerifyAsync()
    {
        if (PickArchive("PixFix-Sicherung prüfen") is not { } file)
        {
            return;
        }

        await RunAsync("Sicherung wird geprüft …", async progress =>
        {
            var info = await Task.Run(() => BackupArchive.Verify(
                file,
                new SyncProgress<(int Done, int Total)>(p => progress.Report(new ImportProgress(100 * p.Done / Math.Max(1, p.Total), $"Datei {p.Done} von {p.Total} wird geprüft …"))))).ConfigureAwait(true);
            Status = "Die Sicherung ist vollständig und unbeschädigt.";
            ReportText = InfoText(info, "Prüfung bestanden: alle Dateien vorhanden, Längen und Prüfsummen stimmen.");
        }).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ Wiederherstellen

    private async Task RestoreAsync()
    {
        if (PickArchive("PixFix-Sicherung wiederherstellen") is not { } file)
        {
            return;
        }

        BackupInfo info;
        try
        {
            info = BackupArchive.Inspect(file);
        }
        catch (Exception ex) when (ex is BackupException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Owner!, ex.Message, "PixFix – Wiederherstellen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var question = $"{info.KindText} vom {info.Created} wiederherstellen?\n\n"
            + "Alle aktuellen PixFix-Daten (Wissensdatenbank, Schreibbereiche, Anrufe, Vorlagen, Einstellungen) werden durch den "
            + "Stand der Sicherung ersetzt. Der aktuelle Stand wird vorher automatisch gesichert in:\n" + coordinator.Folder;
        if (MessageBox.Show(Owner!, question, "PixFix – Wiederherstellen", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        await RunAsync("Wiederherstellung wird vorbereitet …", async progress =>
        {
            var report = await Task.Run(() => restore.RestoreArchive(file, coordinator.Folder, progress)).ConfigureAwait(true);
            ReportText = report.ToText();
            if (report.Succeeded)
            {
                Status = "Wiederherstellung abgeschlossen.";
                logger.LogInformation("Sicherung wiederhergestellt: {File}", file);
                RestoreSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Status = "Wiederherstellung abgebrochen – die bisherigen Daten sind unverändert.";
                logger.LogWarning("Wiederherstellung fehlgeschlagen: {Errors}", string.Join(" | ", report.Errors));
            }
        }).ConfigureAwait(true);
    }

    // ------------------------------------------------------------------ Ordner

    private void ChangeFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Ordner für PixFix-Sicherungen wählen (z. B. in OneDrive)" };
        if (dialog.ShowDialog(Owner) == true)
        {
            SetFolder(dialog.FolderName);
        }
    }

    private void SetFolder(string folder)
    {
        settings.Set(SettingKeys.BackupDirectory, folder);
        OnPropertyChanged(nameof(BackupFolder));
        OnPropertyChanged(nameof(IsDefaultFolder));
        OnPropertyChanged(nameof(LastAutoBackupText));
        ResetFolderCommand.NotifyCanExecuteChanged();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(coordinator.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + coordinator.Folder + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Owner!, "Der Ordner konnte nicht geöffnet werden.\n\n" + ex.Message, "PixFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ Hilfen

    private string? PickArchive(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = FileFilter,
            InitialDirectory = Directory.Exists(coordinator.Folder) ? coordinator.Folder : string.Empty,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    /// <summary>Führt einen Vorgang mit Fortschrittsbalken aus; nie parallel zu einer anderen Sicherung/Wiederherstellung.</summary>
    private async Task RunAsync(string startText, Func<IProgress<ImportProgress>, Task> work)
    {
        if (!coordinator.TryEnter())
        {
            MessageBox.Show(Owner!, "Gerade läuft bereits eine Sicherung. Bitte einen Moment warten und erneut versuchen.", "PixFix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;
        Status = startText;
        ReportText = string.Empty;
        var progress = new Progress<ImportProgress>(p =>
        {
            ProgressPercent = p.Percent;
            Status = p.Message;
        });
        try
        {
            await work(progress).ConfigureAwait(true);
            ProgressPercent = 100;
        }
        catch (Exception ex) when (ex is BackupException or IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            logger.LogWarning(ex, "Sicherungsvorgang fehlgeschlagen");
            Status = "Fehlgeschlagen: " + ex.Message;
            ReportText = ex.Message;
        }
        finally
        {
            coordinator.Exit();
            IsBusy = false;
            OnPropertyChanged(nameof(LastAutoBackupText));
        }
    }

    private static string InfoText(BackupInfo info, string headline)
    {
        var text = new StringBuilder()
            .AppendLine(headline)
            .AppendLine()
            .AppendLine(CultureInfo.CurrentCulture, $"Datei:    {info.File}")
            .AppendLine(CultureInfo.CurrentCulture, $"Format:   {info.KindText}")
            .AppendLine(CultureInfo.CurrentCulture, $"Erstellt: {info.Created}")
            .AppendLine(CultureInfo.CurrentCulture, $"Inhalt:   {info.Files.Count} Dateien, {info.TotalLength / 1024.0 / 1024.0:0.0} MB")
            .AppendLine();
        return text.Append(CountsText(info.Counts)).ToString();
    }

    private static string CountsText(IReadOnlyDictionary<string, long> counts)
    {
        var text = new StringBuilder();
        foreach (var (name, count) in counts)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{Label(name),-28} {count,7}");
        }

        return text.ToString();
    }

    private static string Label(string table) => table switch
    {
        "knowledge_entries" => "Einträge (Wissensdatenbank)",
        "knowledge_entry_images" => "Bilder (Wissensdatenbank)",
        "workspaces" => "Schreibbereiche",
        "information_entries" => "Reiter",
        "information_attachments" => "PDF-Anhänge",
        "calls" => "Anrufe",
        "templates" => "Vorlagen",
        "cobra_quick_texts" => "Cobra-Schnelltexte",
        "fax_documents" => "Fax-Dokumente",
        "trash" => "Papierkorb",
        "assets" => "Dateien in der Ablage",
        _ => table,
    };

    private static Window? Owner => Application.Current?.MainWindow;

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
