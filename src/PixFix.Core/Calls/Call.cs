namespace PixFix.Core.Calls;

/// <summary>Anrufkarte (SPEC F4) inklusive Rückruf-Erinnerung (SPEC F5).</summary>
public sealed record Call
{
    public required string Id { get; init; }

    /// <summary>L-Nummer / VNUM1 der Praxis.</summary>
    public string CustomerNumber { get; init; } = string.Empty;

    /// <summary>Cobra-ID des Kontakts.</summary>
    public string CustomerId { get; init; } = string.Empty;

    public string Phone1 { get; init; } = string.Empty;
    public string Phone2 { get; init; } = string.Empty;
    public string Phone3 { get; init; } = string.Empty;
    public string PhonePrivate { get; init; } = string.Empty;
    public string PhoneDirect { get; init; } = string.Empty;
    public string Mobile { get; init; } = string.Empty;
    public string AlternativePhone { get; init; } = string.Empty;
    public string Email1 { get; init; } = string.Empty;
    public string Email2 { get; init; } = string.Empty;
    public string EmailTarget { get; init; } = string.Empty;
    public string Fax { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;

    /// <summary>„Warum wird angerufen?“ als Klartext und formatiert.</summary>
    public string ReasonText { get; init; } = string.Empty;
    public string ReasonRtf { get; init; } = string.Empty;

    /// <summary>„Was wurde durchgeführt / Lösung“ als Klartext und formatiert.</summary>
    public string SolutionText { get; init; } = string.Empty;
    public string SolutionRtf { get; init; } = string.Empty;

    public CallStatus Status { get; init; } = CallStatus.Open;

    /// <summary>Gewähltes Rückrufziel, z. B. „Tel.“, „Mobil“, „Tel. privat“.</summary>
    public string CallbackTarget { get; init; } = string.Empty;

    public bool IsBillable { get; init; }
    public DateTimeOffset? BillingStartUtc { get; init; }
    public DateTimeOffset? BillingEndUtc { get; init; }
    public DateTimeOffset? ReceivedUtc { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public DateTimeOffset? ReminderUtc { get; init; }
    public bool ReminderActive { get; init; }
    public DateTimeOffset? ReminderLastShownUtc { get; init; }
    public bool IsCollapsed { get; init; }
}
