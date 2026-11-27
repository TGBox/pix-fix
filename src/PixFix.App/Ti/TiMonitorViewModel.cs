using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PixFix.Infrastructure.Settings;
using PixFix.Infrastructure.Ti;

namespace PixFix.App.Ti;

/// <summary>
/// TI-Monitor (SPEC F9) wie in PixFix 69: Prüfung beim Start und optional alle 5 Minuten, Anzeige grün/rot/orange, rotes
/// Blinken bei einer neuen Störung, bis der Monitor geöffnet wird. Abfragefehler bleiben im Modul (Anzeige „orange“).
/// </summary>
public sealed class TiMonitorViewModel : ObservableObject
{
    private static readonly TimeSpan AutoInterval = TimeSpan.FromMinutes(5);

    private readonly TiMonitorClient client;
    private readonly SettingsRepository settings;
    private readonly ILogger<TiMonitorViewModel> logger;
    private readonly DispatcherTimer timer;
    private readonly HashSet<string> acknowledgedKeys = new(StringComparer.Ordinal);
    private TiResult? result;
    private bool isChecking;
    private bool isBlinking;

    public TiMonitorViewModel(TiMonitorClient client, SettingsRepository settings, ILogger<TiMonitorViewModel> logger)
    {
        this.client = client;
        this.settings = settings;
        this.logger = logger;
        timer = new DispatcherTimer { Interval = AutoInterval };
        timer.Tick += (_, _) => _ = CheckAsync();
        CheckNowCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking && IsEnabled);
        OpenStatusPageCommand = new RelayCommand(() => Open(TiMonitorClient.StatusPage));
        OpenLagePageCommand = new RelayCommand(() => Open(TiMonitorClient.LagePage));
    }

    public AsyncRelayCommand CheckNowCommand { get; }
    public RelayCommand OpenStatusPageCommand { get; }
    public RelayCommand OpenLagePageCommand { get; }

    // ------------------------------------------------------------------ Einstellungen

    public bool IsEnabled
    {
        get => settings.GetFlag(SettingKeys.TiMonitorEnabled, true);
        set
        {
            settings.SetFlag(SettingKeys.TiMonitorEnabled, value);
            ApplySettings();
            if (value && result is null)
            {
                _ = CheckAsync();
            }
        }
    }

    public bool AutoCheck
    {
        get => settings.GetFlag(SettingKeys.TiMonitorAutoCheck, true);
        set
        {
            settings.SetFlag(SettingKeys.TiMonitorAutoCheck, value);
            ApplySettings();
        }
    }

    public bool Blink
    {
        get => settings.GetFlag(SettingKeys.TiMonitorBlink, true);
        set
        {
            settings.SetFlag(SettingKeys.TiMonitorBlink, value);
            ApplySettings();
        }
    }

    // ------------------------------------------------------------------ Anzeige

    /// <summary>Gesamtzustand; <c>null</c> = noch nicht geprüft.</summary>
    public TiState? State => result?.Overall;

    public string Headline => result?.Headline ?? (IsChecking ? "TI-STATUS WIRD GEPRÜFT" : "NOCH NICHT GEPRÜFT");

    public string IncidentText => "TI-Status: " + (result?.Incidents.Text ?? "Noch nicht geprüft");

    public TiState? IncidentState => result?.Incidents.State;

    public string LageText => "TI-Lagebild: " + (result?.Lage.Text ?? "Noch nicht geprüft");

    public TiState? LageState => result?.Lage.State;

    public IReadOnlyList<string> Details =>
        result is null
            ? []
            : [.. result.Incidents.Details.Select(d => "TI-Status: " + d), .. result.Lage.Details.Select(d => "TI-Lagebild: " + d)];

    public string LastCheckedText =>
        IsChecking ? "Prüfe …"
        : result is null ? "Noch nicht geprüft"
        : "Stand: " + result.CheckedLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) + (AutoCheck ? "  ·  automatisch alle 5 Min." : string.Empty);

    public bool IsChecking
    {
        get => isChecking;
        private set
        {
            if (SetProperty(ref isChecking, value))
            {
                CheckNowCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(LastCheckedText));
                OnPropertyChanged(nameof(Headline));
            }
        }
    }

    /// <summary>Rot blinken: neue, noch nicht angesehene Störung (und Blinken eingeschaltet).</summary>
    public bool IsBlinking
    {
        get => isBlinking;
        private set => SetProperty(ref isBlinking, value);
    }

    // ------------------------------------------------------------------ Ablauf

    /// <summary>Beim Programmstart: erste Prüfung und ggf. Zeitgeber.</summary>
    public void Start()
    {
        ApplySettings();
        if (IsEnabled)
        {
            _ = CheckAsync();
        }
    }

    /// <summary>Monitor wurde angesehen – aktuelle Störungen gelten als bestätigt, Blinken aus.</summary>
    public void Acknowledge()
    {
        if (result is not null)
        {
            acknowledgedKeys.UnionWith(result.Keys);
        }

        IsBlinking = false;
    }

    private void ApplySettings()
    {
        if (IsEnabled && AutoCheck)
        {
            timer.Start();
        }
        else
        {
            timer.Stop();
        }

        if (!IsEnabled || !Blink)
        {
            IsBlinking = false;
        }

        CheckNowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(AutoCheck));
        OnPropertyChanged(nameof(Blink));
        OnPropertyChanged(nameof(LastCheckedText));
    }

    private async Task CheckAsync()
    {
        if (IsChecking || !IsEnabled)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var checkedResult = await client.CheckAsync().ConfigureAwait(true);
            result = checkedResult;

            // Nur Störungen, die noch nicht angesehen wurden, lassen das Symbol blinken; behobene vergessen.
            var keys = checkedResult.Keys.ToHashSet(StringComparer.Ordinal);
            acknowledgedKeys.IntersectWith(keys);
            IsBlinking = Blink && checkedResult.HasIncident && !keys.IsSubsetOf(acknowledgedKeys);
            if (checkedResult.HasIncident)
            {
                logger.LogInformation("TI-Monitor: {Headline} – {Incidents} / {Lage}", checkedResult.Headline, checkedResult.Incidents.Text, checkedResult.Lage.Text);
            }
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(string.Empty);
        }
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Application.Current?.MainWindow!, "Die Seite konnte nicht geöffnet werden.\n\n" + ex.Message, "TI-Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
