using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PixFix.App.Help;

/// <summary>Bereich „Hilfe“: Kapitel zum Nachlesen, Sprung in den beschriebenen Bereich und Start der Einführungstour.</summary>
public sealed class HelpViewModel : ObservableObject
{
    private HelpTopic selectedTopic;

    public HelpViewModel()
    {
        selectedTopic = Topics[0];
        StartTourCommand = new RelayCommand(() => TourRequested?.Invoke(this, EventArgs.Empty));
        GoToSectionCommand = new RelayCommand(
            () =>
            {
                if (SelectedTopic.SectionIndex is { } index)
                {
                    SectionRequested?.Invoke(this, index);
                }
            });
    }

    /// <summary>„Einführungstour starten“.</summary>
    public event EventHandler? TourRequested;

    /// <summary>„Zum Bereich wechseln“ – Index des Bereichs in der Navigation.</summary>
    public event EventHandler<int>? SectionRequested;

    public IReadOnlyList<HelpTopic> Topics => HelpContent.Topics;

    public IReadOnlyList<KeyValuePair<string, string>> Shortcuts => HelpContent.Shortcuts;

    public HelpTopic SelectedTopic
    {
        get => selectedTopic;
        set => SetProperty(ref selectedTopic, value ?? Topics[0]);
    }

    public RelayCommand StartTourCommand { get; }

    public RelayCommand GoToSectionCommand { get; }
}
