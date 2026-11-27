using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixFix.App.Editing;
using PixFix.Core.Common;
using PixFix.Core.Workspaces;
using PixFix.Infrastructure.Workspaces;

namespace PixFix.App.Workspaces;

/// <summary>Reiter in der Liste. Der Eingeklappt-Zustand gilt für den ganzen Abschnitt und wird gespeichert.</summary>
public sealed class InformationItem(InformationListItem item, WorkspacesViewModel owner) : ObservableObject
{
    public InformationListItem Item { get; private set; } = item;

    public string Id => Item.Id;

    public string Title => Item.Title;

    public string Section => Item.Section;

    /// <summary>Für die Gruppenkopfzeile: bei aktiver Suche immer aufgeklappt, damit keine Treffer verborgen sind.</summary>
    public bool IsExpanded
    {
        get => owner.IsSearching || !Item.SectionCollapsed;
        set
        {
            if (owner.IsSearching || value == !Item.SectionCollapsed)
            {
                return;
            }

            owner.SetSectionCollapsed(Item.Section, !value);
        }
    }

    internal void Update(InformationListItem updated)
    {
        Item = updated;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Schreibbereiche: Notizen, Anleitungen, aktuelle Fehler, Werkzeuge (SPEC F3).</summary>
public sealed class WorkspacesViewModel : ObservableObject, IDocumentEditorHost
{
    private const string DefaultSection = "Allgemein";

    private readonly InformationRepository repository;
    private readonly ILogger<WorkspacesViewModel> logger;
    private Workspace? selectedWorkspace;
    private InformationItem? selectedItem;
    private EditorDocument? document;
    private string query = string.Empty;
    private string status = string.Empty;
    private string? pendingImageAssetId;
    private string editTitle = string.Empty;
    private string editSection = string.Empty;
    private bool isLoaded;
    private bool rebuilding;

    public WorkspacesViewModel(InformationRepository repository, ILogger<WorkspacesViewModel> logger)
    {
        this.repository = repository;
        this.logger = logger;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InformationItem.Section)));
        EntriesView.Filter = MatchesQuery;

