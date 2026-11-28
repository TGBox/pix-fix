using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixFix.App.Calls;
using PixFix.App.Common;
using PixFix.App.Help;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using PixFix.App.Ti;
using PixFix.App.Updates;
using PixFix.App.Workspaces;
using PixFix.Infrastructure;
using PixFix.Infrastructure.Settings;

namespace PixFix.App;

/// <summary>Eintrag der linken Navigation. <paramref name="Content"/> ist das ViewModel des Bereichs (oder null = noch nicht umgesetzt).</summary>
public sealed record NavigationSection(string Icon, string Title, string Description, object? Content);

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsRepository settingsRepository;
    private NavigationSection? selectedSection;

    public MainViewModel(
        AppPaths paths,
        KnowledgeViewModel knowledge,
        WorkspacesViewModel workspaces,
        CallsViewModel calls,
        TiMonitorViewModel ti,
        SettingsViewModel settings,
        SettingsRepository settingsRepository,
        UndoService undo,
        HelpViewModel help,
        UpdateViewModel update)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        DataDirectory = paths.DataDirectory;
        this.settingsRepository = settingsRepository;

        // Nach Import oder Wiederherstellung zeigen alle Bereiche sofort den neuen Datenbestand.
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(ti);
        ArgumentNullException.ThrowIfNull(help);
        Ti = ti;
        Undo = undo;
        Update = update;
        settings.DataReplaced += (_, _) =>
        {
            knowledge.Reload();
            workspaces.Reload();
            calls.Reload();
            settings.Calls.Reload();
            settings.Backup.Refresh();
            settings.General.Reload();
            OnPropertyChanged(nameof(IsSidebarCollapsed));
        };

        // Bearbeitete Eintragsvorlagen sofort in der Wissensdatenbank anbieten.
        settings.Calls.TemplatesChanged += (_, _) => knowledge.ReloadTemplates();

        Sections =
        [
            new(IconGlyphs.Knowledge, "Wissensdatenbank", "Fehlerbilder und Lösungen nachschlagen.", knowledge),
            new(IconGlyphs.Workspaces, "Schreibbereiche", "Notizen, Anleitungen, Werkzeuge und PDF-Anhänge.", workspaces),
            new(IconGlyphs.Calls, "Anrufe", "Anrufkarten, Rückruf-Erinnerungen, Vorlagen und Telefonie.", calls),
            new(IconGlyphs.Ti, "TI-Monitor", "Störungen der Telematikinfrastruktur (gematik).", ti),
            new(IconGlyphs.Settings, "Einstellungen", "Anrufe, Vorlagen, Sicherung, Import aus PixFix 69.", settings),
            new(IconGlyphs.Help, "Hilfe", "Anleitungen zu allen Funktionen und die Einführungstour.", help),
        ];
        selectedSection = Sections[0];
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);

        // „OK“ in einer Rückruf-Erinnerung springt zu den Anrufen.
        calls.ShowCallsRequested += (_, _) => SelectedSection = Sections[2];

        // Klick auf das TI-Symbol öffnet den Monitor und bestätigt eine neue Störung.
        ShowTiCommand = new RelayCommand(() => SelectedSection = Sections[3]);

        // Hilfe: „Zum Bereich wechseln“ und „Einführungstour starten“.
        help.SectionRequested += (_, index) => ShowSection(index);
        help.TourRequested += (_, _) => TourRequested?.Invoke(this, EventArgs.Empty);
        ShowHelpCommand = new RelayCommand(() => SelectedSection = Sections[HelpContent.Help]);

        // „Beenden“ unten in der Navigation – wie im Menü des Infobereichs.
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));

        // Strg+1 … Strg+6 wechselt den Bereich.
        ShowSectionCommand = new RelayCommand<string>(index =>
        {
            if (int.TryParse(index, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) && i >= 1 && i <= Sections.Count)
            {
                SelectedSection = Sections[i - 1];
            }
        });
    }

    /// <summary>Die Einführungstour soll angezeigt werden (Hilfe → „Einführungstour starten“).</summary>
    public event EventHandler? TourRequested;

    /// <summary>PixFix soll vollständig beendet werden (nicht nur in den Infobereich).</summary>
    public event EventHandler? ExitRequested;

    public ObservableCollection<NavigationSection> Sections { get; }

    public NavigationSection? SelectedSection
    {
        get => selectedSection;
        set
        {
            if (SetProperty(ref selectedSection, value) && value?.Content == Ti)
            {
                Ti.Acknowledge();
            }
        }
    }

    public string DataDirectory { get; }

    public TiMonitorViewModel Ti { get; }

    /// <summary>Hinweisleiste „… gelöscht – Rückgängig“.</summary>
    public UndoService Undo { get; }

    /// <summary>Hinweisleiste „Neue Version verfügbar“.</summary>
    public UpdateViewModel Update { get; }

    public RelayCommand ShowTiCommand { get; }

    public RelayCommand ShowHelpCommand { get; }

    public RelayCommand ExitCommand { get; }

    public RelayCommand<string> ShowSectionCommand { get; }

    public RelayCommand ToggleSidebarCommand { get; }

    public bool IsSidebarCollapsed
    {
        get => settingsRepository.GetFlag(SettingKeys.SidebarCollapsed, false);
        set
        {
            settingsRepository.SetFlag(SettingKeys.SidebarCollapsed, value);
            OnPropertyChanged();
        }
    }

    public void ShowCalls() => SelectedSection = Sections[2];

    public void ShowTi() => SelectedSection = Sections[3];

    public void ShowKnowledge() => SelectedSection = Sections[0];

    /// <summary>Bereich nach Position in der Navigation (0-basiert) anzeigen; ungültige Werte werden ignoriert.</summary>
    public void ShowSection(int index)
    {
        if (index >= 0 && index < Sections.Count)
        {
            SelectedSection = Sections[index];
        }
    }
}
