using System.Globalization;

namespace PixFix.Core.Legacy;

/// <summary>
/// PixFix 69 speichert Zeitpunkte als lokale Zeit ohne Zeitzone (<c>DateTime.Now.ToString("s")</c>).
/// Neu wird alles in UTC gespeichert.
/// </summary>
public static class LegacyTimestamp
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>Wandelt einen lokalen PixFix-69-Zeitpunkt in UTC um. Leere oder ungültige Werte ergeben <c>null</c>.</summary>
    public static DateTimeOffset? ToUtc(string? value, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        // Werte mit Zeitzonenangabe (z. B. aus schema_info) direkt übernehmen.
        if (text.EndsWith('Z') || HasOffset(text))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withZone)
                ? withZone.ToUniversalTime()
                : null;
        }

        if (!DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // In der Zeitumstellung „fehlende“ Stunde: eine Stunde später ist der tatsächlich gemeinte Zeitpunkt.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static bool HasOffset(string text)
    {
        var timeStart = text.IndexOf('T', StringComparison.Ordinal);
        if (timeStart < 0)
        {
            return false;
        }

        var time = text[timeStart..];
        return time.Contains('+', StringComparison.Ordinal) || time.Contains('-', StringComparison.Ordinal);
    }
}
