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
using PixFix.App.Common;
using PixFix.Core.Common;
using PixFix.Core.Knowledge;
using PixFix.Core.Templates;
using PixFix.Infrastructure.Templates;

namespace PixFix.App.Editing;

/// <summary>
/// Wiederverwendbarer Editor für Wissensdatenbank und Schreibbereiche. Kümmert sich um alles, was am WPF-RichTextBox hängt
/// und sich nicht binden lässt: Dokument laden und automatisch speichern, Bilder einfügen, Kopier-Buttons anlegen und
/// Fundstellen anspringen (nur Auswahl und Scrollposition – der Inhalt wird dabei nie verändert).
/// </summary>
public partial class DocumentEditor : UserControl
{
    private const int MaxHits = 500;

    /// <summary>Eingefügte Bilder werden höchstens so breit angezeigt (geräteunabhängige Pixel).</summary>
    private const double MaxImageWidth = 900;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(2);

    private static readonly System.Windows.Media.Brush CodeBackground = Frozen(System.Windows.Media.Color.FromRgb(241, 245, 249));
    private static readonly System.Windows.Media.Brush CodeForeground = Frozen(System.Windows.Media.Color.FromRgb(30, 41, 59));

    private readonly DispatcherTimer autosave;
    private IDocumentEditorHost? host;
    private string? documentEntryId;
    private bool loadingDocument;
    private bool dirty;
    private List<TextRange> hits = [];
    private int currentHit = -1;
    private Window? hostWindow;

    public DocumentEditor()
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        FlushSave();
        if (host is not null)
        {
            host.PropertyChanged -= OnHostPropertyChanged;
        }

        host = e.NewValue as IDocumentEditorHost;
        if (host is not null)
        {
            host.PropertyChanged += OnHostPropertyChanged;
        }

