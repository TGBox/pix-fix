using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PixFix.App.Common;

/// <summary>Zeile im Papierkorb: Id plus bis zu drei Anzeigespalten.</summary>
public sealed record TrashRow(string Id, string Deleted, string First, string Second);

/// <summary>Einfacher Papierkorb (Liste, Wiederherstellen, endgültig löschen) – derzeit für die Reiter der Schreibbereiche.</summary>
public sealed class TrashListWindow : Window
{
    private readonly Func<IReadOnlyList<TrashRow>> load;
    private readonly Action<string> restore;
    private readonly Action<string> delete;
    private readonly ListView list;

    private TrashListWindow(
        string title,
        string hint,
        (string First, string Second) headers,
        Func<IReadOnlyList<TrashRow>> load,
        Action<string> restore,
        Action<string> delete)
    {
        this.load = load;
        this.restore = restore;
        this.delete = delete;
        Title = title;
        Width = 680;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Gelöscht", Width = 130, DisplayMemberBinding = new Binding(nameof(TrashRow.Deleted)) });
        view.Columns.Add(new GridViewColumn { Header = headers.First, Width = 300, DisplayMemberBinding = new Binding(nameof(TrashRow.First)) });
        view.Columns.Add(new GridViewColumn { Header = headers.Second, Width = 200, DisplayMemberBinding = new Binding(nameof(TrashRow.Second)) });
        list = new ListView { View = view, SelectionMode = SelectionMode.Extended };
        list.MouseDoubleClick += (_, _) => Restore();

        var restoreButton = new Button { Content = "_Wiederherstellen", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        restoreButton.Click += (_, _) => Restore();
        var deleteButton = new Button { Content = "Endgültig _löschen", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        deleteButton.Click += (_, _) => DeleteSelected();
        var close = new Button { Content = "Schließen", Padding = new Thickness(10, 2, 10, 2), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(restoreButton);
        buttons.Children.Add(deleteButton);
        buttons.Children.Add(close);

        var panel = new DockPanel { Margin = new Thickness(12) };
        var hintText = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hintText, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(hintText);
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        Content = panel;
        Refresh();
    }

    public static void ShowFor(
        Window? owner,
        string title,
        string hint,
        (string First, string Second) headers,
        Func<IReadOnlyList<TrashRow>> load,
        Action<string> restore,
        Action<string> delete) =>
        new TrashListWindow(title, hint, headers, load, restore, delete) { Owner = owner }.ShowDialog();

    private void Refresh() => list.ItemsSource = load();

    private List<TrashRow> Selected => list.SelectedItems.OfType<TrashRow>().ToList();

    private void Restore()
    {
        foreach (var row in Selected)
        {
            try
            {
                restore(row.Id);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        Refresh();
    }

    private void DeleteSelected()
    {
        var rows = Selected;
        if (rows.Count == 0
            || MessageBox.Show(this, $"{rows.Count} Element(e) endgültig löschen? Das kann nicht rückgängig gemacht werden.", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var row in rows)
        {
            delete(row.Id);
        }

        Refresh();
    }
}
