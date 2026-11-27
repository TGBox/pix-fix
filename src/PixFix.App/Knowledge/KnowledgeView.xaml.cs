using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixFix.Core.Common;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Knowledge;

namespace PixFix.App.Knowledge;

/// <summary>
/// Ansicht der Wissensdatenbank. Der Code-Behind kümmert sich um alles, was am WPF-Editor hängt und sich nicht binden lässt:
/// Dokument laden und speichern (automatisch), Bilder einfügen und Fundstellen anspringen.
/// </summary>
public partial class KnowledgeView : UserControl
{
    private const int MaxHits = 500;

    /// <summary>Eingefügte Bilder werden höchstens so breit angezeigt (geräteunabhängige Pixel).</summary>
    private const double MaxImageWidth = 900;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer autosave;
    private KnowledgeViewModel? viewModel;
    private string? documentEntryId;
    private bool loadingDocument;
    private bool dirty;
    private List<TextRange> hits = [];
    private int currentHit = -1;
    private Window? hostWindow;

    public KnowledgeView()
    {
        InitializeComponent();
        autosave = new DispatcherTimer { Interval = AutosaveDelay };
        autosave.Tick += (_, _) => FlushSave();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => FlushSave();

        Editor.TextChanged += OnEditorTextChanged;
        DataObject.AddPastingHandler(Editor, OnPasting);
        Editor.PreviewDragOver += OnEditorDragOver;
        Editor.PreviewDrop += OnEditorDrop;

        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveHit(+1)), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveHit(-1)), Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(FlushSave), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => viewModel?.MoveUpCommand.Execute(null)), Key.Up, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => viewModel?.MoveDownCommand.Execute(null)), Key.Down, ModifierKeys.Alt));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        viewModel?.EnsureLoaded();
        if (hostWindow is null)
        {
            hostWindow = Window.GetWindow(this);
            if (hostWindow is not null)
            {
                // Beim Schließen des Programms nichts verlieren.
                hostWindow.Closing += (_, _) => FlushSave();
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.TrashRequested -= OnTrashRequested;
            viewModel.SectionsRequested -= OnSectionsRequested;
        }

        viewModel = e.NewValue as KnowledgeViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.TrashRequested += OnTrashRequested;
            viewModel.SectionsRequested += OnSectionsRequested;
            ShowDocument(viewModel.SelectedHit?.Id, viewModel.Document);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KnowledgeViewModel.SelectedHit):
                // Vor dem Wechsel zum nächsten Eintrag den bisherigen speichern.
                if (viewModel?.SelectedHit?.Id != documentEntryId)
                {
                    FlushSave();
                }

                break;
            case nameof(KnowledgeViewModel.Document):
                ShowDocument(viewModel?.SelectedHit?.Id, viewModel?.Document);
                break;
            case nameof(KnowledgeViewModel.Query):
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(HighlightQuery));
                break;
        }
    }

    // ------------------------------------------------------------------ Laden und Speichern

    private void ShowDocument(string? entryId, KnowledgeDocument? document)
    {
        FlushSave();
        loadingDocument = true;
        try
        {
            documentEntryId = document is null ? null : entryId;
            Editor.Document = CreateFlowDocument(document);
            Editor.CaretPosition = Editor.Document.ContentStart;
            dirty = false;
        }
        finally
        {
            // TextChanged kann nachgelagert ausgelöst werden; erst danach zählen Änderungen.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => loadingDocument = false));
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(HighlightQuery));
    }

    private FlowDocument CreateFlowDocument(KnowledgeDocument? document)
    {
        FlowDocument flow;
        try
        {
            if (document is null)
            {
                return new FlowDocument { PagePadding = new Thickness(12) };
            }

            switch (document.Format)
            {
                case ContentFormat.Xaml:
                    flow = FlowDocumentStore.Load(document.Text, id => viewModel?.GetAssetFile(id));
                    break;
                case ContentFormat.Rtf:
                    flow = new FlowDocument();
                    var range = new TextRange(flow.ContentStart, flow.ContentEnd);
                    using (var stream = new MemoryStream(Encoding.Latin1.GetBytes(document.Text)))
                    {
                        range.Load(stream, DataFormats.Rtf);
                    }

                    AssignLegacyImageAssets(flow, document.PictureAssetIds);
                    break;
                default:
                    flow = new FlowDocument(new Paragraph(new Run(document.Text)));
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or System.Windows.Markup.XamlParseException)
        {
            flow = new FlowDocument(new Paragraph(new Run("Das Dokument konnte nicht angezeigt werden: " + ex.Message)));
            documentEntryId = null; // Fehleranzeige niemals über den echten Inhalt speichern.
        }

        flow.PagePadding = new Thickness(12);
        return flow;
    }

    /// <summary>
    /// Ordnet den Bildern eines übernommenen RTF-Dokuments ihre Originaldateien zu, damit sie beim ersten Speichern nicht
    /// neu kodiert werden. Nur wenn die Anzahl übereinstimmt – sonst könnten Bilder vertauscht werden.
    /// </summary>
    private static void AssignLegacyImageAssets(FlowDocument flow, IReadOnlyList<string?> assetIds)
    {
        var images = FlowDocumentStore.Images(flow).ToList();
        if (images.Count != assetIds.Count)
        {
            return;
        }

        for (var i = 0; i < images.Count; i++)
        {
            if (assetIds[i] is { } id)
            {
                FlowDocumentStore.SetAssetId(images[i], id);
            }
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (loadingDocument || documentEntryId is null || viewModel is not { CanEdit: true })
        {
            return;
        }

        dirty = true;
        autosave.Stop();
        autosave.Start();
    }

    /// <summary>Speichert ausstehende Änderungen sofort (Autosave, Eintragswechsel, Strg+S, Schließen).</summary>
    private void FlushSave()
    {
        autosave.Stop();
        if (!dirty || documentEntryId is null || viewModel is null)
        {
            return;
        }

        dirty = false;
        var saved = FlowDocumentStore.Save(Editor.Document, bitmap => viewModel.StoreImage(FlowDocumentStore.EncodePng(bitmap)));
        viewModel.SaveContent(documentEntryId, saved);
    }

    // ------------------------------------------------------------------ Bilder einfügen

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;

        // Formatierten Text (z. B. aus Word) übernimmt der Editor selbst, auch mit Bildern.
        if (data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.Xaml))
        {
            return;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsImageFile))
        {
            e.CancelCommand();
            foreach (var file in files.Where(IsImageFile))
            {
                InsertImage(FlowDocumentStore.LoadBitmap(file));
            }

            return;
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } bitmap)
        {
            // Screenshot aus der Zwischenablage.
            e.CancelCommand();
            InsertImage(bitmap);
        }
    }

    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (DroppedImageFiles(e).Count > 0 && viewModel is { CanEdit: true })
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        var files = DroppedImageFiles(e);
        if (files.Count == 0 || viewModel is not { CanEdit: true })
        {
            return;
        }

        e.Handled = true;
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: true) is { } position)
        {
            Editor.CaretPosition = position;
        }

        foreach (var file in files)
        {
            InsertImage(FlowDocumentStore.LoadBitmap(file));
        }
    }

    private void OnInsertImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Bild einfügen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            InsertImage(FlowDocumentStore.LoadBitmap(file));
        }
    }

    /// <summary>Speichert das Bild sofort in der Ablage und fügt es an der Schreibmarke ein.</summary>
    private void InsertImage(BitmapSource bitmap)
    {
        if (viewModel is not { CanEdit: true } vm || documentEntryId is null)
        {
            return;
        }

        var assetId = vm.StoreImage(FlowDocumentStore.EncodePng(bitmap));
        var file = vm.GetAssetFile(assetId);
        var image = new Image
        {
            Source = file is null ? bitmap : FlowDocumentStore.LoadBitmap(file),
            Stretch = System.Windows.Media.Stretch.Uniform,
            Width = Math.Min(MaxImageWidth, bitmap.PixelWidth),
        };
        FlowDocumentStore.SetAssetId(image, assetId);

        Editor.BeginChange();
        try
        {
            if (!Editor.Selection.IsEmpty)
            {
                Editor.Selection.Text = string.Empty;
            }

            var container = new InlineUIContainer(image, Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward));
            Editor.CaretPosition = container.ElementEnd;
        }
        finally
        {
            Editor.EndChange();
        }

        Editor.Focus();
    }

    private static List<string> DroppedImageFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(IsImageFile).ToList()
            : [];

    private static bool IsImageFile(string file) =>
        ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Neuer Eintrag, Kopier-Buttons, Abschnitte

    /// <summary>Menü „Leerer Eintrag“ plus alle Eintragsvorlagen.</summary>
    private void OnNewEntry(object sender, RoutedEventArgs e)
    {
        if (viewModel is not { } vm || sender is not Button button)
        {
            return;
        }

        FlushSave();
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

    /// <summary>Markierten Text als Kopier-Button speichern; das Vorkommen merkt sich, welche Stelle gemeint war.</summary>
    private void OnAddCopyAction(object sender, RoutedEventArgs e)
    {
        var text = Editor.Selection.Text.Trim();
        if (text.Length == 0)
        {
            if (viewModel is not null)
            {
                viewModel.Status = "Bitte zuerst den Text markieren, der kopiert werden soll.";
            }

            return;
        }

        var before = new TextRange(Editor.Document.ContentStart, Editor.Selection.Start).Text;
        var occurrence = 0;
        for (var index = before.IndexOf(text, StringComparison.Ordinal); index >= 0; index = before.IndexOf(text, index + text.Length, StringComparison.Ordinal))
        {
            occurrence++;
        }

        viewModel?.AddCopyAction(text, occurrence);
    }

    private void OnSectionsRequested(object? sender, EventArgs e)
    {
        FlushSave();
        if (viewModel is null)
        {
            return;
        }

        var window = new SectionsWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    // ------------------------------------------------------------------ Papierkorb

    private void OnTrashRequested(object? sender, EventArgs e)
    {
        FlushSave();
        if (viewModel is null)
        {
            return;
        }

        var window = new TrashWindow(viewModel) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    // ------------------------------------------------------------------ Fundstellen

    /// <summary>Sucht alle Fundstellen des Suchbegriffs (bzw. seiner Wörter) und springt zur ersten.</summary>
    private void HighlightQuery()
    {
        hits = [];
        currentHit = -1;
        var query = viewModel?.Query.Trim() ?? string.Empty;
        if (query.Length > 0 && Editor.Document is not null)
        {
            hits = FindAll(Editor.Document, query);
            if (hits.Count == 0)
            {
                foreach (var token in KnowledgeSearch.Tokens(query))
                {
                    hits = FindAll(Editor.Document, token);
                    if (hits.Count > 0)
                    {
                        break;
                    }
                }
            }
        }

        HitNavigation.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hits.Count > 0)
        {
            MoveHit(+1);
        }
    }

    private void OnNextHit(object sender, RoutedEventArgs e) => MoveHit(+1);

    private void OnPreviousHit(object sender, RoutedEventArgs e) => MoveHit(-1);

    /// <summary>Markiert eine Fundstelle (nur Auswahl und Scrollposition – der Inhalt bleibt unverändert).</summary>
    private void MoveHit(int direction)
    {
        if (hits.Count == 0)
        {
            return;
        }

        currentHit = (currentHit + direction + hits.Count) % hits.Count;
        var hit = hits[currentHit];
        Editor.Selection.Select(hit.Start, hit.End);
        HitCounter.Text = $"Treffer {currentHit + 1} von {hits.Count}{(hits.Count == MaxHits ? "+" : string.Empty)}";

        var rect = hit.Start.GetCharacterRect(LogicalDirection.Forward);
        if (!rect.IsEmpty)
        {
            Editor.ScrollToVerticalOffset(Math.Max(0, Editor.VerticalOffset + rect.Top - 60));
        }
    }

    /// <summary>Fundstellen innerhalb einzelner Textabschnitte (Runs), ohne Groß-/Kleinschreibung.</summary>
    private static List<TextRange> FindAll(FlowDocument document, string text)
    {
        var result = new List<TextRange>();
        var pointer = document.ContentStart;
        while (pointer is not null && result.Count < MaxHits)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                var index = 0;
                while ((index = run.IndexOf(text, index, StringComparison.CurrentCultureIgnoreCase)) >= 0 && result.Count < MaxHits)
                {
                    var start = pointer.GetPositionAtOffset(index);
                    var end = start?.GetPositionAtOffset(text.Length);
                    if (start is not null && end is not null)
                    {
                        result.Add(new TextRange(start, end));
                    }

                    index += text.Length;
                }
            }

            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }

        return result;
    }
}
