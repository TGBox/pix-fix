using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PixFix.Core.Calls;
using PixFix.Core.Templates;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Templates;

namespace PixFix.App.Settings;

/// <summary>Art einer Vorlage, wie sie im Vorlagenfenster angeboten wird.</summary>
public sealed record TemplateKindOption(string Kind, string Title, bool HasSubject, string Hint, bool HasPlaceholders = false);

/// <summary>
/// Pflege der Vorlagen (SPEC F6): alle Vorlagenarten, Cobra-Schnelltexte und Fax-Dokumente. Änderungen werden sofort
/// gespeichert (Texte 0,5 s nach der letzten Eingabe).
/// </summary>
public sealed class TemplatesViewModel : ObservableObject
{
    private const string PlaceholderHint = "Platzhalter anklicken, um sie an der Cursorposition einzufügen.";

    private readonly TemplateRepository templates;
    private readonly CobraQuickTextRepository quickTexts;
    private readonly FaxDocumentRepository faxDocuments;
    private TemplateKindOption selectedKind;
    private TemplateItem? selectedTemplate;
    private QuickTextItem? selectedQuickText;
    private FaxItem? selectedFax;

    public TemplatesViewModel(TemplateRepository templates, CobraQuickTextRepository quickTexts, FaxDocumentRepository faxDocuments)
    {
        this.templates = templates;
        this.quickTexts = quickTexts;
        this.faxDocuments = faxDocuments;

        Kinds =
        [
            new(TemplateKind.Email, "E-Mail-Vorlagen", true, PlaceholderHint, HasPlaceholders: true),
            new(TemplateKind.CallReason, "Anrufgründe", false, PlaceholderHint + " Wird an „Warum wird angerufen?“ angehängt.", HasPlaceholders: true),
            new(TemplateKind.CallSolution, "Lösungstexte", false, PlaceholderHint + " Wird an „Lösung“ angehängt.", HasPlaceholders: true),
            new(TemplateKind.Fax, "Fax-Vorlagen", false, PlaceholderHint, HasPlaceholders: true),
            new(TemplateKind.Snippet, "Schnelltextbausteine", false, "Kurze Texte zum Einfügen."),
            new(TemplateKind.CodeSnippet, "Schnelltextbausteine (Code)", false, "Befehle und Code, werden unverändert eingefügt."),
            new(TemplateKind.Entry, "Eintragsvorlagen (Wissensdatenbank)", false, "Startinhalt für neue Einträge der Wissensdatenbank."),
        ];
        selectedKind = Kinds[0];

        NewTemplateCommand = new RelayCommand(NewTemplate);
        DeleteTemplateCommand = new RelayCommand(DeleteTemplate, () => SelectedTemplate is not null);
        MoveTemplateUpCommand = new RelayCommand(() => MoveTemplate(-1), () => SelectedTemplate is not null);
        MoveTemplateDownCommand = new RelayCommand(() => MoveTemplate(1), () => SelectedTemplate is not null);

        NewQuickTextCommand = new RelayCommand(NewQuickText);
        DeleteQuickTextCommand = new RelayCommand(DeleteQuickText, () => SelectedQuickText is not null);
        MoveQuickTextUpCommand = new RelayCommand(() => MoveQuickText(-1), () => SelectedQuickText is not null);
        MoveQuickTextDownCommand = new RelayCommand(() => MoveQuickText(1), () => SelectedQuickText is not null);

        AddFaxCommand = new RelayCommand(AddFax);
        RemoveFaxCommand = new RelayCommand(RemoveFax, () => SelectedFax is not null);
        MoveFaxUpCommand = new RelayCommand(() => MoveFax(-1), () => SelectedFax is not null);
        MoveFaxDownCommand = new RelayCommand(() => MoveFax(1), () => SelectedFax is not null);
        OpenFaxCommand = new RelayCommand(OpenFax, () => SelectedFax is not null);

        LoadTemplates(null);
        LoadQuickTexts(null);
        LoadFax(null);
    }

    public Window? Owner { get; set; }

    // ------------------------------------------------------------------ Vorlagen

    public IReadOnlyList<TemplateKindOption> Kinds { get; }

    public ObservableCollection<TemplateItem> Templates { get; } = [];

    public TemplateKindOption SelectedKind
    {
        get => selectedKind;
        set
        {
            if (value is not null && SetProperty(ref selectedKind, value))
            {
                LoadTemplates(null);
            }
        }
    }

