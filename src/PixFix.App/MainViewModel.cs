using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixFix.App.Calls;
using PixFix.App.Common;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using PixFix.App.Ti;
using PixFix.App.Workspaces;
using PixFix.Infrastructure;

namespace PixFix.App;

/// <summary>Eintrag der linken Navigation. <paramref name="Content"/> ist das ViewModel des Bereichs (oder null = noch nicht umgesetzt).</summary>
public sealed record NavigationSection(string Title, string Description, object? Content);

public sealed class MainViewModel : ObservableObject
{
    private NavigationSection? selectedSection;

    public MainViewModel(AppPaths paths, KnowledgeViewModel knowledge, WorkspacesViewModel workspaces, CallsViewModel calls, TiMonitorViewModel ti, SettingsViewModel settings, UndoService undo)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(settings);
        DataDirectory = paths.DataDirectory;

        // Nach Import oder Wiederherstellung zeigen alle Bereiche sofort den neuen Datenbestand.
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(ti);
        Ti = ti;
        Undo = undo;
        settings.DataReplaced += (_, _) =>
        {
            knowledge.Reload();
            workspaces.Reload();
            calls.Reload();
            settings.Calls.Reload();
            settings.Backup.Refresh();
        };

        // Bearbeitete Eintragsvorlagen sofort in der Wissensdatenbank anbieten.
        settings.Calls.TemplatesChanged += (_, _) => knowledge.ReloadTemplates();

        Sections =
        [
            new("Wissensdatenbank", "Fehlerbilder und Lösungen nachschlagen.", knowledge),
            new("Schreibbereiche", "Notizen, Anleitungen, Werkzeuge und PDF-Anhänge.", workspaces),
            new("Anrufe", "Anrufkarten, Rückruf-Erinnerungen, Vorlagen und Telefonie.", calls),
            new("TI-Monitor", "Störungen der Telematikinfrastruktur (gematik).", ti),
            new("Einstellungen", "Anrufe, Vorlagen, Sicherung, Import aus PixFix 69.", settings),
        ];
        selectedSection = Sections[0];

        // „OK“ in einer Rückruf-Erinnerung springt zu den Anrufen.
        calls.ShowCallsRequested += (_, _) => SelectedSection = Sections[2];

        // Klick auf das TI-Symbol öffnet den Monitor und bestätigt eine neue Störung.
        ShowTiCommand = new RelayCommand(() => SelectedSection = Sections[3]);

        // Strg+1 … Strg+5 wechselt den Bereich.
        ShowSectionCommand = new RelayCommand<string>(index =>
        {
            if (int.TryParse(index, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) && i >= 1 && i <= Sections.Count)
            {
                SelectedSection = Sections[i - 1];
            }
        });
    }

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

    public RelayCommand ShowTiCommand { get; }

    public RelayCommand<string> ShowSectionCommand { get; }

    public void ShowCalls() => SelectedSection = Sections[2];

    public void ShowTi() => SelectedSection = Sections[3];

    public void ShowKnowledge() => SelectedSection = Sections[0];
}
