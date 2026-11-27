using System.Text.RegularExpressions;
using PixFix.Core.Templates;

namespace PixFix.Core.Calls;

/// <summary>Telefonnummer eines Anrufs mit Beschriftung wie in PixFix 69 („Tel.“, „Mobil“ …).</summary>
public sealed record PhoneOption(string Label, string Number);

/// <summary>Cobra-Schnelltext (z. B. „Praxis nicht erreicht“) mit seinen Nebenwirkungen (SPEC F10).</summary>
public sealed record CobraQuickText(long Id, string Name, string CobraText, string NotesText, string WorkText, bool CompleteCall, bool ScheduleRecall);

/// <summary>Ergebnis der Prüfung vor einem Statuswechsel.</summary>
/// <param name="MissingBillingTime">Gesetzt, wenn ein kostenpflichtiger Anruf ohne Start-/Endzeit erledigt werden soll – Wechsel ablehnen.</param>
/// <param name="AskInvoiceMail">Vor „Erledigt“ bei kostenpflichtigen Anrufen fragen, ob die Rechnungs-Mail schon raus ist.</param>
/// <param name="NeedsReminder">Für „Später/Morgen erneut anrufen“ muss ein Erinnerungszeitpunkt gewählt werden.</param>
public sealed record StatusChangeCheck(string? MissingBillingTime, bool AskInvoiceMail, bool NeedsReminder)
{
    public bool IsBlocked => MissingBillingTime is not null;
}

/// <summary>
/// Fachregeln der Anrufbearbeitung, 1:1 aus PixFix 69 (<c>PixFix.Calls.cs</c>, <c>Reminders.cs</c>, <c>Integrations.cs</c>,
/// <c>Communication.cs</c>, <c>Cobra.cs</c>). Siehe SPEC F4–F7.
/// </summary>
public static partial class CallRules
{
    public const string AlternativeNumberLabel = "Alternative Nummer";

    /// <summary>Uhrzeit, zu der „Morgen erneut anrufen“ standardmäßig erinnert.</summary>
    public static readonly TimeSpan TomorrowReminderTime = TimeSpan.FromHours(9);

    /// <summary>Abstand, mit dem „Später erneut anrufen“ standardmäßig erinnert.</summary>
    public static readonly TimeSpan LaterReminderDelay = TimeSpan.FromMinutes(30);

    [GeneratedRegex(@"[^0-9+]")]
    private static partial Regex NonDialCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // ------------------------------------------------------------------ Telefon und E-Mail

    /// <summary>Alle hinterlegten Nummern in der Reihenfolge von PixFix 69.</summary>
    public static IReadOnlyList<PhoneOption> PhoneOptions(Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return new[]
            {
                new PhoneOption("Tel.", call.Phone1),
                new PhoneOption("Tel.2", call.Phone2),
                new PhoneOption("Tel.3", call.Phone3),
                new PhoneOption("Tel. privat", call.PhonePrivate),
                new PhoneOption("Tel. direkt", call.PhoneDirect),
                new PhoneOption("Mobil", call.Mobile),
                new PhoneOption(AlternativeNumberLabel, call.AlternativePhone),
            }
            .Where(o => !string.IsNullOrWhiteSpace(o.Number))
            .ToList();
    }

    /// <summary>Vorausgewählte Nummer: alternative Nummer, sonst das gewählte Rückrufziel.</summary>
    public static string? PreferredPhoneLabel(Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!string.IsNullOrWhiteSpace(call.AlternativePhone))
        {
            return AlternativeNumberLabel;
        }

