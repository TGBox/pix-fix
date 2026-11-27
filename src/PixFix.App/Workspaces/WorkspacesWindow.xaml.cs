using System.Windows;
using System.Windows.Controls;
using PixFix.Core.Workspaces;

namespace PixFix.App.Workspaces;

/// <summary>Schreibbereiche anlegen, umbenennen, sortieren, schützen und leere löschen (SPEC F3).</summary>
public partial class WorkspacesWindow : Window
{
    private readonly WorkspacesViewModel viewModel;

    public WorkspacesWindow(WorkspacesViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        Refresh(viewModel.SelectedWorkspace?.Id);
    }

    private Workspace? Selected => Items.SelectedItem as Workspace;

    private void Refresh(string? selectId)
    {
        var workspaces = viewModel.LoadWorkspaces();
        Items.ItemsSource = workspaces;
        Items.SelectedItem = workspaces.FirstOrDefault(w => w.Id == selectId) ?? workspaces.FirstOrDefault();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NameBox.Text = Selected?.Title ?? string.Empty;
        IconBox.Text = Selected?.Icon ?? string.Empty;
        ProtectedBox.IsChecked = Selected?.IsProtected ?? false;
        Error.Text = string.Empty;
    }

    private void OnSave(object sender, RoutedEventArgs e) =>
        Apply(w => viewModel.ChangeWorkspaces(r => r.UpdateWorkspace(w.Id, NameBox.Text, IconBox.Text, ProtectedBox.IsChecked == true)), Selected?.Id);

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        string? created = null;
        var error = viewModel.ChangeWorkspaces(r => created = r.CreateWorkspace(NameBox.Text, IconBox.Text));
        Refresh(created);
        Error.Text = error ?? string.Empty;
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) =>
        Apply(w => viewModel.ChangeWorkspaces(r => r.MoveWorkspace(w.Id, -1)), Selected?.Id);

    private void OnMoveDown(object sender, RoutedEventArgs e) =>
        Apply(w => viewModel.ChangeWorkspaces(r => r.MoveWorkspace(w.Id, +1)), Selected?.Id);

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is { } workspace
            && MessageBox.Show(this, $"Bereich „{workspace.Title}“ löschen?", "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Apply(w => viewModel.ChangeWorkspaces(r => r.DeleteWorkspace(w.Id)), null);
        }
    }

    private void Apply(Func<Workspace, string?> change, string? selectAfter)
    {
        if (Selected is not { } workspace)
        {
            return;
        }

        var error = change(workspace);
        Refresh(selectAfter);
        Error.Text = error ?? string.Empty;
    }
}
