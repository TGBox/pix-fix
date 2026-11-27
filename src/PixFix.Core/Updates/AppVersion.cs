using System.Globalization;

namespace PixFix.Core.Updates;

/// <summary>
/// Versionsnummern von PixFix (SemVer <c>Major.Minor.Patch</c>) aus Release-Tags (<c>v1.4.2</c>) und der
/// InformationalVersion der EXE (<c>1.4.2+abc123</c>) lesen und vergleichen.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Liest <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2.3.0</c> und <c>1.2.3+commit</c>. Vorabversionen (<c>1.2.3-beta</c>) und
    /// alles andere gelten als nicht lesbar.
    /// </summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        if (value.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (i < 3)
            {
                numbers[i] = number;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary><c>true</c>, wenn <paramref name="latest"/> neuer ist als <paramref name="current"/> (nur Major.Minor.Patch).</summary>
    public static bool IsNewer(Version latest, Version current)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(current);
        return Normalize(latest) > Normalize(current);
    }

    /// <summary>Anzeigeform <c>1.2.3</c>.</summary>
    public static string Format(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return Normalize(version).ToString(3);
    }

    private static Version Normalize(Version version) =>
        new(Math.Max(version.Major, 0), Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
}
