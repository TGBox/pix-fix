using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using PixFix.App.Common;
using PixFix.Core.Calls;

namespace PixFix.App.Calls;

/// <summary>
/// Fällige Rückruf-Erinnerung (SPEC F5) wie PixFix 69: nur ein Fenster gleichzeitig, kurz im Vordergrund, Taskleiste blinkt.
/// <b>OK</b> beendet die Erinnerung (Status „In Bearbeitung“), <b>Erneut erinnern</b> verschiebt sie ohne Statuswechsel.
/// </summary>
public partial class ReminderWindow : Window
{
    private TimeSpan? snooze;

    private ReminderWindow()
    {
        InitializeComponent();
    }

    /// <summary><c>null</c> = OK, sonst die gewählte Verschiebung.</summary>
    public static TimeSpan? ShowFor(Window? mainWindow, Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var window = new ReminderWindow();
        window.Meta.Text = $"L-Nummer: {Dash(call.CustomerNumber)}     ID: {Dash(call.CustomerId)}\nStatus: {call.Status.ToDisplayName()}";
        var reason = string.IsNullOrWhiteSpace(call.ReasonText) ? "Keine Gesprächsnotiz vorhanden." : call.ReasonText.Trim();
        window.Reason.Text = reason.Length > 500 ? reason[..500] + "…" : reason;

        window.Loaded += (_, _) =>
        {
            window.Activate();
            WindowFlash.Start(window);
            if (mainWindow is not null)
            {
                WindowFlash.Start(mainWindow);
            }

            // Nur kurz im Vordergrund erzwingen, danach normal verdeckbar (wie PixFix 69).
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.Topmost = false;
            };
            timer.Start();
        };
        window.Closed += (_, _) =>
        {
            WindowFlash.Stop(window);
            if (mainWindow is not null)
            {
                WindowFlash.Stop(mainWindow);
            }
        };

        window.ShowDialog();
        return window.snooze;
    }

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "–" : value;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        snooze = null;
        Close();
    }

    private void OnSnooze(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(Amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value <= 0 || value > 999)
        {
            Amount.Focus();
            Amount.SelectAll();
            return;
        }

        snooze = Unit.SelectedIndex switch
        {
            2 => TimeSpan.FromDays(value),
            1 => TimeSpan.FromHours(value),
            _ => TimeSpan.FromMinutes(value),
        };
        Close();
    }
}
