using System.Windows;
using System.Windows.Controls;
using PixFix.App.Common;

namespace PixFix.App.Workspaces;

/// <summary>Ansicht der Schreibbereiche; der Code-Behind öffnet nur die Bereichsverwaltung.</summary>
public partial class WorkspacesView : UserControl, IShortcutTarget
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

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void CreateNew()
    {
        EditorPanel.FlushSave();
        viewModel?.NewEntryCommand.Execute(null);
    }

    private void OnAttachmentDragOver(object sender, DragEventArgs e)
    {
        e.Effects = viewModel?.AddAttachmentCommand.CanExecute(null) == true && DroppedPdfs(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnAttachmentDrop(object sender, DragEventArgs e)
    {
        if (viewModel?.AddAttachmentCommand.CanExecute(null) == true && DroppedPdfs(e) is { Count: > 0 } files)
        {
            viewModel.AddAttachments(files);
        }
    }

    private static List<string> DroppedPdfs(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

    private void OnManageWorkspaces(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not null)
        {
            new WorkspacesWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
