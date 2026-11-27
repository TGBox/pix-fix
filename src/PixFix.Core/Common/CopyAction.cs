namespace PixFix.Core.Common;

/// <summary>
/// Kopier-Button an einem Eintrag (SPEC F1/F3). <paramref name="Occurrence"/> ist das wievielte
/// Vorkommen von <paramref name="Text"/> im Inhalt gemeint ist (0 = erstes).
/// </summary>
public sealed record CopyAction(string Text, int Occurrence);
