using System.Text;

namespace PixFix.Core.Common;

/// <summary>
/// Beschriftungen von Schaltflächen mit WPF-Zugriffstasten ("_Anrufen" = Alt+A, "__" = wörtlicher Unterstrich):
/// lesbarer Text, Zugriffstaste und der Tooltip für die reine Symbolansicht.
/// </summary>
public static class ActionLabel
{
    /// <summary>Beschriftung ohne Zugriffstasten-Markierung ("_Anrufen" → "Anrufen", "A__B" → "A_B").</summary>
    public static string PlainText(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return string.Empty;
        }

        var text = new StringBuilder(label.Length);
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] == '_')
            {
                if (i + 1 < label.Length && label[i + 1] == '_')
                {
                    text.Append('_');
                    i++;
                }

                continue;
            }

            text.Append(label[i]);
        }

        return text.ToString().Trim();
    }

    /// <summary>Zugriffstaste in Großbuchstaben ("Auf_legen" → 'L') oder <c>null</c>.</summary>
    public static char? AccessKey(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        for (var i = 0; i < label.Length - 1; i++)
        {
            if (label[i] != '_')
            {
                continue;
            }

            if (label[i + 1] == '_')
            {
                i++;
                continue;
            }

            return char.IsWhiteSpace(label[i + 1]) ? null : char.ToUpperInvariant(label[i + 1]);
        }

        return null;
    }

    /// <summary>
    /// Tooltip: erste Zeile Beschriftung mit Tastenkürzel ("Anrufen (Alt+A)"), darunter die optionale Beschreibung.
    /// Leer, wenn es weder Beschriftung noch Beschreibung gibt.
    /// </summary>
    public static string ToolTip(string? label, string? description)
    {
        var plain = PlainText(label);
        var key = AccessKey(label);
        var title = key is { } letter && plain.Length > 0 ? $"{plain} (Alt+{letter})" : plain;
        var detail = description?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return detail;
        }

        return detail.Length == 0 || string.Equals(detail, plain, StringComparison.Ordinal)
            ? title
            : title + Environment.NewLine + detail;
    }
}
