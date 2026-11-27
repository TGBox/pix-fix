using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.Core.Calls;

namespace PixFix.App.Calls;

/// <summary>
/// Eine Anrufkarte. Jede Änderung eines Feldes wird sofort gespeichert (über <see cref="CallsViewModel"/>);
/// der Status läuft über <see cref="CallsViewModel.ChangeStatus"/>, weil dort Rückfragen und Erinnerungen hängen.
/// </summary>
public sealed class CallItemViewModel(Call model, CallsViewModel owner) : ObservableObject
{
    public Call Model { get; private set; } = model;

    public string Id => Model.Id;

    public string CustomerNumber { get => Model.CustomerNumber; set => Update(Model with { CustomerNumber = value.Trim() }); }
    public string CustomerId { get => Model.CustomerId; set => Update(Model with { CustomerId = value.Trim() }); }
    public string Phone1 { get => Model.Phone1; set => Update(Model with { Phone1 = value.Trim() }); }
    public string Phone2 { get => Model.Phone2; set => Update(Model with { Phone2 = value.Trim() }); }
    public string Phone3 { get => Model.Phone3; set => Update(Model with { Phone3 = value.Trim() }); }
    public string PhonePrivate { get => Model.PhonePrivate; set => Update(Model with { PhonePrivate = value.Trim() }); }
    public string PhoneDirect { get => Model.PhoneDirect; set => Update(Model with { PhoneDirect = value.Trim() }); }
    public string Mobile { get => Model.Mobile; set => Update(Model with { Mobile = value.Trim() }); }
    public string AlternativePhone { get => Model.AlternativePhone; set => Update(Model with { AlternativePhone = value.Trim() }); }
    public string Email1 { get => Model.Email1; set => Update(Model with { Email1 = value.Trim() }); }
    public string Email2 { get => Model.Email2; set => Update(Model with { Email2 = value.Trim() }); }
    public string EmailTarget { get => Model.EmailTarget; set => Update(Model with { EmailTarget = value }); }
    public string Fax { get => Model.Fax; set => Update(Model with { Fax = value.Trim() }); }
    public string Notes { get => Model.Notes; set => Update(Model with { Notes = value }); }
    public string CallbackTarget { get => Model.CallbackTarget; set => Update(Model with { CallbackTarget = value }); }
    public bool IsBillable { get => Model.IsBillable; set => Update(Model with { IsBillable = value }); }

    /// <summary>Statusauswahl; Änderungen gehen durch die Prüfungen von PixFix 69 und können abgelehnt werden.</summary>
    public CallStatus Status
    {
        get => Model.Status;
        set => owner.ChangeStatus(this, value);
    }

    /// <summary>Abrechnungsbeginn als Uhrzeit „HH:mm“ (am Tag des Anrufs).</summary>
    public string BillingStartText
    {
        get => Clock(Model.BillingStartUtc);
        set => Update(Model with { BillingStartUtc = ParseClock(value) });
    }

    public string BillingEndText
    {
        get => Clock(Model.BillingEndUtc);
        set => Update(Model with { BillingEndUtc = ParseClock(value) });
    }

    public string Duration => CallRules.BillingDuration(Model) is { } d ? $"{(int)d.TotalHours:00}:{d.Minutes:00} h" : string.Empty;

    public bool HasReminderStatus => Model.Status.RequiresReminder();

    public string ReminderText => Model.ReminderActive && Model.ReminderUtc is { } at
        ? "Erinnerung " + at.ToLocalTime().ToString("ddd dd.MM. HH:mm", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>Kopfzeile in der Liste.</summary>
    public string Title => (Model.CustomerNumber.Length > 0 ? "L " + Model.CustomerNumber : "Neuer Anruf")
        + (Model.ReceivedUtc is { } received ? " · " + received.ToLocalTime().ToString("dd.MM. HH:mm", CultureInfo.CurrentCulture) : string.Empty);

    public string Summary => CallRules.CobraText(Model.ReasonText, null) is { Length: > 0 } reason
        ? (reason.Length > 90 ? reason[..90] + "…" : reason)
        : "–";

    public string StatusText => Model.Status.ToDisplayName();

    /// <summary>Ersetzt das Modell (z. B. nach Statuswechsel) und aktualisiert alle Anzeigen.</summary>
    internal void Replace(Call updated)
    {
        Model = updated;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Gesprächsnotiz/Lösung aus den Textfeldern (RTF und Klartext).</summary>
    internal void UpdateTexts(string reasonText, string reasonRtf, string solutionText, string solutionRtf) =>
        Update(Model with { ReasonText = reasonText, ReasonRtf = reasonRtf, SolutionText = solutionText, SolutionRtf = solutionRtf });

    private void Update(Call updated)
    {
        if (updated == Model)
        {
            return;
        }

        Replace(updated with { UpdatedUtc = DateTimeOffset.UtcNow });
        owner.Persist(this);
    }

    private static string Clock(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>„9:5“, „0905“ oder „09:05“; der Tag ist der Tag des Anrufs. Leer oder ungültig → keine Zeit.</summary>
    private DateTimeOffset? ParseClock(string? text)
    {
        if (!TryParseClock(text, out var hours, out var minutes))
        {
            return null;
        }

        var day = (Model.ReceivedUtc ?? Model.CreatedUtc).ToLocalTime().Date;
        var local = DateTime.SpecifyKind(day.AddHours(hours).AddMinutes(minutes), DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    private static bool TryParseClock(string? text, out int hours, out int minutes)
    {
        hours = minutes = 0;
        var value = (text ?? string.Empty).Trim();
        var parts = value.Split(':', '.');
        var ok = parts.Length == 2
            ? int.TryParse(parts[0], CultureInfo.InvariantCulture, out hours) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out minutes)
            : value.Length is 3 or 4 && value.All(char.IsAsciiDigit)
                && int.TryParse(value[..^2], CultureInfo.InvariantCulture, out hours)
                && int.TryParse(value[^2..], CultureInfo.InvariantCulture, out minutes);
        return ok && hours is >= 0 and <= 23 && minutes is >= 0 and <= 59;
    }

    /// <summary>Nach abgelehntem Statuswechsel die Auswahl wieder auf den gespeicherten Status setzen.</summary>
    internal void RefreshStatusLater() =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            new Action(() => OnPropertyChanged(nameof(Status))), System.Windows.Threading.DispatcherPriority.Background);
}
