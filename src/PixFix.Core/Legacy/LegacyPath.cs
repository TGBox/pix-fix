namespace PixFix.Core.Legacy;

/// <summary>Relative Pfade aus PixFix 69 verwenden Windows-Trennzeichen.</summary>
public static class LegacyPath
{
    public static string Normalize(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim();
    }

    /// <summary>
    /// Kombiniert Basisordner und relativen Pfad und stellt sicher, dass das Ergebnis innerhalb des Basisordners liegt.
    /// Gibt <c>null</c> zurück, wenn der Pfad aus dem Basisordner herausführt.
    /// </summary>
    public static string? CombineWithin(string baseDirectory, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        var normalized = Normalize(relativePath);
        if (normalized.Length == 0 || Path.IsPathRooted(normalized))
        {
            return null;
        }

        var root = Path.GetFullPath(baseDirectory);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
