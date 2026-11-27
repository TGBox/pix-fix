using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Common;

/// <summary>
/// Merkt Fenstergröße/-position und Splitterbreiten in den Einstellungen (wie PixFix 69, SPEC F4: „Wunschposition merken“).
/// In XAML: <c>common:LayoutMemory.Key="calls.list"</c> an einem <see cref="GridSplitter"/> (merkt die Spalte links davon)
/// bzw. an einem <see cref="Window"/>.
/// </summary>
public static class LayoutMemory
{
    private const string Prefix = "ui.layout.";
    private static SettingsRepository? settings;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(LayoutMemory), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element?.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element?.SetValue(KeyProperty, value);

    /// <summary>Beim Programmstart einmal aufrufen; ohne Einstellungen (z. B. im Designer) wird nichts gemerkt.</summary>
    public static void Initialize(SettingsRepository repository) => settings = repository;

    private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (settings is null || e.NewValue is not string key || key.Length == 0)
        {
            return;
        }

        switch (element)
        {
            case GridSplitter splitter:
                AttachSplitter(splitter, key);
                break;
            case Window window:
                AttachWindow(window, key);
                break;
        }
    }

    // ------------------------------------------------------------------ Splitter (Breite der Spalte links davon)

    private static void AttachSplitter(GridSplitter splitter, string key)
    {
        splitter.Loaded += (_, _) =>
        {
            if (Column(splitter) is { } column
                && double.TryParse(Read(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && width >= column.MinWidth
                && width > 0)
            {
                column.Width = new GridLength(width);
            }
        };
        splitter.DragCompleted += (_, _) =>
        {
            if (Column(splitter) is { Width.IsAbsolute: true } column)
            {
                Write(key, column.Width.Value.ToString("0", CultureInfo.InvariantCulture));
            }
        };
    }

    private static ColumnDefinition? Column(GridSplitter splitter)
    {
        var index = Grid.GetColumn(splitter) - 1;
        return splitter.Parent is Grid grid && index >= 0 && index < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[index] : null;
    }

    // ------------------------------------------------------------------ Fenster

    private static void AttachWindow(Window window, string key)
    {
        Restore(window, Read(key));
        window.Closing += (_, _) => Write(key, Describe(window));
    }

    /// <summary>„links;oben;breite;höhe;maximiert“ in geräteunabhängigen Pixeln.</summary>
    private static string Describe(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        return string.Join(
            ';',
            new[] { bounds.Left, bounds.Top, bounds.Width, bounds.Height }.Select(v => v.ToString("0", CultureInfo.InvariantCulture))
                .Append(window.WindowState == WindowState.Maximized ? "1" : "0"));
    }

    private static void Restore(Window window, string stored)
    {
        var parts = stored.Split(';');
        if (parts.Length != 5
            || !TryParse(parts[0], out var left) || !TryParse(parts[1], out var top)
            || !TryParse(parts[2], out var width) || !TryParse(parts[3], out var height)
            || width < 200 || height < 150)
        {
            return;
        }

        var bounds = new Rect(left, top, Math.Max(width, window.MinWidth), Math.Max(height, window.MinHeight));
        if (!IsVisibleOnScreens(bounds))
        {
            // Monitor nicht mehr vorhanden: nur die Größe übernehmen, Position wie im Fenster festgelegt (meist zentriert).
            window.Width = Math.Min(bounds.Width, SystemParameters.WorkArea.Width);
            window.Height = Math.Min(bounds.Height, SystemParameters.WorkArea.Height);
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        if (parts[4] == "1")
        {
            // Erst nach dem Anzeigen maximieren, damit es auf dem richtigen Monitor geschieht.
            window.SourceInitialized += (_, _) => window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Die Titelleiste muss zu einem guten Teil auf dem (virtuellen) Bildschirm liegen.</summary>
    private static bool IsVisibleOnScreens(Rect bounds)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(bounds.Left, bounds.Top, bounds.Width, 30);
        var visible = Rect.Intersect(screen, titleBar);
        return !visible.IsEmpty && visible.Width >= Math.Min(150, bounds.Width / 2);
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ------------------------------------------------------------------ Speichern

    private static string Read(string key) => settings?.Get(Prefix + key) ?? string.Empty;

    private static void Write(string key, string value) => settings?.Set(Prefix + key, value);
}
