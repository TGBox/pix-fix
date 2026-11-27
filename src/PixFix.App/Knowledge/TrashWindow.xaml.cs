using System.Windows;
using PixFix.Infrastructure.Knowledge;

namespace PixFix.App.Knowledge;

/// <summary>Papierkorb der Wissensdatenbank: wiederherstellen oder endgültig löschen (SPEC F1).</summary>
public partial class TrashWindow : Window
{
    private readonly KnowledgeViewModel viewModel;

    public TrashWindow(KnowledgeViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        Refresh();
    }

    private KnowledgeTrashItem? Selected => Items.SelectedItem as KnowledgeTrashItem;

    private void Refresh() => Items.ItemsSource = viewModel.LoadTrash();

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        viewModel.RestoreFromTrash(item.TrashId);
        Close();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"„{item.Title}“ endgültig löschen? Das kann nicht rückgängig gemacht werden.",
            "PixFix",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            viewModel.DeleteFromTrash(item.TrashId);
            Refresh();
        }
    }
}
