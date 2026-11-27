using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.App.Knowledge;
using PixFix.App.Settings;
using PixFix.Infrastructure;

namespace PixFix.App;

/// <summary>Eintrag der linken Navigation. <paramref name="Content"/> ist das ViewModel des Bereichs (oder null = noch nicht umgesetzt).</summary>
public sealed record NavigationSection(string Title, string Description, object? Content);

public sealed class MainViewModel : ObservableObject
{
    private NavigationSection? selectedSection;

    public MainViewModel(AppPaths paths, KnowledgeViewModel knowledge, ImportViewModel import)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(import);
        DataDirectory = paths.DataDirectory;

        // Nach einem Import zeigt die Wissensdatenbank sofort die übernommenen Einträge.
        import.ImportSucceeded += (_, _) => knowledge.Reload();

        Sections =
        [
            new("Wissensdatenbank", "Fehlerbilder und Lösungen nachschlagen.", knowledge),
            new("Schreibbereiche", "Notizen, Anleitungen und PDF-Anhänge (SPEC F3) – folgt in Phase 4.", null),
            new("Anrufe", "Anrufkarten, Rückruf-Erinnerungen, Vorlagen und Telefonie (SPEC F4–F7) – folgt in Phase 5.", null),
            new("Einstellungen", "Datenordner, Sicherung, Import aus PixFix 69.", import),
        ];
        selectedSection = Sections[0];
    }

    public ObservableCollection<NavigationSection> Sections { get; }

    public NavigationSection? SelectedSection
    {
        get => selectedSection;
        set => SetProperty(ref selectedSection, value);
    }

    public string DataDirectory { get; }
}
