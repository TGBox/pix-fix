using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace PixFix.App.Editing;

/// <summary>Ergebnis des Speicherns: XAML ohne Bilddaten, Klartext und Bilder in Dokumentreihenfolge.</summary>
public sealed record SavedDocument(string Xaml, string PlainText, IReadOnlyList<string> ImageAssetIds);

/// <summary>
/// Speichert und lädt Editor-Inhalte als FlowDocument-XAML (SPEC 6.2). Bilder stehen im XAML nur als Verweis
/// <c>Tag="pixfix-asset:&lt;id&gt;"</c>; die Bilddateien liegen in der Ablage.
/// </summary>
public static class FlowDocumentStore
{
    public const string AssetTagPrefix = "pixfix-asset:";

    public static FlowDocument Load(string xaml, Func<string, string?> resolveAssetFile)
    {
        ArgumentNullException.ThrowIfNull(resolveAssetFile);
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return new FlowDocument(new Paragraph());
        }

        var document = (FlowDocument)XamlReader.Parse(xaml);
        foreach (var image in Images(document))
        {
            if (AssetId(image) is { } id && resolveAssetFile(id) is { } file && File.Exists(file))
            {
                image.Source = LoadBitmap(file);
            }
        }

        return document;
    }

    /// <summary>
    /// Serialisiert das Dokument. Bilder ohne Asset (eingefügt oder aus RTF) werden über <paramref name="storeImage"/>
    /// in der Ablage gespeichert und bekommen danach ihren Verweis.
    /// </summary>
    public static SavedDocument Save(FlowDocument document, Func<BitmapSource, string?> storeImage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(storeImage);

        var images = Images(document).ToList();
        var ids = new List<string>();
        var sources = new List<(Image Image, object? Source)>();
        foreach (var image in images)
        {
            var id = AssetId(image);
            if (id is null && image.Source is BitmapSource bitmap)
            {
                id = storeImage(bitmap);
                if (id is not null)
                {
                    SetAssetId(image, id);
                }
            }

            if (id is not null)
            {
                ids.Add(id);
            }

            // Bilddaten nicht ins XAML schreiben – nur den Verweis.
            sources.Add((image, image.ReadLocalValue(Image.SourceProperty)));
            image.ClearValue(Image.SourceProperty);
        }

        string xaml;
        try
        {
            xaml = XamlWriter.Save(document);
        }
        finally
        {
            foreach (var (image, source) in sources)
            {
                if (source != DependencyProperty.UnsetValue)
                {
                    image.SetValue(Image.SourceProperty, source);
                }
            }
        }

        var plain = new TextRange(document.ContentStart, document.ContentEnd).Text;
        return new SavedDocument(xaml, plain, ids);
    }

    /// <summary>XAML eines Dokuments aus Klartext (z. B. Eintragsvorlage); jede Zeile wird ein Absatz.</summary>
    public static string FromPlainText(string text)
    {
        var document = new FlowDocument();
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            document.Blocks.Add(new Paragraph(new Run(line)));
        }

        return XamlWriter.Save(document);
    }

    /// <summary>Alle Bilder im Dokument in Lesereihenfolge.</summary>
    public static IEnumerable<Image> Images(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is Image image)
            {
                yield return image;
            }
            else if (child is DependencyObject element)
            {
                foreach (var nested in Images(element))
                {
                    yield return nested;
                }
            }
        }
    }

    public static string? AssetId(Image image) =>
        image.Tag is string tag && tag.StartsWith(AssetTagPrefix, StringComparison.Ordinal) ? tag[AssetTagPrefix.Length..] : null;

    public static void SetAssetId(Image image, string assetId) => image.Tag = AssetTagPrefix + assetId;

    public static BitmapImage LoadBitmap(string file)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(file, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
