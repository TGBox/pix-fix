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

    public MainViewModel(AppPaths paths, KnowledgeViewModel knowledge, WorkspacesViewModel workspaces, CallsViewModel calls, ImportViewModel import)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(import);
        DataDirectory = paths.DataDirectory;

        // Nach einem Import zeigt die Wissensdatenbank sofort die übernommenen Einträge.
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(calls);
        import.ImportSucceeded += (_, _) =>
        {
            knowledge.Reload();
            workspaces.Reload();
            calls.Reload();
        };

        Sections =
        [
            new("Wissensdatenbank", "Fehlerbilder und Lösungen nachschlagen.", knowledge),
            new("Schreibbereiche", "Notizen, Anleitungen, Werkzeuge und PDF-Anhänge.", workspaces),
            new("Anrufe", "Anrufkarten, Rückruf-Erinnerungen, Vorlagen und Telefonie.", calls),
            new("Einstellungen", "Datenordner, Sicherung, Import aus PixFix 69.", import),
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
