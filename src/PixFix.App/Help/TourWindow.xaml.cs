using System.Globalization;
using System.Windows;

namespace PixFix.App.Help;

/// <summary>
/// Einführungstour für neue Mitarbeiter: kleines Fenster unten rechts über dem Hauptfenster, das Schritt für Schritt die
/// Bereiche erklärt und dabei den jeweiligen Bereich im Hauptfenster anzeigt. Nur eine Tour gleichzeitig.
/// </summary>
public partial class TourWindow : Window
{
    private const double EdgeDistance = 32;

    private static TourWindow? open;

    private readonly IReadOnlyList<TourStep> steps;
    private readonly Action<int> showSection;
    private int index;

    private TourWindow(IReadOnlyList<TourStep> steps, Action<int> showSection)
    {
        InitializeComponent();
        this.steps = steps;
        this.showSection = showSection;
        Progress.Maximum = steps.Count;
        ContentRendered += (_, _) => PlaceNearOwnerCorner();
    }

    /// <summary>Startet die Tour (bzw. holt eine laufende nach vorn). <paramref name="finished"/> läuft beim Schließen.</summary>
    public static void Start(Window owner, Action<int> showSection, Action? finished = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(showSection);
        if (open is not null)
        {
            open.Activate();
            return;
        }

        var window = new TourWindow(HelpContent.Tour, showSection);
        if (owner.IsVisible)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Closed += (_, _) =>
        {
            open = null;
            finished?.Invoke();
        };
        open = window;
        window.ShowStep(0);
        window.Show();
    }

    private void ShowStep(int newIndex)
    {
        index = Math.Clamp(newIndex, 0, steps.Count - 1);
        var step = steps[index];
        Counter.Text = string.Format(CultureInfo.CurrentCulture, "Schritt {0} von {1}", index + 1, steps.Count);
        Heading.Text = step.Title;
        Body.Text = step.Text;
        Progress.Value = index + 1;
        BackButton.IsEnabled = index > 0;
        var last = index == steps.Count - 1;
        NextButton.Content = last ? "Fertig" : "Weiter";
        CloseButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        if (step.SectionIndex is { } section)
        {
            showSection(section);
        }
    }

    /// <summary>Unten rechts über dem Hauptfenster, damit der erklärte Bereich sichtbar bleibt.</summary>
    private void PlaceNearOwnerCorner()
    {
        if (Owner is not { IsVisible: true, WindowState: not WindowState.Minimized } owner)
        {
            return;
        }

        var ownerLeft = owner.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Left : owner.Left;
        var ownerTop = owner.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Top : owner.Top;
        var ownerWidth = owner.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Width : owner.ActualWidth;
        var ownerHeight = owner.WindowState == WindowState.Maximized ? SystemParameters.WorkArea.Height : owner.ActualHeight;
        Left = Math.Max(ownerLeft, ownerLeft + ownerWidth - ActualWidth - EdgeDistance);
        Top = Math.Max(ownerTop, ownerTop + ownerHeight - ActualHeight - EdgeDistance);
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (index >= steps.Count - 1)
        {
            Close();
            return;
        }

        ShowStep(index + 1);
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(index - 1);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
