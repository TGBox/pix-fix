namespace PixFix.Core.Common;

/// <summary>
/// Eine Datei im Datenordner (Bild, PDF, RTF-Dokument, Fax-Vorlage). Die Id ist der SHA-256 des Inhalts,
/// dadurch werden identische Dateien nur einmal gespeichert.
/// </summary>
public sealed record Asset(string Id, string RelativePath, string MediaType, long Length, string? OriginalName);
