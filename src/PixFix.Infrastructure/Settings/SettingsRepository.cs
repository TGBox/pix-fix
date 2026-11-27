using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Settings;

/// <summary>Schlüssel der Einstellungen (aus <c>settings.xml</c> von PixFix 69 übernommen, siehe LegacyImporter).</summary>
public static class SettingKeys
{
    public const string DialMethod = "telephony.dial_method";
    public const string MailProgram = "mail.program";
    public const string BillingMailRecipient = "billing_mail.recipient";
    public const string BillingMailSubject = "billing_mail.subject";
    public const string BillingMailBody = "billing_mail.body";
    public const string AnyDeskShortcut = "remote.anydesk_shortcut";

    /// <summary>Ordner für automatische Sicherungen und Sicherungen vor einer Wiederherstellung; leer = Datenordner\backups.</summary>
    public const string BackupDirectory = "backup.directory";

    /// <summary>TI-Monitor an/aus, automatische Prüfung alle 5 Minuten, Blinken bei neuer Störung (Werte aus PixFix 69: true/false).</summary>
    public const string TiMonitorEnabled = "ti_monitor.enabled";
    public const string TiMonitorAutoCheck = "ti_monitor.auto_check";
    public const string TiMonitorBlink = "ti_monitor.blink";

    /// <summary>„0“ schaltet die automatische Sicherung ab; sonst ist sie an.</summary>
    public const string AutoBackupEnabled = "backup.auto_enabled";

    /// <summary>Letzte Fehlermeldung der automatischen Sicherung (leer = letzte Sicherung erfolgreich).</summary>
    public const string AutoBackupLastError = "backup.auto_last_error";

    /// <summary>Wert von <see cref="DialMethod"/> für <c>tel:</c> (Unify Phone o. Ä.), Standard in PixFix 69.</summary>
    public const string DialMethodStandard = "Windows-Standard (tel:)";

    /// <summary>Wert von <see cref="MailProgram"/> für <c>mailto:</c>.</summary>
    public const string MailProgramStandard = "Windows-Standard";

    /// <summary>Betreff der Rechnungs-Mail, solange keiner eingerichtet ist (wie PixFix 69).</summary>
    public const string BillingMailSubjectDefault = "Kostenpflichtiger Support";

    /// <summary>Wert von <see cref="DialMethod"/> für die Windows-Wählhilfe (<c>dialer.exe</c>); sonst <c>tel:</c>.</summary>
    public const string DialMethodTapi = "Windows-TAPI-Wählhilfe";

    /// <summary>Wert von <see cref="MailProgram"/> für Outlook Desktop; sonst <c>mailto:</c>.</summary>
    public const string MailProgramOutlook = "Outlook Classic";
}

/// <summary>Einstellungen als Schlüssel/Wert in der Tabelle <c>settings</c>.</summary>
public sealed class SettingsRepository(PixFixDatabase database)
{
    public string Get(string key, string defaultValue = "")
    {
        using var db = database.Open();
        return db.Scalar("SELECT value FROM settings WHERE key = $key;", null, ("$key", key)) as string ?? defaultValue;
    }

    /// <summary>Ja/Nein-Einstellung: „true“/„1“ bzw. „false“/„0“; fehlt sie oder ist sie unlesbar, gilt <paramref name="defaultValue"/>.</summary>
    public bool GetFlag(string key, bool defaultValue) =>
        Get(key).Trim().ToUpperInvariant() switch
        {
            "TRUE" or "1" => true,
            "FALSE" or "0" => false,
            _ => defaultValue,
        };

    public void SetFlag(string key, bool value) => Set(key, value ? "true" : "false");

    public void Set(string key, string value)
    {
        using var db = database.Open();
        db.Execute(
            "INSERT INTO settings(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            null,
            ("$key", key),
            ("$value", value ?? string.Empty));
    }
}
