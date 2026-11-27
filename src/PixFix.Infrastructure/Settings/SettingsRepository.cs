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
