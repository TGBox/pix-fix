using System.Globalization;
using System.Windows;
using PixFix.Infrastructure.Calls;

namespace PixFix.App.Calls;

/// <summary>Papierkorb der Anrufe: wiederherstellen oder endgültig löschen.</summary>
public partial class CallTrashWindow : Window
{
    private readonly CallsViewModel viewModel;

    private CallTrashWindow(CallsViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        Refresh();
    }

    public static void ShowFor(Window? owner, CallsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        new CallTrashWindow(viewModel) { Owner = owner }.ShowDialog();
    }

    private void Refresh() =>
        Items.ItemsSource = viewModel.LoadTrash().Select(t => new Row(t)).ToList();

    private List<Row> Selected => Items.SelectedItems.OfType<Row>().ToList();

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var rows = Selected;
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            viewModel.RestoreFromTrash(row.Item.TrashId);
        }

        Refresh();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var rows = Selected;
        if (rows.Count == 0
            || MessageBox.Show(this, $"{rows.Count} Anruf(e) endgültig löschen? Das kann nicht rückgängig gemacht werden.", "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var row in rows)
        {
            viewModel.DeleteFromTrash(row.Item.TrashId);
        }

        Refresh();
    }

    private sealed class Row(CallTrashItem item)
    {
        public CallTrashItem Item { get; } = item;

        public string Deleted => Item.DeletedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

        public string CustomerNumber => Item.CustomerNumber;

        public string Reason => Item.Reason.ReplaceLineEndings(" ");
    }
}
