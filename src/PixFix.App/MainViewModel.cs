using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.App.Calls;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using PixFix.App.Workspaces;
using PixFix.Infrastructure;

namespace PixFix.App;

/// <summary>Eintrag der linken Navigation. <paramref name="Content"/> ist das ViewModel des Bereichs (oder null = noch nicht umgesetzt).</summary>
public sealed record NavigationSection(string Title, string Description, object? Content);

public sealed class MainViewModel : ObservableObject
{
    private NavigationSection? selectedSection;

    public MainViewModel(AppPaths paths, KnowledgeViewModel knowledge, WorkspacesViewModel workspaces, CallsViewModel calls, SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(settings);
        DataDirectory = paths.DataDirectory;

        // Nach Import oder Wiederherstellung zeigen alle Bereiche sofort den neuen Datenbestand.
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(calls);
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
            new("Einstellungen", "Anrufe, Vorlagen, Sicherung, Import aus PixFix 69.", settings),
        ];
        selectedSection = Sections[0];

        // „OK“ in einer Rückruf-Erinnerung springt zu den Anrufen.
        calls.ShowCallsRequested += (_, _) => SelectedSection = Sections[2];
    }

    public ObservableCollection<NavigationSection> Sections { get; }

    public NavigationSection? SelectedSection
    {
        get => selectedSection;
        set => SetProperty(ref selectedSection, value);
    }

    public string DataDirectory { get; }
}
