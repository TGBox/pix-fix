using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PixFix.App.Common;
using PixFix.Core.Common;

namespace PixFix.App.Editing;

/// <summary>
/// Losslessly adapts FlowDocument default and dark-colored text for Dark Mode display while preserving original colors
/// for Light Mode, persistence, and HTML email export.
/// </summary>
public static class FlowDocumentThemeHelper
{
    private static readonly object UnsetSentinel = new();

    private static readonly DependencyProperty SavedForegroundProperty = DependencyProperty.RegisterAttached(
        "SavedForeground",
        typeof(object),
        typeof(FlowDocumentThemeHelper));

    private static readonly DependencyProperty SavedBackgroundProperty = DependencyProperty.RegisterAttached(
        "SavedBackground",
        typeof(object),
        typeof(FlowDocumentThemeHelper));

    private static int mutationDepth;

    public static bool IsMutatingTheme => mutationDepth > 0;

    public static void ApplyTheme(FlowDocument document, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(document);

        mutationDepth++;
        try
        {
            RestoreOriginalColorsCore(document);

            if (isDark
                && document.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush docBg
                && docBg.Color.A > 0
                && ThemeColorContrast.IsNearWhiteBackground(ToRgb(docBg.Color)))
            {
                SaveAndClear(document, TextElement.BackgroundProperty, SavedBackgroundProperty, docBg);
            }

            var docHasBackground =
                document.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush { Color.A: > 0 };

            if (!docHasBackground)
            {
                var localDocFg = document.ReadLocalValue(TextElement.ForegroundProperty);
                var shouldBindThemePrimary = localDocFg == DependencyProperty.UnsetValue
                    || (localDocFg is SolidColorBrush { Color: var docFgColor }
                        && docFgColor.A > 0
                        && (isDark
                            ? ThemeColorContrast.IsDefaultDarkText(ToRgb(docFgColor))
                            : ThemeColorContrast.IsNearWhiteBackground(ToRgb(docFgColor))));

                if (shouldBindThemePrimary)
                {
                    document.SetValue(
                        SavedForegroundProperty,
                        localDocFg == DependencyProperty.UnsetValue ? UnsetSentinel : localDocFg);
                    document.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
                }
            }

            AdaptChildren(document, docHasBackground, isDark);
        }
        finally
        {
            mutationDepth--;
        }
    }

    public static void RestoreOriginalColors(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        mutationDepth++;
        try
        {
            RestoreOriginalColorsCore(document);
        }
        finally
        {
            mutationDepth--;
        }
    }

    public static IDisposable SuspendThemeAdaptation(FlowDocument document)
    {
        RestoreOriginalColors(document);
        return new ThemeRestoreScope(document);
    }

    private static void AdaptChildren(DependencyObject parent, bool hasExplicitBackground, bool isDark)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject dep)
            {
                continue;
            }

            if (dep is TextElement element)
            {
                var localBg = element.ReadLocalValue(TextElement.BackgroundProperty);
                var localFg = element.ReadLocalValue(TextElement.ForegroundProperty);
                var elementHasBackground = false;

                if (localBg is SolidColorBrush { Color: var bg } && bg.A > 0)
                {
                    if (isDark && ThemeColorContrast.IsNearWhiteBackground(ToRgb(bg)))
                    {
                        SaveAndClear(element, TextElement.BackgroundProperty, SavedBackgroundProperty, localBg);
                    }
                    else
                    {
                        elementHasBackground = true;
                    }
                }

                var effectiveHasBackground = hasExplicitBackground || elementHasBackground;
                if (!effectiveHasBackground && localFg is SolidColorBrush { Color: var fg } && fg.A > 0)
                {
                    var rgb = ToRgb(fg);
                    if (isDark)
                    {
                        if (ThemeColorContrast.IsDefaultDarkText(rgb))
                        {
                            SaveAndClear(element, TextElement.ForegroundProperty, SavedForegroundProperty, localFg);
                        }
                        else
                        {
                            var adapted = ThemeColorContrast.AdaptForDarkSurface(rgb);
                            if (adapted != rgb)
                            {
                                element.SetValue(SavedForegroundProperty, localFg);
                                var brush = new SolidColorBrush(Color.FromArgb(fg.A, adapted.R, adapted.G, adapted.B));
                                brush.Freeze();
                                element.Foreground = brush;
                            }
                        }
                    }
                    else if (ThemeColorContrast.IsNearWhiteBackground(rgb))
                    {
                        SaveAndClear(element, TextElement.ForegroundProperty, SavedForegroundProperty, localFg);
                    }
                }

                AdaptChildren(element, effectiveHasBackground, isDark);
            }
            else
            {
                AdaptChildren(dep, hasExplicitBackground, isDark);
            }
        }
    }

    private static void RestoreOriginalColorsCore(DependencyObject node)
    {
        RestoreProperty(node, TextElement.ForegroundProperty, SavedForegroundProperty);
        RestoreProperty(node, TextElement.BackgroundProperty, SavedBackgroundProperty);

        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dep)
            {
                RestoreOriginalColorsCore(dep);
            }
        }
    }

    private static void SaveAndClear(
        DependencyObject target,
        DependencyProperty property,
        DependencyProperty savedProperty,
        object originalValue)
    {
        target.SetValue(savedProperty, originalValue);
        target.ClearValue(property);
    }

    private static void RestoreProperty(
        DependencyObject target,
        DependencyProperty property,
        DependencyProperty savedProperty)
    {
        var saved = target.ReadLocalValue(savedProperty);
        if (saved == DependencyProperty.UnsetValue)
        {
            return;
        }

        if (ReferenceEquals(saved, UnsetSentinel))
        {
            target.ClearValue(property);
        }
        else
        {
            target.SetValue(property, saved);
        }

        target.ClearValue(savedProperty);
    }

    private static ThemeColorContrast.RgbColor ToRgb(Color color) => new(color.R, color.G, color.B);

    private sealed class ThemeRestoreScope(FlowDocument document) : IDisposable
    {
        public void Dispose() => ApplyTheme(document, ThemeService.IsDark);
    }
}
