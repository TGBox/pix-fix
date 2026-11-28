using System.Windows;
using System.Windows.Controls;

namespace PixFix.App.Common;

/// <summary>
/// Werkzeugleiste für Spalten (SPEC F16): Die Schaltflächen nutzen in jeder Darstellung die volle Spaltenbreite. Passen
/// alle in eine Zeile, teilen sie sich die Breite – gleich breit, wenn jede in ein gleiches Feld passt, sonst bekommt jede
/// den gleichen Zuschlag auf ihre Wunschbreite. Reicht der Platz nicht, brechen sie um, und jede Zeile wird für sich
/// auf die volle Breite gestreckt.
/// </summary>
public sealed class ActionBar : Panel
{
    /// <summary>
    /// Schaltflächen füllen ihr Feld, der Inhalt steht mittig. Wird bei jedem Messen gesetzt, weil das Fluent-Theme seinen
    /// Stil (mit linker Ausrichtung) erst nach dem Einfügen anwendet und einen früher gesetzten Wert wieder verwirft.
    /// </summary>
    private static void FillSlot(UIElement child)
    {
        if (child is FrameworkElement element && element.HorizontalAlignment != HorizontalAlignment.Stretch)
        {
            element.SetCurrentValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        }

        if (child is Control control && control.HorizontalContentAlignment != HorizontalAlignment.Center)
        {
            control.SetCurrentValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var visible = VisibleChildren().ToList();
        foreach (var child in visible)
        {
            FillSlot(child);
            child.Measure(infinite);
        }

        var rows = SplitIntoRows(visible, availableSize.Width);
        var height = rows.Sum(row => row.Max(child => child.DesiredSize.Height));
        var width = double.IsInfinity(availableSize.Width)
            ? rows.Select(row => row.Sum(child => child.DesiredSize.Width)).DefaultIfEmpty(0).Max()
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var top = 0.0;
        foreach (var row in SplitIntoRows(VisibleChildren().ToList(), finalSize.Width))
        {
            var rowHeight = row.Max(child => child.DesiredSize.Height);
            var widths = RowWidths(row, finalSize.Width);
            var left = 0.0;
            for (var i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(left, top, widths[i], rowHeight));
                left += widths[i];
            }

            top += rowHeight;
        }

        return finalSize;
    }

    /// <summary>
    /// Breiten einer Zeile: gleich breite Felder, wenn jede Schaltfläche hineinpasst, sonst Wunschbreite plus gleicher
    /// Zuschlag. Der rechte Außenabstand der letzten Schaltfläche zählt mit, damit sie bündig am Rand endet.
    /// </summary>
    private static double[] RowWidths(List<UIElement> row, double width)
    {
        if (double.IsInfinity(width) || width <= 0)
        {
            return row.Select(child => child.DesiredSize.Width).ToArray();
        }

        var total = width + RightMargin(row[^1]);
        var slot = total / row.Count;
        if (row.All(child => child.DesiredSize.Width <= slot))
        {
            return row.Select(_ => slot).ToArray();
        }

        var extra = Math.Max(0, total - row.Sum(child => child.DesiredSize.Width)) / row.Count;
        return row.Select(child => child.DesiredSize.Width + extra).ToArray();
    }

    /// <summary>Zeilen wie in einem <see cref="WrapPanel"/>; der rechte Außenabstand am Zeilenende darf überstehen.</summary>
    private static List<List<UIElement>> SplitIntoRows(List<UIElement> children, double width)
    {
        var rows = new List<List<UIElement>>();
        var current = new List<UIElement>();
        var used = 0.0;
        foreach (var child in children)
        {
            var size = child.DesiredSize.Width;
            if (current.Count > 0 && used + size - RightMargin(child) > width)
            {
                rows.Add(current);
                current = [];
                used = 0;
            }

            current.Add(child);
            used += size;
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static double RightMargin(UIElement child) => child is FrameworkElement element ? element.Margin.Right : 0;

    private IEnumerable<UIElement> VisibleChildren() =>
        InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed);
}
