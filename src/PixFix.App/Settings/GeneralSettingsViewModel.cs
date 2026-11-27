using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.App.Common;
using PixFix.App.Help;
using PixFix.App.Updates;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Settings;

/// <summary>Reiter „Allgemein“: Darstellung, Verhalten beim Schließen, systemweites Tastenkürzel und Updates.</summary>
public sealed class GeneralSettingsViewModel(SettingsRepository settings, ThemeService themeService, UpdateViewModel updates)
    : ObservableObject
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

    /// <summary>Übersicht der Tastenkürzel (Taste, Wirkung) – dieselbe Liste wie in der Hilfe.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Shortcuts => HelpContent.Shortcuts;

    /// <summary>Installierte Version, automatische Update-Suche und „Jetzt nach Updates suchen“.</summary>
    public UpdateViewModel Updates => updates;

    /// <summary>Applies persisted theme and refreshes UI properties after backup restore or import.</summary>
    public void Reload()
    {
        themeService.ApplySaved();
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(GlobalHotkey));
        updates.Reload();
    }
}
