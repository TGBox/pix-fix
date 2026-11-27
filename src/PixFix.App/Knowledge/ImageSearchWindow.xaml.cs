using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixFix.App.Editing;
using PixFix.Infrastructure.Images;

namespace PixFix.App.Knowledge;

/// <summary>Eine Zeile der Trefferliste.</summary>
public sealed record ImageHitItem(string EntryId, string AssetId, string Title, string SectionName, string Details, string File)
{
    /// <summary>Kleine Vorschau; wird erst beim Anzeigen geladen.</summary>
    public ImageSource? Thumbnail => LoadImage(File, 220);

    public static BitmapImage? LoadImage(string file, int decodeWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Bildsuche (SPEC F2) wie in PixFix 69: Screenshot einfügen oder Bild ablegen; einfacher Klick zeigt die Vorschau,
/// Doppelklick/Enter/„Treffer öffnen“ öffnet den Eintrag an der Bildstelle und schließt das Fenster.
/// </summary>
public partial class ImageSearchWindow : Window
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff"];

    private readonly ImageSearchService search;
    private readonly KnowledgeViewModel knowledge;
    private readonly ILogger logger;
    private bool searching;

    public ImageSearchWindow(ImageSearchService search, KnowledgeViewModel knowledge, ILogger logger)
    {
        InitializeComponent();
        this.search = search;
        this.knowledge = knowledge;
        this.logger = logger;

        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(PasteFromClipboard), Key.V, ModifierKeys.Control));
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void OnPaste(object sender, RoutedEventArgs e) => PasteFromClipboard();

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            var file = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(IsImageFile);
            if (file is not null)
            {
                _ = RunAsync(file, temporary: false);
                return;
            }
        }

        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
        {
            StatusText.Text = "In der Zwischenablage ist kein Bild. Bitte zuerst einen Screenshot kopieren (z. B. Windows+Umschalt+S).";
            return;
        }

        _ = RunAsync(SaveOnWhite(image), temporary: true);
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Bild suchen", Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true)
        {
            _ = RunAsync(dialog.FileName, temporary: false);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedImage(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedImage(e) is { } file)
        {
            _ = RunAsync(file, temporary: false);
        }
    }

    private async Task RunAsync(string file, bool temporary)
    {
        if (searching)
        {
            return;
        }

        searching = true;
        Cursor = Cursors.Wait;
        try
        {
            QueryPreview.Source = ImageHitItem.LoadImage(file, 0);
            DropHint.Visibility = Visibility.Collapsed;
            StatusText.Text = "Bildtext und Bildähnlichkeit werden verglichen …";
            Results.ItemsSource = null;

            var result = await search.SearchAsync(file).ConfigureAwait(true);

            RecognizedText.Text = result.RecognizedText;
            RecognizedExpander.Visibility = string.IsNullOrWhiteSpace(result.RecognizedText) ? Visibility.Collapsed : Visibility.Visible;
            var items = result.Hits.Select(h => new ImageHitItem(
                h.Hit.Entry.Id,
                h.Hit.AssetId,
                h.Hit.Entry.Title + (h.Hit.Entry.IsArchived ? " (archiviert)" : string.Empty),
                h.Hit.Entry.SectionName,
                $"Bild {h.Hit.Visual} % · Text {h.Hit.TextCoverage} %",
                h.ImageFile)).ToList();
            Results.ItemsSource = items;
            StatusText.Text = items.Count == 0
                ? "Keine gespeicherten Fehlerbilder gefunden."
                : $"{items.Count} Treffer – einfacher Klick zeigt das Bild, Doppelklick öffnet den Eintrag.";
            if (items.Count > 0)
            {
                Results.SelectedIndex = 0;
                Results.Focus();
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            logger.LogWarning(ex, "Bildsuche fehlgeschlagen für {File}", file);
            StatusText.Text = "Das Bild konnte nicht durchsucht werden: " + ex.Message;
        }
        finally
        {
            ClearValue(CursorProperty);
            searching = false;
            if (temporary)
            {
                TryDelete(file);
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = Results.SelectedItem as ImageHitItem;
        OpenButton.IsEnabled = item is not null;
        PreviewTitle.Text = item?.Title ?? string.Empty;
        HitPreview.Source = item is null ? null : ImageHitItem.LoadImage(item.File, 0);
    }

    private void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOpen(sender, e);
            e.Handled = true;
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not ImageHitItem item)
        {
            return;
        }

        knowledge.OpenImageHit(item.EntryId, item.AssetId);
        Close();
    }

    /// <summary>Wie PixFix 69: Screenshot auf weißen Hintergrund zeichnen und als PNG speichern.</summary>
    private static string SaveOnWhite(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            context.DrawRectangle(Brushes.White, null, rect);
            context.DrawImage(image, rect);
        }

        var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var file = Path.Combine(Path.GetTempPath(), "PixFix-Bildsuche-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, FlowDocumentStore.EncodePng(target));
        return file;
    }

    private static string? DroppedImage(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.FirstOrDefault(IsImageFile)
            : null;

    private static bool IsImageFile(string file) =>
        ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Temporäre Datei {File} konnte nicht gelöscht werden", file);
        }
    }
}