    public TemplateItem? SelectedTemplate
    {
        get => selectedTemplate;
        set
        {
            if (SetProperty(ref selectedTemplate, value))
            {
                DeleteTemplateCommand.NotifyCanExecuteChanged();
                MoveTemplateUpCommand.NotifyCanExecuteChanged();
                MoveTemplateDownCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand NewTemplateCommand { get; }
    public RelayCommand DeleteTemplateCommand { get; }
    public RelayCommand MoveTemplateUpCommand { get; }
    public RelayCommand MoveTemplateDownCommand { get; }

    private void LoadTemplates(long? select)
    {
        Templates.Clear();
        foreach (var template in templates.LoadByKind(SelectedKind.Kind))
        {
            Templates.Add(new TemplateItem(template, templates.Save));
        }

        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == select) ?? Templates.FirstOrDefault();
    }

    private void NewTemplate() => LoadTemplates(templates.Create(SelectedKind.Kind, "Neue Vorlage").Id);

    private void DeleteTemplate()
    {
        if (SelectedTemplate is { } item && Confirm($"Vorlage „{item.Name}“ löschen?"))
        {
            templates.Delete(item.Id);
            LoadTemplates(null);
        }
    }

    private void MoveTemplate(int delta)
    {
        if (SelectedTemplate is { } item && templates.Move(item.Id, delta))
        {
            LoadTemplates(item.Id);
        }
    }

    // ------------------------------------------------------------------ Cobra-Schnelltexte

    public ObservableCollection<QuickTextItem> QuickTexts { get; } = [];

    public QuickTextItem? SelectedQuickText
    {
        get => selectedQuickText;
        set
        {
            if (SetProperty(ref selectedQuickText, value))
            {
                DeleteQuickTextCommand.NotifyCanExecuteChanged();
                MoveQuickTextUpCommand.NotifyCanExecuteChanged();
                MoveQuickTextDownCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand NewQuickTextCommand { get; }
    public RelayCommand DeleteQuickTextCommand { get; }
    public RelayCommand MoveQuickTextUpCommand { get; }
    public RelayCommand MoveQuickTextDownCommand { get; }

    private void LoadQuickTexts(long? select)
    {
        QuickTexts.Clear();
        foreach (var quick in quickTexts.LoadAll())
        {
            QuickTexts.Add(new QuickTextItem(quick, quickTexts.Save));
        }

        SelectedQuickText = QuickTexts.FirstOrDefault(q => q.Id == select) ?? QuickTexts.FirstOrDefault();
    }

    private void NewQuickText() => LoadQuickTexts(quickTexts.Create("Neuer Schnelltext").Id);

    private void DeleteQuickText()
    {
        if (SelectedQuickText is { } item && Confirm($"Cobra-Schnelltext „{item.Name}“ löschen?"))
        {
            quickTexts.Delete(item.Id);
            LoadQuickTexts(null);
        }
    }

    private void MoveQuickText(int delta)
    {
        if (SelectedQuickText is { } item && quickTexts.Move(item.Id, delta))
        {
            LoadQuickTexts(item.Id);
        }
    }

    // ------------------------------------------------------------------ Fax-Dokumente

    public ObservableCollection<FaxItem> FaxDocuments { get; } = [];

    public FaxItem? SelectedFax
    {
        get => selectedFax;
        set
        {
            if (SetProperty(ref selectedFax, value))
            {
                RemoveFaxCommand.NotifyCanExecuteChanged();
                MoveFaxUpCommand.NotifyCanExecuteChanged();
                MoveFaxDownCommand.NotifyCanExecuteChanged();
                OpenFaxCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddFaxCommand { get; }
    public RelayCommand RemoveFaxCommand { get; }
    public RelayCommand MoveFaxUpCommand { get; }
    public RelayCommand MoveFaxDownCommand { get; }
    public RelayCommand OpenFaxCommand { get; }

    private void LoadFax(long? select)
    {
        FaxDocuments.Clear();
        foreach (var document in faxDocuments.LoadAll())
        {
            FaxDocuments.Add(new FaxItem(document, faxDocuments.Rename));
        }

        SelectedFax = FaxDocuments.FirstOrDefault(f => f.Id == select) ?? FaxDocuments.LastOrDefault();
    }

    private void AddFax()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Fax-Dokumente hinzufügen",
            Multiselect = true,
            Filter = "Dokumente (*.doc;*.docx;*.dot;*.dotx;*.rtf;*.odt;*.pdf;*.txt)|*.doc;*.docx;*.dot;*.dotx;*.rtf;*.odt;*.pdf;*.txt|Alle Dateien (*.*)|*.*",
        };
        if ((Owner is null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) != true)
        {
            return;
        }

        AddFaxFiles(dialog.FileNames);
    }

    /// <summary>Dateien als Fax-Dokumente übernehmen (Dateiauswahl oder Drag &amp; Drop aus dem Explorer).</summary>
    public void AddFaxFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            foreach (var file in files)
            {
                faxDocuments.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show("Die Datei konnte nicht übernommen werden.\n\n" + ex.Message, "PixFix – Fax-Dokumente", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        LoadFax(null);
    }

    private void RemoveFax()
    {
        if (SelectedFax is { } item && Confirm($"Fax-Dokument „{item.Name}“ entfernen?"))
        {
            faxDocuments.Remove(item.Id);
            LoadFax(null);
        }
    }

    private void MoveFax(int delta)
    {
        if (SelectedFax is { } item && faxDocuments.Move(item.Id, delta))
        {
            LoadFax(item.Id);
        }
    }

    private void OpenFax()
    {
        if (SelectedFax is not { } item)
        {
            return;
        }

        try
        {
            ShellOpener.OpenCopy(item.File, item.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Show("Das Dokument konnte nicht geöffnet werden.\n\n" + ex.Message, "PixFix – Fax-Dokumente", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool Confirm(string question) =>
        Show(question, "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is null ? MessageBox.Show(text, caption, buttons, icon) : MessageBox.Show(Owner, text, caption, buttons, icon);
}

/// <summary>Bearbeitbare Vorlage; jede Änderung wird sofort gespeichert.</summary>
public sealed class TemplateItem(Template model, Action<Template> save) : ObservableObject
{
    private Template model = model;

    public long Id => model.Id;

    public string Name
    {
        get => model.Name;
        set => Update(model with { Name = string.IsNullOrWhiteSpace(value) ? "Ohne Namen" : value.Trim() }, nameof(Name));
    }

    public string Subject
    {
        get => model.Subject;
        set => Update(model with { Subject = value ?? string.Empty }, nameof(Subject));
    }

    public string Body
    {
        get => model.Body;
        set => Update(model with { Body = value ?? string.Empty }, nameof(Body));
    }

    private void Update(Template updated, string property)
    {
        if (updated != model)
        {
            model = updated;
            save(updated);
            OnPropertyChanged(property);
        }
    }
}

/// <summary>Bearbeitbarer Cobra-Schnelltext; jede Änderung wird sofort gespeichert.</summary>
public sealed class QuickTextItem(CobraQuickText model, Action<CobraQuickText> save) : ObservableObject
{
    private CobraQuickText model = model;

    public long Id => model.Id;

    public string Name
    {
        get => model.Name;
        set => Update(model with { Name = string.IsNullOrWhiteSpace(value) ? "Ohne Namen" : value.Trim() }, nameof(Name));
    }

    public string CobraText
    {
        get => model.CobraText;
        set => Update(model with { CobraText = value ?? string.Empty }, nameof(CobraText));
    }

    public string NotesText
    {
        get => model.NotesText;
        set => Update(model with { NotesText = value ?? string.Empty }, nameof(NotesText));
    }

    public string WorkText
    {
        get => model.WorkText;
        set => Update(model with { WorkText = value ?? string.Empty }, nameof(WorkText));
    }

    /// <summary>Anruf danach auf „Erledigt“ setzen (schließt „Später erneut anrufen“ aus).</summary>
    public bool CompleteCall
    {
        get => model.CompleteCall;
        set
        {
            Update(model with { CompleteCall = value, ScheduleRecall = !value && model.ScheduleRecall }, nameof(CompleteCall));
            OnPropertyChanged(nameof(ScheduleRecall));
        }
    }

    /// <summary>Anruf danach auf „Später erneut anrufen“ setzen (mit Erinnerung).</summary>
    public bool ScheduleRecall
    {
        get => model.ScheduleRecall;
        set
        {
            Update(model with { ScheduleRecall = value, CompleteCall = !value && model.CompleteCall }, nameof(ScheduleRecall));
            OnPropertyChanged(nameof(CompleteCall));
        }
    }

    private void Update(CobraQuickText updated, string property)
    {
        if (updated != model)
        {
            model = updated;
            save(updated);
            OnPropertyChanged(property);
        }
    }
}

/// <summary>Fax-Dokument mit umbenennbarem Anzeigenamen.</summary>
public sealed class FaxItem(FaxDocument document, Action<long, string> rename) : ObservableObject
{
    private string name = document.Name;

    public long Id => document.Id;

    public string File => document.File;

    public string Name
    {
        get => name;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != name)
            {
                name = value.Trim();
                rename(Id, name);
                OnPropertyChanged();
            }
        }
    }
}
