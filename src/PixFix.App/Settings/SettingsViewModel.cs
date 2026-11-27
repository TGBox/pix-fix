namespace PixFix.App.Settings;

/// <summary>Bereich „Einstellungen“: Anruf-Einstellungen und Import aus PixFix 69.</summary>
public sealed class SettingsViewModel(CallSettingsViewModel calls, ImportViewModel import)
{
    public CallSettingsViewModel Calls { get; } = calls;

    public ImportViewModel Import { get; } = import;
}
