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

    /// <summary>Schließen-Kreuz lässt PixFix im Infobereich weiterlaufen (Standard: ja), damit Erinnerungen nicht verloren gehen.</summary>
    public const string CloseToTray = "ui.close_to_tray";

    /// <summary>Systemweites Tastenkürzel Strg+Alt+P für die Bildsuche (Standard: ja).</summary>
    public const string GlobalHotkey = "ui.global_hotkey";

    /// <summary>UI color theme: "light" (default), "dark", or "system".</summary>
    public const string Theme = "ui.theme";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";
    public const string ThemeSystem = "system";

    /// <summary>Schaltflächen: "icon_text" (Symbol und Text, Standard), "icon" (nur Symbol) oder "text" (nur Text).</summary>
    public const string ButtonStyle = "ui.button_style";
    public const string ButtonStyleIcon = "icon";
    public const string ButtonStyleIconText = "icon_text";
    public const string ButtonStyleText = "text";

    /// <summary>Farbgebung der Schaltflächen: "subtle" (dezent, Standard) oder "colorful" (farbig nach Bedeutung).</summary>
    public const string ColorMode = "ui.color_mode";
    public const string ColorModeSubtle = "subtle";
    public const string ColorModeColorful = "colorful";

    /// <summary>Whether the left navigation sidebar is collapsed to compact icon mode (default: false).</summary>
    public const string SidebarCollapsed = "ui.sidebar_collapsed";

    /// <summary>TI-Monitor an/aus, automatische Prüfung alle 5 Minuten, Blinken bei neuer Störung (Werte aus PixFix 69: true/false).</summary>
    public const string TiMonitorEnabled = "ti_monitor.enabled";
    public const string TiMonitorAutoCheck = "ti_monitor.auto_check";
    public const string TiMonitorBlink = "ti_monitor.blink";

    /// <summary>Beim Start und alle 6 Stunden auf GitHub nach einer neuen PixFix-Version suchen (Standard: ja).</summary>
    public const string UpdateAutoCheck = "update.auto_check";

    /// <summary>Die Einführungstour wurde beendet oder übersprungen (Standard: nein – sie startet beim ersten Programmstart).</summary>
    public const string TourCompleted = "help.tour_completed";

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

    /// <summary>Text der Rechnungs-Mail, solange keiner eingerichtet ist; Platzhalter wie in den E-Mail-Vorlagen.</summary>
    public const string BillingMailBodyDefault =
        "Hallo,\r\n\r\n"
        + "bitte für den folgenden kostenpflichtigen Support eine Rechnung erstellen.\r\n\r\n"
        + "L-Nummer: {LNUM}\r\n"
        + "ID: {ID}\r\n\r\n"
        + "Anliegen: {WARUM}\r\n"
        + "Durchgeführt: {LOESUNG}\r\n\r\n"
        + "Vielen Dank!";

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
