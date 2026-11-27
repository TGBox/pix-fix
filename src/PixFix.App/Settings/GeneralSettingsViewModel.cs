using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.App.Common;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Settings;

/// <summary>Reiter „Allgemein“: Verhalten beim Schließen und systemweites Tastenkürzel.</summary>
public sealed class GeneralSettingsViewModel(SettingsRepository settings, ThemeService themeService) : ObservableObject
{
    public IReadOnlyList<ThemeOption> Themes => ThemeService.Options;

    public ThemeOption SelectedTheme
    {
        get
        {
            var currentKey = themeService.CurrentKey;
            return Themes.FirstOrDefault(option => option.Key == currentKey) ?? Themes[0];
        }
        set
        {
            if (value is not null && value.Key != themeService.CurrentKey)
            {
                themeService.SetTheme(value.Key);
                OnPropertyChanged();
            }
        }
    }

    public bool CloseToTray
    {
        get => settings.GetFlag(SettingKeys.CloseToTray, true);
        set
        {
            settings.SetFlag(SettingKeys.CloseToTray, value);
            OnPropertyChanged();
        }
    }

    public bool GlobalHotkey
    {
        get => settings.GetFlag(SettingKeys.GlobalHotkey, true);
        set
        {
            settings.SetFlag(SettingKeys.GlobalHotkey, value);
            OnPropertyChanged();
        }
    }

    /// <summary>Übersicht der Tastenkürzel (Taste, Wirkung).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Shortcuts { get; } =
    [
        new("Strg+Alt+P", "Überall in Windows: PixFix nach vorn, Bildsuche mit dem Screenshot aus der Zwischenablage"),
        new("Strg+1 … Strg+5", "Bereich wechseln (Wissensdatenbank, Schreibbereiche, Anrufe, TI-Monitor, Einstellungen)"),
        new("Strg+F", "Suchfeld des Bereichs (bei Anrufen: Anrufliste)"),
        new("Strg+N", "Neuer Eintrag, Reiter bzw. Anruf"),
        new("Entf", "Ausgewählten Eintrag, Reiter bzw. Anruf in den Papierkorb (in der Liste)"),
        new("Alt + unterstrichener Buchstabe", "Schaltfläche auslösen, z. B. Alt+A = Anrufen, Alt+L = Auflegen"),
        new("Strg+Umschalt+B", "Bildsuche (Wissensdatenbank)"),
        new("Alt+↑ / Alt+↓", "Eintrag im Abschnitt verschieben (Wissensdatenbank)"),
        new("Strg+S", "Editor sofort speichern"),
        new("Strg+Z / Strg+Y", "Rückgängig / Wiederholen im Editor"),
        new("F3 / Umschalt+F3", "Nächste / vorige Fundstelle im Editor"),
        new("Strg+V", "Screenshot einfügen (Editor, Bildsuche)"),
        new("Esc", "Suche leeren"),
    ];

    /// <summary>Applies persisted theme and refreshes UI properties after backup restore or import.</summary>
    public void Reload()
    {
        themeService.ApplySaved();
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(GlobalHotkey));
    }
}
