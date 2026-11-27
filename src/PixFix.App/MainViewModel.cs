using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.Infrastructure;

namespace PixFix.App;

public sealed record NavigationSection(string Title, string Description);

public sealed class MainViewModel : ObservableObject
{
    private NavigationSection? selectedSection;

    public MainViewModel(AppPaths paths, ImportViewModel import)
    {
        ArgumentNullException.ThrowIfNull(paths);
        DataDirectory = paths.DataDirectory;
        Import = import;
        selectedSection = Sections[0];
    }

    public ImportViewModel Import { get; }

    public ObservableCollection<NavigationSection> Sections { get; } =
    [
        new("Wissensdatenbank", "Fehlerbilder, Lösungen, Text- und Bildsuche (SPEC F1, F2) – Phase 2/3."),
        new("Schreibbereiche", "Notizen, Anleitungen und PDF-Anhänge (SPEC F3) – Phase 4."),
        new("Anrufe", "Anrufkarten, Rückruf-Erinnerungen, Vorlagen und Telefonie (SPEC F4–F7) – Phase 5."),
        new("Einstellungen", "Datenordner, Sicherung, Import aus PixFix 69 (SPEC F11–F13)."),
    ];

    public NavigationSection? SelectedSection
    {
        get => selectedSection;
        set
        {
            if (SetProperty(ref selectedSection, value))
            {
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsSettingsSelected => ReferenceEquals(SelectedSection, Sections[^1]);

    public string DataDirectory { get; }
}
