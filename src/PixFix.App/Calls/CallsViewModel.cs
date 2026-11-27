using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PixFix.App.Common;
using PixFix.Core.Calls;
using PixFix.Core.Templates;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Settings;
using PixFix.Infrastructure.Telephony;
using PixFix.Infrastructure.Templates;

namespace PixFix.App.Calls;

/// <summary>
/// Anrufbearbeitung (SPEC F4–F7, F10): Anrufkarten, Status mit Rückfragen, Rückruf-Erinnerungen, Anrufen/Auflegen,
/// E-Mail/Fax/Rechnungs-Mail, Vorlagen und Cobra-Schnelltexte. Die Erinnerungen laufen unabhängig davon, welcher
/// Bereich gerade angezeigt wird (<see cref="Start"/>).
/// </summary>
public sealed class CallsViewModel : ObservableObject
{
    private static readonly TimeSpan ReminderCheckInterval = TimeSpan.FromSeconds(5);

    private readonly CallRepository repository;
    private readonly CobraQuickTextRepository quickTexts;
    private readonly TemplateRepository templates;
    private readonly FaxDocumentRepository faxDocuments;
    private readonly SettingsRepository settings;
    private readonly ILogger<CallsViewModel> logger;
    private readonly DispatcherTimer reminderTimer;
    private CallItemViewModel? selectedCall;
    private bool showDone;
    private bool reminderOpen;
    private string status = string.Empty;

