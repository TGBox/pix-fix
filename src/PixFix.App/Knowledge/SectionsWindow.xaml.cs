using System.Windows;
using System.Windows.Controls;
using PixFix.Infrastructure.Knowledge;

namespace PixFix.App.Knowledge;

/// <summary>Abschnitte der Wissensdatenbank umbenennen, sortieren und leere löschen (SPEC F1).</summary>
public partial class SectionsWindow : Window
{
    private readonly KnowledgeViewModel viewModel;

    public SectionsWindow(KnowledgeViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        Refresh(null);
    }

    private KnowledgeSectionInfo? Selected => Items.SelectedItem as KnowledgeSectionInfo;

    private void Refresh(string? selectId)
    {
        var sections = viewModel.LoadSections();
        Items.ItemsSource = sections;
        Items.SelectedItem = sections.FirstOrDefault(s => s.Id == selectId) ?? sections.FirstOrDefault();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NameBox.Text = Selected?.Name ?? string.Empty;
        Error.Text = string.Empty;
    }

    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Rand, Rahmen und senkrechte Bildlaufleiste abziehen; der Name bekommt den Rest, mindestens 120 px.
        var available = Items.ActualWidth - CountColumn.Width - SystemParameters.VerticalScrollBarWidth - 12;
        NameColumn.Width = Math.Max(120, available);
    }

    private void OnRename(object sender, RoutedEventArgs e) =>
        Apply(section => viewModel.ChangeSections(r => r.RenameSection(section.Id, NameBox.Text)));

    private void OnMoveUp(object sender, RoutedEventArgs e) =>
        Apply(section => viewModel.ChangeSections(r => r.MoveSection(section.Id, -1)));

    private void OnMoveDown(object sender, RoutedEventArgs e) =>
        Apply(section => viewModel.ChangeSections(r => r.MoveSection(section.Id, +1)));

    private void OnDelete(object sender, RoutedEventArgs e) =>
        Apply(section => viewModel.ChangeSections(r => r.DeleteSection(section.Id)));

    private void Apply(Func<KnowledgeSectionInfo, string?> change)
    {
        if (Selected is not { } section)
        {
            return;
        }

        var error = change(section);
        Refresh(section.Id);
        Error.Text = error ?? string.Empty;
    }
}
