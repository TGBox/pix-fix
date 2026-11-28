using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PixFix.Core.Updates;
using PixFix.Infrastructure.Settings;
using PixFix.Infrastructure.Updates;

namespace PixFix.App.Updates;

/// <summary>
/// Hinweis auf neue PixFix-Versionen: Prüfung beim Start und alle 6 Stunden (abschaltbar), Hinweisleiste im Hauptfenster mit
/// Download-Link, Windows-Benachrichtigung über <see cref="UpdateFound"/>. Installiert wird nichts automatisch – die neue
/// <c>PixFix.exe</c> ersetzt der Nutzer selbst; die Daten liegen getrennt unter <c>%LOCALAPPDATA%\PixFix</c>.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan AutoInterval = TimeSpan.FromHours(6);

    private readonly UpdateChecker checker;
    private readonly SettingsRepository settings;
    private readonly ILogger<UpdateViewModel> logger;
    private readonly DispatcherTimer timer;
    private UpdateInfo? latest;
    private Version? dismissedVersion;
    private Version? notifiedVersion;
    private DateTime? lastChecked;
    private string? lastError;
    private bool isChecking;

    public UpdateViewModel(UpdateChecker checker, SettingsRepository settings, ILogger<UpdateViewModel> logger)
    {
        this.checker = checker;
        this.settings = settings;
        this.logger = logger;
        timer = new DispatcherTimer { Interval = AutoInterval };
        timer.Tick += (_, _) => _ = CheckAsync();
        CheckNowCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking);
        DownloadCommand = new RelayCommand(() => Open(latest?.DownloadOrReleasePage), () => IsUpdateAvailable);
        OpenReleaseNotesCommand = new RelayCommand(
            () => Open(latest?.ReleasePage ?? new Uri(UpdateChecker.ReleasesPage)));
        OpenProgramFolderCommand = new RelayCommand(OpenProgramFolder, () => ProgramFolder is not null);
        DismissCommand = new RelayCommand(() =>
        {
            dismissedVersion = latest?.Version;
            OnPropertyChanged(nameof(IsBannerVisible));
        });
    }

    /// <summary>Eine neuere Version wurde gefunden (je Version nur einmal) – für die Windows-Benachrichtigung.</summary>
    public event EventHandler<UpdateInfo>? UpdateFound;

    public AsyncRelayCommand CheckNowCommand { get; }

    public RelayCommand DownloadCommand { get; }

    public RelayCommand OpenReleaseNotesCommand { get; }

    public RelayCommand OpenProgramFolderCommand { get; }

    public RelayCommand DismissCommand { get; }

    /// <summary>Version dieser EXE (aus dem Release-Build, z. B. 1.4.2); unlesbar = 0.0.0.</summary>
    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    /// <summary>
    /// Lokaler Build (Version mit <c>-dev</c>): keine automatische Suche und keine Hinweisleiste, nur „Jetzt nach Updates
    /// suchen“ in den Einstellungen.
    /// </summary>
    public static bool IsDevelopmentBuild { get; } = AppVersion.IsDevelopmentBuild(ReadInformationalVersion());

    public bool IsReleaseBuild => !IsDevelopmentBuild;

    public string CurrentVersionText =>
        IsDevelopmentBuild
            ? AppVersion.Format(CurrentVersion) + "-dev (Entwicklungsversion, nicht aus einem Release)"
            : AppVersion.Format(CurrentVersion);

    /// <summary>Ordner der laufenden <c>PixFix.exe</c> (dort muss die neue Datei hin).</summary>
    public static string? ProgramFolder =>
        Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe) : null;

    public bool AutoCheck
    {
        get => settings.GetFlag(SettingKeys.UpdateAutoCheck, true);
        set
        {
            settings.SetFlag(SettingKeys.UpdateAutoCheck, value);
            ApplySettings();
            if (value && latest is null && !IsChecking && !IsDevelopmentBuild)
            {
                _ = CheckAsync();
            }
        }
    }

    public bool IsChecking
    {
        get => isChecking;
        private set
        {
            if (SetProperty(ref isChecking, value))
            {
                CheckNowCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsUpdateAvailable => latest is not null && AppVersion.IsNewer(latest.Version, CurrentVersion);

    /// <summary>Hinweisleiste im Hauptfenster: neue Version vorhanden und nicht mit „Später“ ausgeblendet.</summary>
    public bool IsBannerVisible => !IsDevelopmentBuild && IsUpdateAvailable && dismissedVersion != latest!.Version;

    public string LatestVersionText => latest?.VersionText ?? string.Empty;

    public string BannerText =>
        latest is null
            ? string.Empty
            : $"PixFix {latest.VersionText} ist verfügbar – du verwendest Version {CurrentVersionText}.";

    public string InstallHint =>
        "Nach dem Download PixFix beenden („Beenden“ unten links) und die alte PixFix.exe "
        + "durch die neue ersetzen. Deine Daten bleiben erhalten.";

    public string StatusText
    {
        get
        {
            if (IsChecking)
            {
                return "Suche nach Updates …";
            }

            if (lastChecked is null)
            {
                if (lastError is not null)
                {
                    return "Prüfung fehlgeschlagen: " + lastError;
                }

                return IsDevelopmentBuild
                    ? "Entwicklungsversion – keine automatische Suche. „Jetzt nach Updates suchen“ prüft trotzdem."
                    : "Noch nicht geprüft.";
            }

            var stand = "  ·  Stand: " + lastChecked.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
            if (lastError is not null)
            {
                return "Prüfung fehlgeschlagen: " + lastError + stand;
            }

            return IsUpdateAvailable
                ? $"Neue Version {LatestVersionText} verfügbar." + stand
                : "PixFix ist auf dem neuesten Stand." + stand;
        }
    }

    /// <summary>Beim Programmstart: erste Prüfung (falls eingeschaltet) und Zeitgeber.</summary>
    public void Start()
    {
        ApplySettings();
        if (IsDevelopmentBuild)
        {
            logger.LogInformation("Entwicklungsversion – automatische Update-Suche aus");
            return;
        }

        if (AutoCheck)
        {
            _ = CheckAsync();
        }
    }

    /// <summary>Nach Wiederherstellung oder Import: Einstellung neu lesen.</summary>
    public void Reload() => ApplySettings();

    private void ApplySettings()
    {
        if (AutoCheck && !IsDevelopmentBuild)
        {
            timer.Start();
        }
        else
        {
            timer.Stop();
        }

        OnPropertyChanged(nameof(AutoCheck));
    }

    private async Task CheckAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var result = await checker.CheckAsync().ConfigureAwait(true);
            lastChecked = DateTime.Now;
            lastError = result.Error;
            if (result.Latest is { } found)
            {
                latest = found;
            }
            else
            {
                logger.LogInformation("Update-Prüfung ohne Ergebnis: {Error}", result.Error);
            }

            if (IsUpdateAvailable && !IsDevelopmentBuild && notifiedVersion != latest!.Version)
            {
                notifiedVersion = latest.Version;
                logger.LogInformation(
                    "Neue PixFix-Version {Latest} verfügbar (installiert: {Current})", latest.VersionText, CurrentVersionText);
                UpdateFound?.Invoke(this, latest);
            }
        }
        finally
        {
            IsChecking = false;
            DownloadCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(string.Empty);
        }
    }

    private static Assembly EntryAssembly => Assembly.GetEntryAssembly() ?? typeof(UpdateViewModel).Assembly;

    private static string? ReadInformationalVersion() =>
        EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static Version ReadCurrentVersion()
    {
        var assembly = EntryAssembly;
        if (AppVersion.TryParse(ReadInformationalVersion(), out var version))
        {
            return version;
        }

        return assembly.GetName().Version is { } fallback && AppVersion.TryParse(fallback.ToString(), out var parsed)
            ? parsed
            : new Version(0, 0, 0);
    }

    private void OpenProgramFolder()
    {
        if (ProgramFolder is { } folder)
        {
            Open(folder);
        }
    }

    private void Open(Uri? uri)
    {
        if (uri is not null)
        {
            Open(uri.AbsoluteUri);
        }
    }

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Update-Link konnte nicht geöffnet werden");
            MessageBox.Show(
                Application.Current?.MainWindow!,
                "Der Link konnte nicht geöffnet werden.\n\n" + ex.Message + "\n\nAlle Versionen: " + UpdateChecker.ReleasesPage,
                "PixFix-Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
