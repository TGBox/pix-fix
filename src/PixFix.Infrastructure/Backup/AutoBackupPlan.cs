using System.Globalization;

namespace PixFix.Infrastructure.Backup;

/// <summary>
/// Regeln der automatischen Sicherung (SPEC F11, angelehnt an PixFix 69): eine Datei je Tag (<c>AUTO-yyyy-MM-dd.pixfix</c>),
/// frühestens alle 4 Stunden neu geschrieben und nur, wenn PixFix eine Weile nicht benutzt wurde; ältere Tagesarchive werden
/// nach <see cref="KeepDays"/> Tagen gelöscht.
/// </summary>
public static class AutoBackupPlan
{
    public const string Prefix = "AUTO-";
    public const int KeepDays = 7;
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(4);

    /// <summary>Erste Sicherung frühestens so lange nach dem Programmstart (Start soll nicht ausgebremst werden).</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

    /// <summary>So lange muss der Benutzer untätig sein, bevor gesichert wird.</summary>
    public static readonly TimeSpan RequiredIdle = TimeSpan.FromSeconds(60);

    public static string FileName(DateTime localDay) =>
        Prefix + localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pixfix";

    /// <summary>Zeitpunkt der letzten automatischen Sicherung im Ordner (oder null).</summary>
    public static DateTime? LastBackup(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var files = Directory.GetFiles(folder, Prefix + "*.pixfix", SearchOption.TopDirectoryOnly);
        return files.Length == 0 ? null : files.Max(File.GetLastWriteTime);
    }

    public static bool IsDue(DateTime? lastBackupLocal, DateTime nowLocal, DateTime startedLocal, TimeSpan idle) =>
        idle >= RequiredIdle
        && nowLocal - startedLocal >= StartupDelay
        && (lastBackupLocal is not { } last || nowLocal - last >= MinimumInterval || last.Date != nowLocal.Date);

    /// <summary>Löscht Tagesarchive älter als <see cref="KeepDays"/> Tage und liegengebliebene <c>.partial</c>-Dateien.</summary>
    public static IReadOnlyList<string> Cleanup(string folder, DateTime todayLocal)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(folder))
        {
            return deleted;
        }

        var oldest = todayLocal.Date.AddDays(-(KeepDays - 1));
        foreach (var file in Directory.GetFiles(folder, Prefix + "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var expired = name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
                || (name.Length == Prefix.Length + 10 + ".pixfix".Length
                    && DateTime.TryParseExact(name.AsSpan(Prefix.Length, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    && day < oldest);
            if (expired)
            {
                BackupWriter.TryDelete(file);
                deleted.Add(name);
            }
        }

        return deleted;
    }
}
