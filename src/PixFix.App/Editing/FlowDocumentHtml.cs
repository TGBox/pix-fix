using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixFix.App.Editing;

/// <summary>Bild für die Mail: Content-ID und Datei.</summary>
public sealed record HtmlImage(string ContentId, string File);

/// <summary>
/// Wandelt den Editorinhalt (FlowDocument) in einfaches E-Mail-HTML um: Absätze, fett/kursiv/unterstrichen, Farben,
/// Code-Blöcke (Consolas), Listen, Tabellen, Links und Bilder (als <c>cid:</c>-Verweise für Outlook).
/// </summary>
public static class FlowDocumentHtml
{
    private const double MaxImageWidth = 900;

    /// <param name="imageFile">Datei zu einem Bild (z. B. aus der Ablage); <c>null</c> = Bild wird als PNG in den Temp-Ordner geschrieben.</param>
    public static (string Html, IReadOnlyList<HtmlImage> Images) Convert(FlowDocument document, Func<Image, string?> imageFile, string? footerText = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(imageFile);
        var context = new Context(imageFile);
        var html = new StringBuilder("<html><head><meta charset=\"utf-8\"></head><body style=\"font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:11pt;\">");
        foreach (var block in document.Blocks)
        {
            WriteBlock(html, block, context);
        }

        if (!string.IsNullOrWhiteSpace(footerText))
        {
            html.Append("<p style=\"margin:12px 0 0 0;\">").Append(Encode(footerText).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>");
        }

        html.Append("</body></html>");
        return (html.ToString(), context.Images);
    }

    private static void WriteBlock(StringBuilder html, Block block, Context context)
    {
        switch (block)
        {
            case Paragraph paragraph:
                html.Append("<p style=\"margin:0 0 6px 0;").Append(ParagraphStyle(paragraph)).Append("\">");
                if (!paragraph.Inlines.Any())
                {
                    html.Append("&nbsp;");
                }

                foreach (var inline in paragraph.Inlines)
                {
                    WriteInline(html, inline, context);
                }

                html.Append("</p>");
                break;

            case List list:
                var tag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                    or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? "ol" : "ul";
                html.Append('<').Append(tag).Append(" style=\"margin:0 0 6px 0;\">");
                foreach (var item in list.ListItems)
                {
                    html.Append("<li>");
                    foreach (var child in item.Blocks)
                    {
                        WriteBlock(html, child, context);
                    }

                    html.Append("</li>");
                }

                html.Append("</").Append(tag).Append('>');
                break;

            case Table table:
                html.Append("<table style=\"border-collapse:collapse;margin:0 0 6px 0;\">");
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        html.Append("<tr>");
                        foreach (var cell in row.Cells)
                        {
                            html.Append("<td style=\"border:1px solid #ccc;padding:3px 6px;vertical-align:top;\">");
                            foreach (var child in cell.Blocks)
                            {
                                WriteBlock(html, child, context);
                            }

                            html.Append("</td>");
                        }

                        html.Append("</tr>");
                    }
                }

                html.Append("</table>");
                break;

            case Section section:
                foreach (var child in section.Blocks)
                {
                    WriteBlock(html, child, context);
                }

                break;

            case BlockUIContainer { Child: Image image }:
                html.Append("<p style=\"margin:0 0 6px 0;\">");
                WriteImage(html, image, context);
                html.Append("</p>");
                break;
        }
    }

    private static void WriteInline(StringBuilder html, Inline inline, Context context)
    {
        switch (inline)
        {
            case Run run when run.Text.Length > 0:
                var style = TextStyle(run);
                var open = new StringBuilder();
                var close = new StringBuilder();
                if (run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight())
                {
                    open.Append("<b>");
                    close.Insert(0, "</b>");
                }

                if (run.FontStyle == FontStyles.Italic || run.FontStyle == FontStyles.Oblique)
                {
                    open.Append("<i>");
                    close.Insert(0, "</i>");
                }

                if (Decorations(run).Any(d => d.Location == TextDecorationLocation.Underline))
                {
                    open.Append("<u>");
                    close.Insert(0, "</u>");
                }

                if (style.Length > 0)
                {
                    open.Insert(0, "<span style=\"" + style + "\">");
                    close.Append("</span>");
                }

                html.Append(open).Append(Encode(run.Text).Replace("\n", "<br>", StringComparison.Ordinal)).Append(close);
                break;

            case LineBreak:
                html.Append("<br>");
                break;

            case Hyperlink link:
                html.Append("<a href=\"").Append(WebUtility.HtmlEncode(link.NavigateUri?.ToString() ?? string.Empty)).Append("\">");
                foreach (var child in link.Inlines)
                {
                    WriteInline(html, child, context);
                }

                html.Append("</a>");
                break;

            case Span span:
                foreach (var child in span.Inlines)
                {
                    WriteInline(html, child, context);
                }

                break;

            case InlineUIContainer { Child: Image image }:
                WriteImage(html, image, context);
                break;
        }
    }

    private static void WriteImage(StringBuilder html, Image image, Context context)
    {
        if (image.Source is not BitmapSource bitmap)
        {
            return;
        }

        var file = context.ImageFile(image) ?? WriteTemp(bitmap);
        var cid = "pixfix-img-" + context.Images.Count.ToString(CultureInfo.InvariantCulture) + "@pixfix";
        context.Images.Add(new HtmlImage(cid, file));
        var width = !double.IsNaN(image.Width) && image.Width > 0 ? image.Width : bitmap.Width;
        html.Append("<img src=\"cid:").Append(cid).Append("\" width=\"")
            .Append(Math.Round(Math.Min(width, MaxImageWidth)).ToString(CultureInfo.InvariantCulture)).Append("\" alt=\"\">");
    }

    private static string WriteTemp(BitmapSource bitmap)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pixfix-mail");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, FlowDocumentStore.EncodePng(bitmap));
        return file;
    }

    private static string ParagraphStyle(Paragraph paragraph) => paragraph.TextAlignment switch
    {
        TextAlignment.Center => "text-align:center;",
        TextAlignment.Right => "text-align:right;",
        _ => string.Empty,
    };

    private static string TextStyle(Run run)
    {
        var style = new StringBuilder();
        if (run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || run.FontFamily.Source.Contains("Courier", StringComparison.OrdinalIgnoreCase))
        {
            style.Append("font-family:Consolas,'Courier New',monospace;");
        }

        if (run.Foreground is SolidColorBrush { Color: var foreground } && foreground != Colors.Black && foreground.A > 0)
        {
            style.Append("color:").Append(Hex(foreground)).Append(';');
        }

        if (run.Background is SolidColorBrush { Color: var background } && background.A > 0 && background != Colors.White)
        {
            style.Append("background-color:").Append(Hex(background)).Append(';');
        }

        if (Math.Abs(run.FontSize - 12) > 0.5)
        {
            // WPF-Einheiten (1/96 Zoll) in Punkt.
            style.Append("font-size:").Append(Math.Round(run.FontSize * 72 / 96, 1).ToString(CultureInfo.InvariantCulture)).Append("pt;");
        }

        return style.ToString();
    }

    private static List<TextDecoration> Decorations(Run run)
    {
        var all = new List<TextDecoration>();
        if (run.TextDecorations is { } own)
        {
            all.AddRange(own);
        }

        if (run.Parent is Inline { TextDecorations: { } span })
        {
            all.AddRange(span);
        }

        if (run.Parent is Paragraph { TextDecorations: { } paragraph })
        {
            all.AddRange(paragraph);
        }

        return all;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private sealed class Context(Func<Image, string?> imageFile)
    {
        public Func<Image, string?> ImageFile { get; } = imageFile;

        public List<HtmlImage> Images { get; } = [];
    }
}