    public CallsViewModel(
        CallRepository repository,
        CobraQuickTextRepository quickTexts,
        TemplateRepository templates,
        FaxDocumentRepository faxDocuments,
        SettingsRepository settings,
        ILogger<CallsViewModel> logger)
    {
        this.repository = repository;
        this.quickTexts = quickTexts;
        this.templates = templates;
        this.faxDocuments = faxDocuments;
        this.settings = settings;
        this.logger = logger;

        CallList = CollectionViewSource.GetDefaultView(Calls);
        CallList.Filter = o => ShowDone || o is not CallItemViewModel { Model.Status: CallStatus.Done };

        reminderTimer = new DispatcherTimer { Interval = ReminderCheckInterval };
        reminderTimer.Tick += (_, _) => CheckReminders();

        NewCallCommand = new RelayCommand(NewCall);
        DeleteCommand = new RelayCommand(DeleteSelected, HasSelection);
        DialCommand = new RelayCommand(Dial, HasSelection);
        HangUpCommand = new AsyncRelayCommand(HangUpAsync);
        EmailCommand = new RelayCommand(ComposeEmail, HasSelection);
        BillingMailCommand = new RelayCommand(() => ComposeBillingMail(SelectedCall), HasSelection);
        FaxCommand = new RelayCommand(OpenFax, HasSelection);
        CopyCobraCommand = new RelayCommand(CopyCobraText, HasSelection);
        EditReminderCommand = new RelayCommand(EditReminder, () => SelectedCall?.HasReminderStatus == true);
        BillingStartNowCommand = new RelayCommand(() => SetBillingNow(start: true), HasSelection);
        BillingEndNowCommand = new RelayCommand(() => SetBillingNow(start: false), HasSelection);
        ShowTrashCommand = new RelayCommand(() => TrashRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? TrashRequested;

    /// <summary>Die Ansicht soll Text an „Warum wird angerufen?“ (true) bzw. „Lösung“ (false) anhängen.</summary>
    public event EventHandler<(bool Reason, string Text)>? AppendTextRequested;

    public ObservableCollection<CallItemViewModel> Calls { get; } = [];

    public ICollectionView CallList { get; }

    public IReadOnlyList<CallStatus> Statuses { get; } = Enum.GetValues<CallStatus>();

    public IReadOnlyList<string> CallbackTargets { get; } = ["", "Tel.", "Tel.2", "Tel.3", "Tel. privat", "Tel. direkt", "Mobil"];

    public IReadOnlyList<string> EmailTargets { get; } = ["", "E-Mail", "E-Mail2"];

    public RelayCommand NewCallCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand DialCommand { get; }
    public AsyncRelayCommand HangUpCommand { get; }
    public RelayCommand EmailCommand { get; }
    public RelayCommand BillingMailCommand { get; }
    public RelayCommand FaxCommand { get; }
    public RelayCommand CopyCobraCommand { get; }
    public RelayCommand EditReminderCommand { get; }
    public RelayCommand BillingStartNowCommand { get; }
    public RelayCommand BillingEndNowCommand { get; }
    public RelayCommand ShowTrashCommand { get; }

    public CallItemViewModel? SelectedCall
    {
        get => selectedCall;
        set
        {
            if (SetProperty(ref selectedCall, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool ShowDone
    {
        get => showDone;
        set
        {
            if (SetProperty(ref showDone, value))
            {
                CallList.Refresh();
            }
        }
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    /// <summary>Lädt die Anrufe und startet die Erinnerungsprüfung (beim Programmstart).</summary>
    public void Start()
    {
        Reload();
        reminderTimer.Start();
    }

    public void Reload()
    {
        var keep = SelectedCall?.Id;
        Calls.Clear();
        foreach (var call in repository.LoadAll())
        {
            Calls.Add(new CallItemViewModel(call, this));
        }

        SelectedCall = Calls.FirstOrDefault(c => c.Id == keep);
    }

    // ------------------------------------------------------------------ Speichern und Status

    internal void Persist(CallItemViewModel item)
    {
        try
        {
            repository.Save(item.Model);
            if (item == SelectedCall)
            {
                EditReminderCommand.NotifyCanExecuteChanged();
            }
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogError(ex, "Anruf {CallId} konnte nicht gespeichert werden", item.Id);
            Status = "Speichern fehlgeschlagen: " + ex.Message;
        }
    }

    /// <summary>
    /// Statuswechsel mit den Abläufen aus PixFix 69: Kostenpflichtig → Zeiten prüfen und nach der Rechnungs-Mail fragen
    /// („Nein“ öffnet die Mail und lässt den Status unverändert); „Später/Morgen erneut anrufen“ → Erinnerungszeitpunkt.
    /// </summary>
    internal void ChangeStatus(CallItemViewModel item, CallStatus target)
    {
        var call = item.Model;
        if (call.Status == target)
        {
            return;
        }

        var check = CallRules.CheckStatusChange(call, target);
        if (check.IsBlocked)
        {
            MessageBox.Show(
                Owner,
                $"Bei diesem kostenpflichtigen Anruf fehlt noch die {check.MissingBillingTime}.\n\nBitte trage die fehlende Zeit ein, bevor du den Anruf auf Erledigt setzt.",
                "PixFix – Kostenpflichtiger Anruf",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            item.RefreshStatusLater();
            return;
        }

        if (check.AskInvoiceMail
            && MessageBox.Show(Owner, "Wurde die E-Mail für die Rechnung schon versendet?", "PixFix – Kostenpflichtiger Support", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            item.RefreshStatusLater();
            ComposeBillingMail(item);
            return;
        }

        DateTimeOffset? reminder = null;
        if (check.NeedsReminder)
        {
            var tomorrow = target == CallStatus.CallBackTomorrow;
            var initial = CallRules.DefaultReminderLocal(call, tomorrow, DateTime.Now, TimeZoneInfo.Local);
            if (ReminderScheduleWindow.Pick(Owner, initial, tomorrow) is not { } chosen)
            {
                item.RefreshStatusLater();
                return;
            }

            reminder = new DateTimeOffset(DateTime.SpecifyKind(chosen, DateTimeKind.Local)).ToUniversalTime();
        }

        item.Replace(CallRules.ApplyStatus(call, target, reminder, DateTimeOffset.UtcNow));
        Persist(item);
        CallList.Refresh();
        RefreshCommands();
    }

    private void EditReminder()
    {
        if (SelectedCall is not { HasReminderStatus: true } item)
        {
            return;
        }

        var tomorrow = item.Model.Status == CallStatus.CallBackTomorrow;
        var initial = CallRules.DefaultReminderLocal(item.Model, tomorrow, DateTime.Now, TimeZoneInfo.Local);
        if (ReminderScheduleWindow.Pick(Owner, initial, tomorrow) is { } chosen)
        {
            var at = new DateTimeOffset(DateTime.SpecifyKind(chosen, DateTimeKind.Local)).ToUniversalTime();
            item.Replace(item.Model with { ReminderActive = true, ReminderUtc = at, ReminderLastShownUtc = null, UpdatedUtc = DateTimeOffset.UtcNow });
            Persist(item);
            Status = "Rückruf-Erinnerung wurde angepasst.";
        }
    }

    /// <summary>Prüft alle 5 Sekunden auf fällige Erinnerungen; es ist immer nur ein Erinnerungsfenster offen.</summary>
    private void CheckReminders()
    {
        if (reminderOpen)
        {
            return;
        }

        var due = CallRules.NextDueReminder(Calls.Select(c => c.Model), DateTimeOffset.UtcNow);
        var item = due is null ? null : Calls.FirstOrDefault(c => c.Id == due.Id);
        if (item is null)
        {
            return;
        }

        reminderOpen = true;
        try
        {
            var snooze = ReminderWindow.ShowFor(Owner, item.Model);
            if (snooze is { } delay)
            {
                item.Replace(CallRules.SnoozeReminder(item.Model, delay, DateTimeOffset.UtcNow));
            }
            else
            {
                item.Replace(CallRules.AcknowledgeReminder(item.Model, DateTimeOffset.UtcNow));
                ShowCallsRequested?.Invoke(this, EventArgs.Empty);
                SelectedCall = item;
                if (Owner is { } main)
                {
                    WindowFlash.BringToFront(main);
                }
            }

            Persist(item);
            CallList.Refresh();
        }
        finally
        {
            reminderOpen = false;
        }
    }

    /// <summary>Nach „OK“ in einer Erinnerung soll das Hauptfenster die Anrufe zeigen.</summary>
    public event EventHandler? ShowCallsRequested;

    // ------------------------------------------------------------------ Anrufe anlegen/löschen

    private void NewCall()
    {
        var call = repository.Create();
        var item = new CallItemViewModel(call, this);
        Calls.Insert(0, item);
        SelectedCall = item;
        Status = "Neuer Anruf angelegt.";
    }

    private void DeleteSelected()
    {
        if (SelectedCall is not { } item)
        {
            return;
        }

        if (MessageBox.Show(Owner, $"Anruf „{item.Title}“ in den Papierkorb verschieben?", "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        repository.MoveToTrash(item.Id);
        Calls.Remove(item);
        SelectedCall = null;
        Status = "Anruf liegt im Papierkorb.";
    }

    public IReadOnlyList<CallTrashItem> LoadTrash() => repository.LoadTrash();

    public void RestoreFromTrash(string trashId)
    {
        var call = repository.Restore(trashId);
        var item = new CallItemViewModel(call, this);
        Calls.Insert(0, item);
        if (call.Status == CallStatus.Done)
        {
            ShowDone = true;
        }

        SelectedCall = item;
    }

    public void DeleteFromTrash(string trashId) => repository.DeletePermanently(trashId);

    // ------------------------------------------------------------------ Telefon

    private void Dial()
    {
        if (SelectedCall is not { } item)
        {
            return;
        }

        var options = CallRules.PhoneOptions(item.Model);
        if (options.Count == 0)
        {
            MessageBox.Show(Owner, "Für diesen Anruf ist keine Telefonnummer hinterlegt.", "PixFix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preferred = CallRules.PreferredPhoneLabel(item.Model);
        var choices = options.Select(o => new Choice<PhoneOption>($"{o.Label}:  {o.Number}", o)).ToList();
        var index = Math.Max(0, options.ToList().FindIndex(o => o.Label == preferred));
        var option = options.Count == 1
            ? options[0]
            : ChoiceWindow.Pick(Owner, "PixFix – Anrufen", "Welche Nummer soll angerufen werden?", choices, index);
        if (option is null)
        {
            return;
        }

        try
        {
            var useTapiDialer = settings.Get(SettingKeys.DialMethod) == SettingKeys.DialMethodTapi;
            var dialed = PhoneDialer.Dial(option.Number, useTapiDialer);
            Status = dialed is null ? "Die Nummer enthält keine Ziffern." : $"Telefonwahl an Windows übergeben ({dialed}).";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Owner, "Der Anruf konnte nicht an Windows übergeben werden.\n\n" + ex.Message, "PixFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task HangUpAsync()
    {
        HangupResult result;
        try
        {
            result = await TapiHangup.HangUpSingleLocalCallAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.ExternalException)
        {
            logger.LogWarning(ex, "Auflegen über TAPI fehlgeschlagen");
            MessageBox.Show(Owner, "Auflegen konnte nicht ausgeführt werden.\n\n" + ex.Message + "\n\nUnify Phone wurde nicht verändert.", "PixFix – Auflegen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (message, icon) = result switch
        {
            HangupResult.Done => ((string?)null, MessageBoxImage.None),
            HangupResult.TapiUnavailable => ("Die lokale Windows-Telefonie konnte nicht geöffnet werden.\n\nDer Anrufen-Button ist davon nicht betroffen.", MessageBoxImage.Information),
            HangupResult.NoActiveCall => ("Es wurde kein aktives lokales TAPI-Gespräch gefunden.", MessageBoxImage.Information),
            HangupResult.SeveralActiveCalls => ("PixFix sieht mehr als ein aktives TAPI-Gespräch.\n\nAus Sicherheitsgründen wurde nichts beendet.", MessageBoxImage.Warning),
            HangupResult.NoPermission => ("Das aktive Gespräch wurde gefunden, aber der Telefonietreiber gibt PixFix keine Berechtigung zum Auflegen.\n\nUnify Phone bleibt unverändert bedienbar.", MessageBoxImage.Information),
            HangupResult.DropNotSupported => ("Das aktive Gespräch wurde gefunden, aber der OpenScape-/TAPI-Treiber bietet für diesen Gesprächszustand kein Auflegen über TAPI an.", MessageBoxImage.Information),
            _ => ("Das Gespräch konnte über TAPI nicht beendet werden.\n\nUnify Phone wurde nicht verändert.", MessageBoxImage.Warning),
        };

        if (message is null)
        {
            Status = "Telefonat beendet.";
            return;
        }

        Status = message.Split('\n')[0];
        MessageBox.Show(Owner, message, "PixFix – Auflegen", MessageBoxButton.OK, icon);
    }

    // ------------------------------------------------------------------ E-Mail, Fax, Cobra

    private bool UseOutlook => settings.Get(SettingKeys.MailProgram, SettingKeys.MailProgramOutlook) == SettingKeys.MailProgramOutlook;

    private void ComposeEmail()
    {
        if (SelectedCall is not { } item)
        {
            return;
        }

        var address = CallRules.EmailAddress(item.Model);
        if (address is null)
        {
            MessageBox.Show(Owner, "Für diesen Anruf ist keine E-Mail-Adresse hinterlegt.", "PixFix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var emailTemplates = templates.LoadByKind(TemplateKind.Email);
        if (emailTemplates.Count == 0)
        {
            OpenMail(address, string.Empty, string.Empty);
            return;
        }

        var template = emailTemplates.Count == 1
            ? emailTemplates[0]
            : ChoiceWindow.Pick(Owner, "PixFix – E-Mail", "Welche Vorlage soll verwendet werden?", emailTemplates.Select(t => new Choice<Template>(t.Name, t)).ToList());
        if (template is null)
        {
            return;
        }

        OpenMail(address, CallRules.ApplyTemplate(item.Model, template.Subject), CallRules.ApplyTemplate(item.Model, template.Body));
    }

    /// <summary>Rechnungs-Mail für kostenpflichtigen Support (Empfänger, Betreff und Text aus den Einstellungen).</summary>
    private void ComposeBillingMail(CallItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var recipient = settings.Get(SettingKeys.BillingMailRecipient).Trim();
        if (recipient.Length == 0)
        {
            MessageBox.Show(Owner, "Für die Kostenpflichtig-Support-Mail ist noch kein Empfänger eingerichtet (Einstellungen).", "PixFix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenMail(
            recipient,
            CallRules.ApplyTemplate(item.Model, settings.Get(SettingKeys.BillingMailSubject, SettingKeys.BillingMailSubjectDefault)),
            CallRules.ApplyTemplate(item.Model, settings.Get(SettingKeys.BillingMailBody)));
    }

    private void OpenMail(string to, string subject, string body)
    {
        try
        {
            MailComposer.OpenDraft(to, subject, body, UseOutlook);
            Status = "E-Mail-Entwurf wurde geöffnet.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "E-Mail-Entwurf konnte nicht geöffnet werden");
            MessageBox.Show(Owner, "Die E-Mail konnte nicht geöffnet werden.\n\n" + ex.Message, "PixFix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFax()
    {
        var documents = faxDocuments.LoadAll();
        if (documents.Count == 0)
        {
            MessageBox.Show(Owner, "Es sind keine Fax-Dokumente hinterlegt.", "PixFix – Fax", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var document = ChoiceWindow.Pick(Owner, "PixFix – Fax", "Welches Fax-Dokument soll geöffnet werden?", documents.Select(d => new Choice<FaxDocument>(d.Name, d)).ToList());
        if (document is null)
        {
            return;
        }

        try
        {
            ShellOpener.OpenCopy(document.File, document.Name);
            Status = "Fax-Dokument geöffnet.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Owner, "Das Dokument konnte nicht geöffnet werden.\n\n" + ex.Message, "PixFix – Fax", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyCobraText()
    {
        if (SelectedCall is not { } item)
        {
            return;
        }

        var text = CallRules.CobraText(item.Model.ReasonText, item.Model.SolutionText);
        if (text.Length == 0)
        {
            MessageBox.Show(Owner, "In den beiden Anruffeldern steht noch kein Text zum Kopieren.", "PixFix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(text);
        Status = "Anruftext für Cobra wurde in die Zwischenablage kopiert.";
    }

    public IReadOnlyList<CobraQuickText> LoadQuickTexts() => quickTexts.LoadAll();

    /// <summary>
    /// Cobra-Schnelltext (z. B. „Praxis nicht erreicht“): Text für Cobra in die Zwischenablage (die automatische Übertragung
    /// folgt mit der Cobra-Anbindung), Notizen ergänzen (z. B. „NE,“), Lösungstext anhängen, Status setzen.
    /// </summary>
    public void ApplyQuickText(CobraQuickText quick)
    {
        ArgumentNullException.ThrowIfNull(quick);
        if (SelectedCall is not { } item)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(quick.CobraText))
        {
            Clipboard.SetText(quick.CobraText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(quick.NotesText))
        {
            item.Notes = CallRules.AppendNotes(item.Notes, quick.NotesText);
        }

        if (!string.IsNullOrWhiteSpace(quick.WorkText))
        {
            AppendTextRequested?.Invoke(this, (false, quick.WorkText.Trim()));
        }

        Status = $"Schnelltext „{quick.Name}“ angewendet – Cobra-Text liegt in der Zwischenablage.";
        if (quick.CompleteCall)
        {
            ChangeStatus(item, CallStatus.Done);
        }
        else if (quick.ScheduleRecall)
        {
            ChangeStatus(item, CallStatus.CallBackLater);
        }
    }

    public IReadOnlyList<Template> LoadTemplates(string kind) => templates.LoadByKind(kind);

    /// <summary>Anrufgrund- bzw. Lösungsvorlage in das passende Feld einfügen (Platzhalter ersetzt).</summary>
    public void ApplyTextTemplate(Template template, bool reason)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (SelectedCall is { } item)
        {
            AppendTextRequested?.Invoke(this, (reason, CallRules.ApplyTemplate(item.Model, template.Body)));
        }
    }

    private void SetBillingNow(bool start)
    {
        if (SelectedCall is not { } item)
        {
            return;
        }

        var now = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (start)
        {
            item.BillingStartText = now;
            item.IsBillable = true;
        }
        else
        {
            item.BillingEndText = now;
        }
    }

    private bool HasSelection() => SelectedCall is not null;

    private void RefreshCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        DialCommand.NotifyCanExecuteChanged();
        EmailCommand.NotifyCanExecuteChanged();
        BillingMailCommand.NotifyCanExecuteChanged();
        FaxCommand.NotifyCanExecuteChanged();
        CopyCobraCommand.NotifyCanExecuteChanged();
        EditReminderCommand.NotifyCanExecuteChanged();
        BillingStartNowCommand.NotifyCanExecuteChanged();
        BillingEndNowCommand.NotifyCanExecuteChanged();
    }

    private static Window? Owner => Application.Current?.MainWindow;
}
