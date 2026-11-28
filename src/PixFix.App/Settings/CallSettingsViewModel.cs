using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Settings;
using PixFix.Infrastructure.Templates;

namespace PixFix.App.Settings;

/// <summary>Anruf-Einstellungen wie in PixFix 69: Telefonie, E-Mail-Programm, Rechnungs-Mail; Zugang zu den Vorlagen.</summary>
public sealed class CallSettingsViewModel : ObservableObject
{
    private readonly SettingsRepository settings;
    private readonly TemplateRepository templates;
    private readonly CobraQuickTextRepository quickTexts;
    private readonly FaxDocumentRepository faxDocuments;

    public CallSettingsViewModel(
        SettingsRepository settings,
        TemplateRepository templates,
        CobraQuickTextRepository quickTexts,
        FaxDocumentRepository faxDocuments)
    {
        this.settings = settings;
        this.templates = templates;
        this.quickTexts = quickTexts;
        this.faxDocuments = faxDocuments;
        OpenTemplatesCommand = new RelayCommand(OpenTemplates);
    }

    /// <summary>Nach dem Schließen des Vorlagenfensters (z. B. Eintragsvorlagen neu laden).</summary>
    public event EventHandler? TemplatesChanged;

    public IReadOnlyList<string> DialMethods { get; } = [SettingKeys.DialMethodStandard, SettingKeys.DialMethodTapi];

    public IReadOnlyList<string> MailPrograms { get; } = [SettingKeys.MailProgramStandard, SettingKeys.MailProgramOutlook];

    public RelayCommand OpenTemplatesCommand { get; }

    public string DialMethod
    {
        get => OneOf(settings.Get(SettingKeys.DialMethod), DialMethods, SettingKeys.DialMethodStandard);
        set => Set(SettingKeys.DialMethod, value);
    }

    /// <summary>Ohne Einstellung gilt Outlook (alle nutzen Outlook Desktop), passend zu den Anrufen.</summary>
    public string MailProgram
    {
        get => OneOf(settings.Get(SettingKeys.MailProgram, SettingKeys.MailProgramOutlook), MailPrograms, SettingKeys.MailProgramOutlook);
        set => Set(SettingKeys.MailProgram, value);
    }

    public string BillingMailRecipient
    {
        get => settings.Get(SettingKeys.BillingMailRecipient);
        set => Set(SettingKeys.BillingMailRecipient, value?.Trim());
    }

    public string BillingMailSubject
    {
        get => settings.Get(SettingKeys.BillingMailSubject, SettingKeys.BillingMailSubjectDefault);
        set => Set(SettingKeys.BillingMailSubject, value);
    }

    public string BillingMailBody
    {
        get => settings.Get(SettingKeys.BillingMailBody, SettingKeys.BillingMailBodyDefault);
        set => Set(SettingKeys.BillingMailBody, value);
    }

    /// <summary>Nach einem Import die übernommenen Werte anzeigen.</summary>
    public void Reload() => OnPropertyChanged(string.Empty);

    private void OpenTemplates()
    {
        TemplatesWindow.ShowFor(System.Windows.Application.Current?.MainWindow, new TemplatesViewModel(templates, quickTexts, faxDocuments));
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Set(string key, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        settings.Set(key, value ?? string.Empty);
        OnPropertyChanged(property);
    }

    private static string OneOf(string value, IReadOnlyList<string> options, string fallback) =>
        options.Contains(value) ? value : fallback;
}
