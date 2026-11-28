using CommunityToolkit.Mvvm.ComponentModel;
using PixFix.Infrastructure.Settings;

namespace PixFix.App.Common;

/// <summary>How action buttons show their command: glyph only, glyph and label (default), or label only.</summary>
public enum ButtonLabelMode
{
    IconOnly,
    IconAndText,
    TextOnly,
}

/// <summary>Selectable appearance option shown in settings.</summary>
public sealed record AppearanceOption(string Key, string Label);

/// <summary>
/// Per-user button appearance (label mode and colored roles). Styles in <c>Themes/Controls.xaml</c> bind to
/// <see cref="Current"/>, so changes apply immediately without restart.
/// </summary>
public sealed class Appearance : ObservableObject
{
    private ButtonLabelMode labelMode = ButtonLabelMode.IconAndText;
    private bool isColorful;

    private Appearance()
    {
    }

    public static Appearance Current { get; } = new();

    public static IReadOnlyList<AppearanceOption> LabelModes { get; } =
    [
        new(SettingKeys.ButtonStyleIconText, "Symbol und Text"),
        new(SettingKeys.ButtonStyleIcon, "Nur Symbol"),
        new(SettingKeys.ButtonStyleText, "Nur Text"),
    ];

    public static IReadOnlyList<AppearanceOption> ColorModes { get; } =
    [
        new(SettingKeys.ColorModeSubtle, "Dezent – einheitlich, nur die Hauptaktion farbig"),
        new(SettingKeys.ColorModeColorful, "Farbig – Farbe zeigt die Bedeutung"),
    ];

    public ButtonLabelMode LabelMode
    {
        get => labelMode;
        private set => SetProperty(ref labelMode, value);
    }

    public bool IsColorful
    {
        get => isColorful;
        private set => SetProperty(ref isColorful, value);
    }

    public static string LabelModeKey(ButtonLabelMode mode) => mode switch
    {
        ButtonLabelMode.IconOnly => SettingKeys.ButtonStyleIcon,
        ButtonLabelMode.TextOnly => SettingKeys.ButtonStyleText,
        _ => SettingKeys.ButtonStyleIconText,
    };

    public static ButtonLabelMode ParseLabelMode(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        SettingKeys.ButtonStyleIcon => ButtonLabelMode.IconOnly,
        SettingKeys.ButtonStyleText => ButtonLabelMode.TextOnly,
        _ => ButtonLabelMode.IconAndText,
    };

    public static bool ParseColorful(string? key) =>
        string.Equals((key ?? string.Empty).Trim(), SettingKeys.ColorModeColorful, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads both settings (start-up, after restore or import).</summary>
    public void Load(SettingsRepository settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LabelMode = ParseLabelMode(settings.Get(SettingKeys.ButtonStyle, SettingKeys.ButtonStyleIconText));
        IsColorful = ParseColorful(settings.Get(SettingKeys.ColorMode, SettingKeys.ColorModeSubtle));
    }

    public void SetLabelMode(SettingsRepository settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LabelMode = ParseLabelMode(key);
        settings.Set(SettingKeys.ButtonStyle, LabelModeKey(LabelMode));
    }

    public void SetColorful(SettingsRepository settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IsColorful = ParseColorful(key);
        settings.Set(SettingKeys.ColorMode, IsColorful ? SettingKeys.ColorModeColorful : SettingKeys.ColorModeSubtle);
    }
}
