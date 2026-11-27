using System.Windows;
using Microsoft.Win32;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Common;

/// <summary>Selectable UI theme option shown in settings.</summary>
public sealed record ThemeOption(string Key, string Label);

/// <summary>Loads, persists, and applies the WPF Fluent application theme and contrast palette.</summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightPaletteUri = "pack://application:,,,/Themes/LightPalette.xaml";
    private const string DarkPaletteUri = "pack://application:,,,/Themes/DarkPalette.xaml";

    private readonly SettingsRepository settings;

    public ThemeService(SettingsRepository settings)
    {
        this.settings = settings;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(SettingKeys.ThemeLight, "Hell"),
        new(SettingKeys.ThemeDark, "Dunkel"),
        new(SettingKeys.ThemeSystem, "System (Windows)"),
    ];

    public string CurrentKey => Normalize(settings.Get(SettingKeys.Theme, SettingKeys.ThemeLight));

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            SettingKeys.ThemeDark => SettingKeys.ThemeDark,
            SettingKeys.ThemeSystem => SettingKeys.ThemeSystem,
            _ => SettingKeys.ThemeLight,
        };

    public void ApplySaved() => Apply(CurrentKey);

    public void SetTheme(string key)
    {
        var normalized = Normalize(key);
        settings.Set(SettingKeys.Theme, normalized);
        Apply(normalized);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || CurrentKey != SettingKeys.ThemeSystem)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() => Apply(SettingKeys.ThemeSystem));
    }

    private static void Apply(string key)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var mode = key switch
        {
            SettingKeys.ThemeDark => ThemeMode.Dark,
            SettingKeys.ThemeSystem => ThemeMode.System,
            _ => ThemeMode.Light,
        };

        if (app.ThemeMode != mode)
        {
            app.ThemeMode = mode;
        }

        var useDark = key == SettingKeys.ThemeDark || (key == SettingKeys.ThemeSystem && IsSystemDarkTheme());
        IsDark = useDark;
        ApplyPalette(app, useDark);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyPalette(Application app, bool useDark)
    {
        var targetFile = useDark ? "DarkPalette.xaml" : "LightPalette.xaml";
        var targetUri = new Uri(useDark ? DarkPaletteUri : LightPaletteUri, UriKind.Absolute);
        var dictionaries = app.Resources.MergedDictionaries;

        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is not null && source.EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase))
            {
                if (!source.EndsWith(targetFile, StringComparison.OrdinalIgnoreCase))
                {
                    dictionaries[i] = new ResourceDictionary { Source = targetUri };
                }

                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = targetUri });
    }

    private static bool IsSystemDarkTheme() =>
        Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int value && value == 0;
}
