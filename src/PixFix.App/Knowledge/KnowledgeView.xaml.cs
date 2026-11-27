using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PixFix.App.Common;

namespace PixFix.App.Knowledge;

/// <summary>
/// Ansicht der Wissensdatenbank: Trefferliste, Kopf des Eintrags und der gemeinsame Editor.
/// Der Code-Behind öffnet nur Menüs und Dialoge; vorher wird der Editor gespeichert.
/// </summary>
public partial class KnowledgeView : UserControl, IShortcutTarget
{
    private ImageSearchWindow? openImageSearch;

    private KnowledgeViewModel? viewModel;

    public KnowledgeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => viewModel?.EnsureLoaded();

        InputBindings.Add(new KeyBinding(new RelayCommand(() => viewModel?.MoveUpCommand.Execute(null)), Key.Up, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => viewModel?.MoveDownCommand.Execute(null)), Key.Down, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => viewModel?.ImageSearchCommand.Execute(null)), Key.B, ModifierKeys.Control | ModifierKeys.Shift));
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void CreateNew()
    {
        EditorPanel.FlushSave();
        viewModel?.CreateEntry(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.TrashRequested -= OnTrashRequested;
            viewModel.SectionsRequested -= OnSectionsRequested;
            viewModel.ImageSearchRequested -= OnImageSearchRequested;
        }

        viewModel = e.NewValue as KnowledgeViewModel;
        if (viewModel is not null)
        {
            viewModel.TrashRequested += OnTrashRequested;
            viewModel.SectionsRequested += OnSectionsRequested;
            viewModel.ImageSearchRequested += OnImageSearchRequested;
        }
    }

    /// <summary>Menü „Leerer Eintrag“ plus alle Eintragsvorlagen.</summary>
    private void OnNewEntry(object sender, RoutedEventArgs e)
    {
        if (viewModel is not { } vm || sender is not Button button)
        {
            return;
        }

        EditorPanel.FlushSave();
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var empty = new MenuItem { Header = "Leerer Eintrag" };
        empty.Click += (_, _) => vm.CreateEntry(null);
        menu.Items.Add(empty);
        if (vm.EntryTemplates.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var template in vm.EntryTemplates)
            {
                var item = new MenuItem { Header = "Vorlage: " + template.Name.Replace("_", "__", StringComparison.Ordinal) };
                item.Click += (_, _) => vm.CreateEntry(template);
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    private void OnSectionsRequested(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not null)
        {
            new SectionsWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }

    private void OnImageSearchRequested(object? sender, EventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        var paste = viewModel.PasteOnNextImageSearch;
        viewModel.PasteOnNextImageSearch = false;

        // Bildsuche schon offen (z. B. erneut Strg+Alt+P): nach vorn holen und neu suchen.
        if (openImageSearch is { } open)
        {
            open.Activate();
            if (paste)
            {
                open.PasteFromClipboard();
            }

            return;
        }

        EditorPanel.FlushSave();
        var window = new ImageSearchWindow(viewModel.ImageSearch, viewModel, viewModel.Logger) { Owner = Window.GetWindow(this) };
        if (paste)
        {
            window.Loaded += (_, _) => window.PasteFromClipboard();
        }

        openImageSearch = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            openImageSearch = null;
        }
    }

    private void OnTrashRequested(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not null)
        {
            new TrashWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
