using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PixFix.App.Calls;

/// <summary>Zeitpunkt für „Später/Morgen erneut anrufen“ wählen (wie PixFix 69, mit Schnellwahl für „Später“).</summary>
public partial class ReminderScheduleWindow : Window
{
    private DateTime selected;

    private ReminderScheduleWindow()
    {
        InitializeComponent();
    }

    /// <summary>Liefert den gewählten Zeitpunkt (Ortszeit) oder <c>null</c> bei Abbruch.</summary>
    public static DateTime? Pick(Window? owner, DateTime initialLocal, bool tomorrow)
    {
        var window = new ReminderScheduleWindow
        {
            Owner = owner,
            Title = tomorrow ? "PixFix – Erinnerung für morgen" : "PixFix – Rückruf-Erinnerung",
        };
        window.Info.Text = tomorrow
            ? "Wann soll PixFix an den Rückruf erinnern? Datum und Uhrzeit können angepasst werden."
            : "Wann soll PixFix an diesen Rückruf erinnern?";
        window.QuickGroup.Visibility = tomorrow ? Visibility.Collapsed : Visibility.Visible;
        window.ShowValue(initialLocal);
        return window.ShowDialog() == true ? window.selected : null;
    }

    private void ShowValue(DateTime value)
    {
        DateBox.SelectedDate = value.Date;
        TimeBox.Text = value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void OnQuick(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        var value = DateTime.Now.AddMinutes(minutes);
        ShowValue(new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Local));
    }

    private void OnTimeFocus(object sender, RoutedEventArgs e) => TimeBox.SelectAll();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time) || time >= TimeSpan.FromDays(1))
        {
            Error.Text = "Bitte die Uhrzeit im Format HH:mm eingeben, z. B. 12:30.";
            TimeBox.Focus();
            return;
        }

        var value = (DateBox.SelectedDate ?? DateTime.Today).Date.Add(time);
        if (value <= DateTime.Now)
        {
            Error.Text = "Bitte einen Zeitpunkt in der Zukunft wählen.";
            return;
        }

        selected = value;
        DialogResult = true;
    }
}
