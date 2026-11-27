namespace PixFix.Infrastructure;

/// <summary>
/// Ablageorte der Nutzerdaten. Getrennt vom Programmordner, standardmäßig unter
/// %LOCALAPPDATA%\PixFix (SPEC Abschnitt 4). Für Tests und Sonderfälle über die
/// Umgebungsvariable PIXFIX_DATA_DIR überschreibbar.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirectoryVariable = "PIXFIX_DATA_DIR";

    public AppPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string DatabaseFile => Path.Combine(DataDirectory, "pixfix.db");

    public string AssetsDirectory => Path.Combine(DataDirectory, "assets");

    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Abgeleitete, jederzeit neu berechenbare Daten (z. B. aufbereitete Dokumente). Wird nicht gesichert.</summary>
    public string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public static AppPaths CreateDefault()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppPaths(overridden);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, "PixFix"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
