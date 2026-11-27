using System.Windows;
using System.Windows.Controls;

namespace PixFix.App.Workspaces;

/// <summary>Ansicht der Schreibbereiche; der Code-Behind öffnet nur die Bereichsverwaltung.</summary>
public partial class WorkspacesView : UserControl
{
    private WorkspacesViewModel? viewModel;

    public WorkspacesView()
    {
        InitializeComponent();
        Loaded += (_, _) => viewModel?.EnsureLoaded();
        DataContextChanged += (_, e) =>
        {
            if (viewModel is not null)
            {
                viewModel.ManageWorkspacesRequested -= OnManageWorkspaces;
            }

            viewModel = e.NewValue as WorkspacesViewModel;
            if (viewModel is not null)
            {
                viewModel.ManageWorkspacesRequested += OnManageWorkspaces;
            }
        };
    }

    private void OnManageWorkspaces(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not null)
        {
            new WorkspacesWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
