namespace PixFix.Core.Calls;

/// <summary>Bearbeitungsstatus eines Anrufs (siehe SPEC F4).</summary>
public enum CallStatus
{
    Open,
    InProgress,
    CallBackLater,
    CallBackTomorrow,
    Done,
}

public static class CallStatusText
{
    private static readonly Dictionary<CallStatus, string> DisplayNames = new()
    {
        [CallStatus.Open] = "Noch zu bearbeiten",
        [CallStatus.InProgress] = "In Bearbeitung",
        [CallStatus.CallBackLater] = "Später erneut anrufen",
        [CallStatus.CallBackTomorrow] = "Morgen erneut anrufen",
        [CallStatus.Done] = "Erledigt",
    };

    /// <summary>Anzeigetext, identisch mit den in PixFix 69 gespeicherten Statuswerten.</summary>
    public static string ToDisplayName(this CallStatus status) => DisplayNames[status];

    /// <summary>
    /// Liest einen Statuswert aus PixFix 69. Unbekannte oder leere Werte werden zu
    /// <see cref="CallStatus.Open"/>, damit beim Import kein Anruf verloren geht.
    /// </summary>
    public static CallStatus ParseLegacy(string? value)
    {
        var text = value?.Trim();
        foreach (var (status, name) in DisplayNames)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return CallStatus.Open;
    }

    /// <summary>Bei diesen Status wird der Erinnerungsdialog geöffnet (SPEC F5).</summary>
    public static bool RequiresReminder(this CallStatus status) =>
        status is CallStatus.CallBackLater or CallStatus.CallBackTomorrow;
}
