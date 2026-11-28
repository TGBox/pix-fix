using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PixFix.Core.Templates;

namespace PixFix.App.Common;

/// <summary>
/// Anklickbare Platzhalter ({LNUM}, {ID} …) für Vorlagen-Editoren (SPEC F16). Ein Klick fügt den Platzhalter an der
/// Cursorposition des zuletzt bearbeiteten Textfeldes im selben Fenster ein – Textfelder melden sich dafür mit
/// <c>common:PlaceholderBar.AcceptsPlaceholders="True"</c> an. Die Pills nehmen keinen Fokus, damit Cursor und Auswahl
/// im Textfeld erhalten bleiben.
/// </summary>
public sealed class PlaceholderBar : WrapPanel
{
    public static readonly DependencyProperty AcceptsPlaceholdersProperty = DependencyProperty.RegisterAttached(
        "AcceptsPlaceholders", typeof(bool), typeof(PlaceholderBar), new PropertyMetadata(false, OnAcceptsPlaceholdersChanged));

    // Zuletzt fokussiertes Textfeld je Fenster.
    private static readonly DependencyProperty LastTargetProperty = DependencyProperty.RegisterAttached(
        "LastTarget", typeof(TextBox), typeof(PlaceholderBar), new PropertyMetadata(null));

    public PlaceholderBar()
    {
        Orientation = Orientation.Horizontal;
        foreach (var placeholder in TemplatePlaceholders.All)
        {
            var pill = new Button
            {
                Content = placeholder.Token,
                ToolTip = $"{placeholder.Description} an der Cursorposition einfügen",
            };
            pill.SetResourceReference(StyleProperty, "PlaceholderPill");
            pill.Click += (_, _) => Insert(placeholder.Token);
            Children.Add(pill);
        }
    }

    public static bool GetAcceptsPlaceholders(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(AcceptsPlaceholdersProperty);
    }

    public static void SetAcceptsPlaceholders(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AcceptsPlaceholdersProperty, value);
    }

    private static void OnAcceptsPlaceholdersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.GotKeyboardFocus -= OnTargetFocused;
        if (e.NewValue is true)
        {
            box.GotKeyboardFocus += OnTargetFocused;
        }
    }

    private static void OnTargetFocused(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && Window.GetWindow(box) is { } window)
        {
            window.SetValue(LastTargetProperty, box);
        }
    }

    private void Insert(string token)
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        var target = window.GetValue(LastTargetProperty) as TextBox;
        if (target is null || !IsUsable(target))
        {
            // Noch nichts angeklickt: das letzte angemeldete Feld ist der Haupttext (Betreff steht davor).
            target = FindTargets(window).LastOrDefault(IsUsable);
        }

        if (target is null)
        {
            return;
        }

        target.Focus();
        var caret = target.SelectionStart;
        target.SelectedText = token;
        target.SelectionLength = 0;
        target.CaretIndex = caret + token.Length;
    }

    private static bool IsUsable(TextBox box) => box.IsVisible && box.IsEnabled && !box.IsReadOnly;

    private static IEnumerable<TextBox> FindTargets(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box && GetAcceptsPlaceholders(box))
            {
                yield return box;
            }

            foreach (var nested in FindTargets(child))
            {
                yield return nested;
            }
        }
    }
}