        NewEntryCommand = new RelayCommand(CreateEntry, () => SelectedWorkspace is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => CanEdit);
        ToggleProtectedCommand = new RelayCommand(ToggleProtected, () => SelectedItem is not null);
        MoveUpCommand = new RelayCommand(() => MoveSelected(-1), () => SelectedItem is not null);
        MoveDownCommand = new RelayCommand(() => MoveSelected(+1), () => SelectedItem is not null);
        MoveSectionUpCommand = new RelayCommand(() => MoveSection(-1), () => SelectedItem is not null);
        MoveSectionDownCommand = new RelayCommand(() => MoveSection(+1), () => SelectedItem is not null);
        AddAttachmentCommand = new RelayCommand(AddAttachment, () => CanEdit);
        OpenAttachmentCommand = new RelayCommand<InformationAttachmentFile>(OpenAttachment);
        RemoveAttachmentCommand = new RelayCommand<InformationAttachmentFile>(RemoveAttachment, _ => CanEdit);
        ManageWorkspacesCommand = new RelayCommand(() => ManageWorkspacesRequested?.Invoke(this, EventArgs.Empty));
        ClearSearchCommand = new RelayCommand(() => Query = string.Empty);
        CopyCommand = new RelayCommand<CopyAction>(a =>
        {
            if (a is not null)
            {
                Clipboard.SetText(a.Text);
                Status = "In die Zwischenablage kopiert.";
            }
        });
        DeleteCopyActionCommand = new RelayCommand<CopyAction>(DeleteCopyAction, _ => CanEdit);
    }

    /// <summary>Die Ansicht öffnet daraufhin die Bereichsverwaltung.</summary>
    public event EventHandler? ManageWorkspacesRequested;

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    public ObservableCollection<InformationItem> Entries { get; } = [];

    public ICollectionView EntriesView { get; }

    public ObservableCollection<string> SectionNames { get; } = [];

    public ObservableCollection<InformationAttachmentFile> Attachments { get; } = [];

    public ObservableCollection<CopyAction> CopyActions { get; } = [];

    public RelayCommand NewEntryCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ToggleProtectedCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveSectionUpCommand { get; }
    public RelayCommand MoveSectionDownCommand { get; }
    public RelayCommand AddAttachmentCommand { get; }
    public RelayCommand<InformationAttachmentFile> OpenAttachmentCommand { get; }
    public RelayCommand<InformationAttachmentFile> RemoveAttachmentCommand { get; }
    public RelayCommand ManageWorkspacesCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand<CopyAction> CopyCommand { get; }
    public RelayCommand<CopyAction> DeleteCopyActionCommand { get; }

    ICommand IDocumentEditorHost.CopyCommand => CopyCommand;

    ICommand IDocumentEditorHost.DeleteCopyActionCommand => DeleteCopyActionCommand;

    public Workspace? SelectedWorkspace
    {
        get => selectedWorkspace;
        set
        {
            if (SetProperty(ref selectedWorkspace, value))
            {
                SelectedItem = null;
                LoadEntries();
                NewEntryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public InformationItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            // Beim Neuaufbau der Liste setzt sie kurz „keine Auswahl“ – das soll den Editor nicht leeren.
            if (rebuilding && value is null)
            {
                return;
            }

            if (SetProperty(ref selectedItem, value))
            {
                LoadSelected();
            }
        }
    }

    /// <summary>Durchsucht Titel und Inhalt der Reiter des Bereichs; Fundstellen werden im Editor markiert.</summary>
    public string Query
    {
        get => query;
        set
        {
            if (SetProperty(ref query, value))
            {
                EntriesView.Refresh();
                foreach (var entry in Entries)
                {
                    entry.Update(entry.Item);
                }
            }
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    public bool CanEdit => SelectedItem is { Item.IsProtected: false } && SelectedWorkspace is { IsProtected: false };

    public bool IsReadOnly => !CanEdit;

    public bool IsLoadingDocument => false;

    public EditorDocument? Document
    {
        get => document;
        private set => SetProperty(ref document, value);
    }

    public string? PendingImageAssetId
    {
        get => pendingImageAssetId;
        set => SetProperty(ref pendingImageAssetId, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public string EditTitle
    {
        get => editTitle;
        set
        {
            if (SetProperty(ref editTitle, value))
            {
                SaveMetadata();
            }
        }
    }

    public string EditSection
    {
        get => editSection;
        set
        {
            if (SetProperty(ref editSection, value))
            {
                SaveMetadata();
            }
        }
    }

    // ------------------------------------------------------------------ Laden

    public void EnsureLoaded()
    {
        if (!isLoaded)
        {
            Reload();
        }
    }

    /// <summary>Bereiche neu laden (nach Import oder Bereichsverwaltung); Auswahl bleibt nach Möglichkeit erhalten.</summary>
    public void Reload()
    {
        isLoaded = true;
        var keep = SelectedWorkspace?.Id;
        Workspaces.Clear();
        foreach (var workspace in repository.LoadWorkspaces())
        {
            Workspaces.Add(workspace);
        }

        var target = Workspaces.FirstOrDefault(w => w.Id == keep) ?? Workspaces.FirstOrDefault();
        if (target == SelectedWorkspace)
        {
            LoadEntries();
        }
        else
        {
            SelectedWorkspace = target;
        }
    }

    private void LoadEntries()
    {
        var keep = SelectedItem?.Id;
        rebuilding = true;
        try
        {
            Entries.Clear();
            SectionNames.Clear();
            if (SelectedWorkspace is not null)
            {
                foreach (var item in repository.LoadEntries(SelectedWorkspace.Id))
                {
                    Entries.Add(new InformationItem(item, this));
                    if (!SectionNames.Contains(item.Section))
                    {
                        SectionNames.Add(item.Section);
                    }
                }
            }
        }
        finally
        {
            rebuilding = false;
        }

        var again = Entries.FirstOrDefault(e => e.Id == keep);
        if (again is not null)
        {
            selectedItem = again;
            OnPropertyChanged(nameof(SelectedItem));
            RefreshCommandStates();
        }
        else if (keep is not null)
        {
            SelectedItem = null;
        }
    }

    private void LoadSelected()
    {
        editTitle = SelectedItem?.Title ?? string.Empty;
        editSection = SelectedItem?.Section ?? string.Empty;
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditSection));

        CopyActions.Clear();
        Attachments.Clear();
        if (SelectedItem is null)
        {
            Document = null;
        }
        else
        {
            foreach (var action in repository.LoadCopyActions(SelectedItem.Id))
            {
                CopyActions.Add(action);
            }

            foreach (var attachment in repository.LoadAttachments(SelectedItem.Id))
            {
                Attachments.Add(attachment);
            }

            try
            {
                var loaded = repository.LoadDocument(SelectedItem.Id);
                Document = loaded is null ? null : new EditorDocument(SelectedItem.Id, loaded.Format, loaded.Text, []);
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
            {
                logger.LogError(ex, "Reiter {EntryId} konnte nicht geladen werden", SelectedItem.Id);
                Document = new EditorDocument(null, ContentFormat.PlainText, "Der Reiter konnte nicht geladen werden: " + ex.Message, []);
            }
        }

        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsReadOnly));
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleProtectedCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveSectionUpCommand.NotifyCanExecuteChanged();
        MoveSectionDownCommand.NotifyCanExecuteChanged();
        AddAttachmentCommand.NotifyCanExecuteChanged();
        RemoveAttachmentCommand.NotifyCanExecuteChanged();
        DeleteCopyActionCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesQuery(object item)
    {
        if (!IsSearching || item is not InformationItem entry)
        {
            return true;
        }

        var q = Query.Trim();
        return entry.Item.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || entry.Item.Section.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || entry.Item.PlainText.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    // ------------------------------------------------------------------ Bearbeiten

    public void SaveContent(string entryId, SavedDocument saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        try
        {
            repository.SaveContent(entryId, saved.Xaml, saved.PlainText, saved.ImageAssetIds);
            var entry = Entries.FirstOrDefault(e => e.Id == entryId);
            entry?.Update(entry.Item with { PlainText = saved.PlainText });
            Status = $"Gespeichert {DateTime.Now:HH:mm:ss}";
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    public string StoreImage(byte[] png) => repository.StoreImage(png, ".png");

    public string? GetAssetFile(string assetId) => repository.GetAssetFile(assetId);

    public void AddCopyAction(string text, int occurrence)
    {
        if (SelectedItem is not { } item || !CanEdit || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Status = repository.AddCopyAction(item.Id, new CopyAction(text.Trim(), occurrence))
                ? "Kopier-Button angelegt."
                : "Diesen Kopier-Button gibt es schon.";
            CopyActions.Clear();
            foreach (var action in repository.LoadCopyActions(item.Id))
            {
                CopyActions.Add(action);
            }
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    private void DeleteCopyAction(CopyAction? action)
    {
        if (action is null || SelectedItem is not { } item)
        {
            return;
        }

        Try(() =>
        {
            repository.DeleteCopyAction(item.Id, action.Text);
            CopyActions.Remove(action);
        });
    }

    private void SaveMetadata()
    {
        if (SelectedItem is not { } item || !CanEdit)
        {
            return;
        }

        if (item.Title == EditTitle.Trim() && item.Section == EditSection.Trim())
        {
            return;
        }

        Try(() =>
        {
            repository.UpdateEntry(item.Id, EditTitle, EditSection);
            Status = $"Gespeichert {DateTime.Now:HH:mm:ss}";
            // Erst nach dem aktuellen Klick neu aufbauen (z. B. Klick in die Liste).
            Application.Current.Dispatcher.BeginInvoke(new Action(LoadEntries), System.Windows.Threading.DispatcherPriority.Background);
        });
    }

    private void CreateEntry()
    {
        if (SelectedWorkspace is not { } workspace)
        {
            return;
        }

        var section = SelectedItem?.Section ?? SectionNames.FirstOrDefault() ?? DefaultSection;
        Try(() =>
        {
            var id = repository.CreateEntry(workspace.Id, section, "Neuer Reiter");
            Query = string.Empty;
            LoadEntries();
            SelectedItem = Entries.FirstOrDefault(e => e.Id == id);
            Status = "Neuer Reiter angelegt – Titel und Abschnitt oben eintragen.";
        });
    }

    private void DeleteSelected()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (MessageBox.Show($"Reiter „{item.Title}“ löschen?", "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Try(() =>
        {
            repository.DeleteEntry(item.Id);
            SelectedItem = null;
            LoadEntries();
            Status = $"„{item.Title}“ gelöscht.";
        });
    }

    private void ToggleProtected()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        repository.SetProtected(item.Id, !item.Item.IsProtected);
        item.Update(item.Item with { IsProtected = !item.Item.IsProtected });
        RefreshCommandStates();
    }

    private void MoveSelected(int direction)
    {
        if (SelectedItem is { } item)
        {
            repository.MoveEntry(item.Id, direction);
            LoadEntries();
        }
    }

    private void MoveSection(int direction)
    {
        if (SelectedItem is { } item && SelectedWorkspace is { } workspace)
        {
            repository.MoveSection(workspace.Id, item.Section, direction);
            LoadEntries();
        }
    }

    internal void SetSectionCollapsed(string section, bool collapsed)
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        repository.SetSectionCollapsed(SelectedWorkspace.Id, section, collapsed);
        foreach (var entry in Entries.Where(e => e.Section == section))
        {
            entry.Update(entry.Item with { SectionCollapsed = collapsed });
        }
    }

    // ------------------------------------------------------------------ PDF-Anhänge

    private void AddAttachment()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = "PDF anhängen", Filter = "PDF-Dateien|*.pdf|Alle Dateien|*.*", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            AddAttachments(dialog.FileNames);
        }
    }

    /// <summary>Dateien an den ausgewählten Reiter anhängen (Dateiauswahl oder Drag &amp; Drop aus dem Explorer).</summary>
    public void AddAttachments(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (SelectedItem is not { } item || !CanEdit)
        {
            return;
        }

        Try(() =>
        {
            foreach (var file in files)
            {
                repository.AddAttachment(item.Id, file);
            }

            Attachments.Clear();
            foreach (var attachment in repository.LoadAttachments(item.Id))
            {
                Attachments.Add(attachment);
            }
        });
    }

    /// <summary>Öffnet eine Kopie mit dem ursprünglichen Dateinamen, damit der PDF-Betrachter einen lesbaren Namen zeigt.</summary>
    private void OpenAttachment(InformationAttachmentFile? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "PixFix-Anhaenge");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, string.Join("_", attachment.DisplayName.Split(Path.GetInvalidFileNameChars())));
            File.Copy(attachment.File, copy, overwrite: true);
            Process.Start(new ProcessStartInfo(copy) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            logger.LogWarning(ex, "Anhang {Name} konnte nicht geöffnet werden", attachment.DisplayName);
            Status = "Anhang konnte nicht geöffnet werden: " + ex.Message;
        }
    }

    private void RemoveAttachment(InformationAttachmentFile? attachment)
    {
        if (attachment is null || SelectedItem is not { } item)
        {
            return;
        }

        if (MessageBox.Show($"Anhang „{attachment.DisplayName}“ entfernen?", "PixFix", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Try(() =>
        {
            repository.RemoveAttachment(item.Id, attachment.Ordinal);
            Attachments.Remove(attachment);
        });
    }

    // ------------------------------------------------------------------ Bereichsverwaltung

    public string? ChangeWorkspaces(Action<InformationRepository> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        try
        {
            change(repository);
            Reload();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public IReadOnlyList<Workspace> LoadWorkspaces() => repository.LoadWorkspaces();

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }
}
