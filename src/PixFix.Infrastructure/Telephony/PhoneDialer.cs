using System.Diagnostics;
using PixFix.Core.Calls;

namespace PixFix.Infrastructure.Telephony;

/// <summary>
/// Anrufen wie in PixFix 69 (SPEC F7, „grüner Anrufen-Weg“ unverändert): Einstellung „Windows-TAPI-Wählhilfe“ →
/// <c>dialer.exe /d "nummer"</c>, sonst <c>tel:nummer</c> (öffnet Unify Phone). Die Nummer bekommt die 0 für die Amtsleitung.
/// </summary>
public static class PhoneDialer
{
    /// <summary>Übergibt die Nummer an Windows. Liefert den gewählten String oder <c>null</c>, wenn die Nummer leer ist.</summary>
    public static string? Dial(string number, bool useTapiDialer)
    {
        var dial = CallRules.DialString(number);
        if (dial is null)
        {
            return null;
        }

        var start = useTapiDialer
            ? new ProcessStartInfo("dialer.exe", "/d \"" + dial + "\"") { UseShellExecute = true }
            : new ProcessStartInfo("tel:" + dial) { UseShellExecute = true };
        Process.Start(start)?.Dispose();
        return dial;
    }
}