        ShowDocument(host?.Document);
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IDocumentEditorHost.Document):
                ShowDocument(host?.Document);
                break;
            case nameof(IDocumentEditorHost.PendingImageAssetId):
                FocusPendingImage();
                break;
            case nameof(IDocumentEditorHost.Query):
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(HighlightQuery));
                break;
        }
    }

    private void ShowDocument(EditorDocument? document)
    {
        FlushSave();
        loadingDocument = true;
        try
        {
            documentEntryId = document?.EntryId;
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
        FocusPendingImage();
    }

    /// <summary>Springt zu dem Bild, das die Bildsuche geöffnet hat (sobald das passende Dokument geladen ist).</summary>
    private void FocusPendingImage()
    {
        if (host?.PendingImageAssetId is not { } assetId || Editor.Document is null)
        {
            return;
        }

        // Ist das Bild (noch) nicht im angezeigten Dokument, wird erst das nächste geladene Dokument geprüft –
        // das Dokument des neu gewählten Eintrags kommt oft erst kurz danach.
        var image = FlowDocumentStore.Images(Editor.Document).FirstOrDefault(i => FlowDocumentStore.AssetId(i) == assetId);
        if (image is null)
        {
            return;
        }

        host.PendingImageAssetId = null;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            image.BringIntoView();
            if (LogicalTreeHelper.GetParent(image) is TextElement container)
            {
                Editor.Selection.Select(container.ContentStart, container.ContentEnd);
            }
        }));
    }

    private FlowDocument CreateFlowDocument(EditorDocument? document)
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
                    flow = FlowDocumentStore.Load(document.Text, id => host?.GetAssetFile(id));
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
        if (loadingDocument || documentEntryId is null || host is not { CanEdit: true })
        {
            return;
        }

        dirty = true;
        autosave.Stop();
        autosave.Start();
    }

    /// <summary>Speichert ausstehende Änderungen sofort (Autosave, Dokumentwechsel, Strg+S, Schließen, vor Dialogen).</summary>
    public void FlushSave()
    {
        autosave.Stop();
        if (!dirty || documentEntryId is null || host is null)
        {
            return;
        }

        dirty = false;
        var saved = FlowDocumentStore.Save(Editor.Document, bitmap => host.StoreImage(FlowDocumentStore.EncodePng(bitmap)));
        host.SaveContent(documentEntryId, saved);
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
        if (DroppedImageFiles(e).Count > 0 && host is { CanEdit: true })
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        var files = DroppedImageFiles(e);
        if (files.Count == 0 || host is not { CanEdit: true })
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
        if (host is not { CanEdit: true } vm || documentEntryId is null)
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

    /// <summary>Markierten Text als Kopier-Button speichern; das Vorkommen merkt sich, welche Stelle gemeint war.</summary>
    private void OnAddCopyAction(object sender, RoutedEventArgs e)
    {
        var text = Editor.Selection.Text.Trim();
        if (text.Length == 0)
        {
            if (host is not null)
            {
                host.Status = "Bitte zuerst den Text markieren, der kopiert werden soll.";
            }

            return;
        }

        var before = new TextRange(Editor.Document.ContentStart, Editor.Selection.Start).Text;
        var occurrence = 0;
        for (var index = before.IndexOf(text, StringComparison.Ordinal); index >= 0; index = before.IndexOf(text, index + text.Length, StringComparison.Ordinal))
        {
            occurrence++;
        }

        host?.AddCopyAction(text, occurrence);
    }

    /// <summary>Das angezeigte Dokument (z. B. für „Reiter per Mail“); vorher <see cref="FlushSave"/> aufrufen.</summary>
    public FlowDocument CurrentDocument => Editor.Document;

    // ------------------------------------------------------------------ Lösungsbausteine

    /// <summary>Vorlagenablage für die Bausteine (beim Programmstart gesetzt; Editor-Instanzen entstehen in XAML).</summary>
    public static TemplateRepository? SnippetStore { get; set; }

    /// <summary>Öffnet die Bausteinverwaltung (Vorlagenfenster); beim Programmstart gesetzt.</summary>
    public static Action<Window?>? ManageSnippets { get; set; }

    /// <summary>Menü wie in PixFix 69: Bausteine (⌘ = Code-Block), aus Auswahl speichern, verwalten.</summary>
    private void OnSnippets(object sender, RoutedEventArgs e)
    {
        if (SnippetStore is not { } store || sender is not Button button)
        {
            return;
        }

        var snippets = store.LoadByKind(TemplateKind.Snippet).Select(t => (Template: t, Code: false))
            .Concat(store.LoadByKind(TemplateKind.CodeSnippet).Select(t => (Template: t, Code: true)))
            .OrderBy(s => s.Template.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        if (snippets.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Noch keine Lösungsbausteine", IsEnabled = false });
        }

        foreach (var (template, code) in snippets)
        {
            var item = new MenuItem { Header = new TextBlock { Text = (code ? "⌘ " : string.Empty) + template.Name } };
            item.Click += (_, _) => InsertSnippet(template, code);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Aus Auswahl als Baustein speichern …", IsEnabled = !Editor.Selection.IsEmpty };
        save.Click += (_, _) => SaveSnippetFromSelection(store);
        menu.Items.Add(save);
        var manage = new MenuItem { Header = "Bausteine verwalten …" };
        manage.Click += (_, _) => ManageSnippets?.Invoke(Window.GetWindow(this));
        menu.Items.Add(manage);
        menu.IsOpen = true;
    }

    /// <summary>Baustein an der Cursorposition einfügen (ersetzt eine Auswahl); Code-Bausteine als Consolas-Block.</summary>
    private void InsertSnippet(Template template, bool code)
    {
        if (host is not { CanEdit: true } || Editor.IsReadOnly)
        {
            return;
        }

        var text = template.Body.ReplaceLineEndings("\r\n");
        Editor.Selection.Text = text;
        if (code && text.Length > 0)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
            Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, CodeBackground);
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, CodeForeground);
        }

        Editor.Selection.Select(Editor.Selection.End, Editor.Selection.End);
        Editor.Focus();
        host.Status = "Baustein eingefügt: " + template.Name;
    }

    private void SaveSnippetFromSelection(TemplateRepository store)
    {
        var body = Editor.Selection.Text.TrimEnd('\r', '\n');
        if (body.Trim().Length == 0)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (TextPrompt.Ask(owner, "Lösungsbaustein speichern", "Name des Bausteins:") is not { } name)
        {
            return;
        }

        var answer = MessageBox.Show(
            owner!,
            "Soll dieser Baustein beim Einfügen als Code-/Befehlsblock formatiert werden?",
            "PixFix – Lösungsbaustein",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            return;
        }

        var created = store.Create(answer == MessageBoxResult.Yes ? TemplateKind.CodeSnippet : TemplateKind.Snippet, name);
        store.Save(created with { Body = body });
        if (host is not null)
        {
            host.Status = "Lösungsbaustein gespeichert: " + name;
        }
    }

    private static System.Windows.Media.Brush Frozen(System.Windows.Media.Color color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ Fundstellen

    /// <summary>Sucht alle Fundstellen des Suchbegriffs (bzw. seiner Wörter) und springt zur ersten.</summary>
    private void HighlightQuery()
    {
        hits = [];
        currentHit = -1;
        var query = host?.Query.Trim() ?? string.Empty;
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