        return string.IsNullOrWhiteSpace(call.CallbackTarget) ? null : call.CallbackTarget.Trim();
    }

    /// <summary>
    /// Wählstring wie in PixFix 69: nur Ziffern und „+“, davor eine 0 für die Amtsleitung.
    /// <c>null</c>, wenn keine Ziffern übrig bleiben.
    /// </summary>
    public static string? DialString(string? number)
    {
        var clean = NonDialCharacters().Replace(number ?? string.Empty, string.Empty);
        return clean.Length == 0 ? null : "0" + clean;
    }

    /// <summary>Empfänger für E-Mails an die Praxis gemäß „E-Mail-Ziel“, sonst die erste vorhandene Adresse.</summary>
    public static string? EmailAddress(Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        string? address;
        if (call.EmailTarget == "E-Mail2" && !string.IsNullOrWhiteSpace(call.Email2))
        {
            address = call.Email2;
        }
        else if (call.EmailTarget == "E-Mail" && !string.IsNullOrWhiteSpace(call.Email1))
        {
            address = call.Email1;
        }
        else
        {
            address = !string.IsNullOrWhiteSpace(call.Email1) ? call.Email1 : call.Email2;
        }

        return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    /// <summary>Platzhalter {LNUM}, {ID}, {WARUM}, {LOESUNG}, {EMAIL}, {FAX} einsetzen.</summary>
    public static string ApplyTemplate(Call call, string? text)
    {
        ArgumentNullException.ThrowIfNull(call);
        return TemplateRenderer.Render(text ?? string.Empty, new Dictionary<string, string?>
        {
            [TemplatePlaceholders.CustomerNumber] = call.CustomerNumber,
            [TemplatePlaceholders.Id] = call.CustomerId,
            [TemplatePlaceholders.Reason] = call.ReasonText,
            [TemplatePlaceholders.Solution] = call.SolutionText,
            [TemplatePlaceholders.Email] = call.Email1,
            [TemplatePlaceholders.Fax] = call.Fax,
        });
    }

    /// <summary>Text für Cobra: „Grund --> Lösung“, Zeilenumbrüche und Mehrfach-Leerzeichen zusammengefasst.</summary>
    public static string CobraText(string? reason, string? work)
    {
        string Clean(string? v) => Whitespace().Replace((v ?? string.Empty).Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
        var why = Clean(reason);
        var done = Clean(work);
        if (why.Length > 0 && done.Length > 0)
        {
            return why + " --> " + done;
        }

        return why.Length > 0 ? why : done;
    }

    /// <summary>Notizen um einen Schnelltext ergänzen (z. B. „NE,“), mit Leerzeichen getrennt.</summary>
    public static string AppendNotes(string? notes, string? addition)
    {
        var value = addition?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return notes ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(notes) ? value : notes.TrimEnd() + " " + value;
    }

    /// <summary>Abrechnungsdauer, wenn Start und Ende gesetzt sind und das Ende nicht vor dem Start liegt.</summary>
    public static TimeSpan? BillingDuration(Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.BillingStartUtc is { } start && call.BillingEndUtc is { } end && end >= start ? end - start : null;
    }

    // ------------------------------------------------------------------ Status und Erinnerungen

    public static StatusChangeCheck CheckStatusChange(Call call, CallStatus target)
    {
        ArgumentNullException.ThrowIfNull(call);
        string? missing = null;
        var askInvoice = false;
        if (target == CallStatus.Done && call.Status != CallStatus.Done && call.IsBillable)
        {
            var missingStart = call.BillingStartUtc is null;
            var missingEnd = call.BillingEndUtc is null;
            missing = (missingStart, missingEnd) switch
            {
                (true, true) => "Start- und Endzeit",
                (true, false) => "Startzeit",
                (false, true) => "Endzeit",
                _ => null,
            };
            askInvoice = missing is null;
        }

        return new StatusChangeCheck(missing, askInvoice, target.RequiresReminder());
    }

    /// <summary>
    /// Neuer Status mit Erinnerung und Erledigt-Zeitpunkt. Ohne Erinnerungsstatus wird eine Erinnerung beendet.
    /// </summary>
    public static Call ApplyStatus(Call call, CallStatus target, DateTimeOffset? reminderUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(call);
        var needsReminder = target.RequiresReminder();
        if (needsReminder && reminderUtc is null)
        {
            throw new ArgumentException("Für diesen Status wird ein Erinnerungszeitpunkt benötigt.", nameof(reminderUtc));
        }

        DateTimeOffset? completed = target switch
        {
            CallStatus.Done when call.Status != CallStatus.Done => nowUtc,
            CallStatus.Done => call.CompletedUtc,
            _ => null,
        };

        return call with
        {
            Status = target,
            ReminderActive = needsReminder,
            ReminderUtc = needsReminder ? reminderUtc : null,
            ReminderLastShownUtc = needsReminder ? null : call.ReminderLastShownUtc,
            CompletedUtc = completed,
            UpdatedUtc = nowUtc,
        };
    }

    /// <summary>
    /// Vorschlag für den Erinnerungszeitpunkt (Ortszeit): vorhandene Erinnerung desselben Status, sonst in 30 Minuten
    /// bzw. morgen um 9 Uhr.
    /// </summary>
    public static DateTime DefaultReminderLocal(Call call, bool tomorrow, DateTime nowLocal, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(zone);
        var sameStatus = tomorrow ? call.Status == CallStatus.CallBackTomorrow : call.Status == CallStatus.CallBackLater;
        if (call.ReminderActive && sameStatus && call.ReminderUtc is { } existing)
        {
            return TimeZoneInfo.ConvertTime(existing, zone).DateTime;
        }

        return tomorrow ? nowLocal.Date.AddDays(1).Add(TomorrowReminderTime) : nowLocal.Add(LaterReminderDelay);
    }

    /// <summary>Die am längsten fällige aktive Erinnerung oder <c>null</c>.</summary>
    public static Call? NextDueReminder(IEnumerable<Call> calls, DateTimeOffset nowUtc) =>
        calls.Where(c => c.ReminderActive && c.ReminderUtc is { } at && at <= nowUtc)
            .OrderBy(c => c.ReminderUtc)
            .FirstOrDefault();

    /// <summary>„OK“ in der Erinnerung: Erinnerung beenden, Status „In Bearbeitung“.</summary>
    public static Call AcknowledgeReminder(Call call, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call with
        {
            ReminderActive = false,
            ReminderUtc = null,
            ReminderLastShownUtc = nowUtc,
            Status = CallStatus.InProgress,
            UpdatedUtc = nowUtc,
        };
    }

    /// <summary>„Erneut erinnern“: Zeitpunkt verschieben, Status bleibt.</summary>
    public static Call SnoozeReminder(Call call, TimeSpan delay, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call with
        {
            ReminderActive = true,
            ReminderUtc = nowUtc + delay,
            ReminderLastShownUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }
}
