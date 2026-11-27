using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Knowledge;
using PixFix.Infrastructure.Ocr;

namespace PixFix.App.Knowledge;

public sealed record KnowledgeHit(string Id, string Title, string SectionName, string Snippet, bool IsFavorite, bool IsArchived)
{
    /// <summary>In der Trefferliste: Textausschnitt bei Suche, sonst nichts (der Abschnitt steht als Gruppenkopf darüber).</summary>
    public string SnippetOrSection => Snippet.Length > 0 ? SectionName + " · " + Snippet : string.Empty;
}

/// <summary>Wissensdatenbank: nachschlagen, suchen und bearbeiten (SPEC F1/F2).</summary>
public sealed class KnowledgeViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly KnowledgeRepository repository;
    private readonly KnowledgeDocumentService documents;
    private readonly OcrService ocr;
    private readonly ILogger<KnowledgeViewModel> logger;
    private List<KnowledgeListItem> entries = [];
    private CancellationTokenSource? searchDelay;
    private int documentRequest;
    private bool rebuildingResults;
    private bool isLoaded;

    private string query = string.Empty;
    private string summary = string.Empty;
    private string status = string.Empty;
    private bool showArchived;
    private KnowledgeHit? selectedHit;
    private KnowledgeListItem? selectedEntry;
    private KnowledgeDocument? document;
    private bool isLoadingDocument;
    private string editTitle = string.Empty;
    private string editSection = string.Empty;
    private string editTags = string.Empty;

    public KnowledgeViewModel(KnowledgeRepository repository, KnowledgeDocumentService documents, OcrService ocr, ILogger<KnowledgeViewModel> logger)
    {
        this.repository = repository;
        this.documents = documents;
        this.ocr = ocr;
        this.logger = logger;
        ResultsView = CollectionViewSource.GetDefaultView(Results);
        ReloadCommand = new RelayCommand(Reload);
        ClearSearchCommand = new RelayCommand(() => Query = string.Empty);
        NewEntryCommand = new RelayCommand(CreateEntry);
        DeleteCommand = new RelayCommand(DeleteSelected, () => CanEdit);
        ToggleFavoriteCommand = new RelayCommand(() => UpdateFlags(f => f with { IsFavorite = !f.IsFavorite }), () => SelectedEntry is not null);
        ToggleProtectedCommand = new RelayCommand(() => UpdateFlags(f => f with { IsProtected = !f.IsProtected }), () => SelectedEntry is not null);
        ToggleArchivedCommand = new RelayCommand(() => UpdateFlags(f => f with { IsArchived = !f.IsArchived }), () => SelectedEntry is not null);
        ShowTrashCommand = new RelayCommand(() => TrashRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Die Ansicht öffnet daraufhin den Papierkorb.</summary>
    public event EventHandler? TrashRequested;

    public ObservableCollection<KnowledgeHit> Results { get; } = [];

    /// <summary>Ohne Suchbegriff nach Abschnitten gruppiert, mit Suchbegriff nach Relevanz sortiert.</summary>
    public ICollectionView ResultsView { get; }

    public ObservableCollection<string> SectionNames { get; } = [];

    public RelayCommand ReloadCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand NewEntryCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand ToggleProtectedCommand { get; }
    public RelayCommand ToggleArchivedCommand { get; }
    public RelayCommand ShowTrashCommand { get; }

    public string Query
    {
        get => query;
        set
        {
            if (SetProperty(ref query, value))
            {
                _ = SearchDelayedAsync();
            }
        }
    }

    public bool ShowArchived
    {
        get => showArchived;
        set
        {
            if (SetProperty(ref showArchived, value))
            {
                ApplySearch();
            }
        }
    }

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    /// <summary>Kurze Rückmeldung (gespeichert, Fehler …) über dem Editor.</summary>
    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public KnowledgeHit? SelectedHit
    {
        get => selectedHit;
        set
        {
            // Beim Neuaufbau der Trefferliste setzt die Liste kurz „keine Auswahl“ – das soll das Dokument nicht schließen.
            if (rebuildingResults && value is null)
            {
                return;
            }

            if (SetProperty(ref selectedHit, value))
            {
                SelectEntry(value?.Id);
                _ = LoadDocumentAsync(value);
            }
        }
    }

    /// <summary>Daten des ausgewählten Eintrags (Titel, Flags …).</summary>
    public KnowledgeListItem? SelectedEntry
    {
        get => selectedEntry;
        private set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(IsReadOnly));
                DeleteCommand.NotifyCanExecuteChanged();
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                ToggleProtectedCommand.NotifyCanExecuteChanged();
                ToggleArchivedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Bearbeiten ist nur bei ausgewähltem, nicht geschütztem Eintrag möglich.</summary>
    public bool CanEdit => SelectedEntry is { IsProtected: false };

    public bool IsReadOnly => !CanEdit;

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

    public string EditTags
    {
        get => editTags;
        set
        {
            if (SetProperty(ref editTags, value))
            {
                SaveMetadata();
            }
        }
    }

    /// <summary>Aktuell angezeigtes Dokument; die Ansicht lädt es in den Editor.</summary>
    public KnowledgeDocument? Document
    {
        get => document;
        private set => SetProperty(ref document, value);
    }

    public bool IsLoadingDocument
    {
        get => isLoadingDocument;
        private set => SetProperty(ref isLoadingDocument, value);
    }

    // ------------------------------------------------------------------ Laden und Suchen

    /// <summary>Lädt die Einträge beim ersten Anzeigen.</summary>
    public void EnsureLoaded()
    {
        if (!isLoaded)
        {
            Reload();
        }
    }

    public void Reload()
    {
        entries = repository.LoadAll().ToList();
        isLoaded = true;
        SectionNames.Clear();
        foreach (var name in repository.LoadSectionNames())
        {
            SectionNames.Add(name);
        }

        logger.LogInformation("Wissensdatenbank geladen: {Count} Einträge", entries.Count);
        ApplySearch();
        SelectEntry(SelectedHit?.Id);
    }

    private async Task SearchDelayedAsync()
    {
        searchDelay?.Cancel();
        searchDelay?.Dispose();
        searchDelay = new CancellationTokenSource();
        var token = searchDelay.Token;
        try
        {
            await Task.Delay(SearchDelay, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        ApplySearch();
    }

    private void ApplySearch()
    {
        var hits = KnowledgeSearch.Search(entries, Query, ShowArchived);
        var keepSelection = SelectedHit?.Id;

        rebuildingResults = true;
        try
        {
            Results.Clear();
            foreach (var hit in hits)
            {
                Results.Add(new KnowledgeHit(
                    hit.Entry.Id, hit.Entry.Title, hit.Entry.SectionName, hit.Snippet, hit.Entry.IsFavorite, hit.Entry.IsArchived));
            }
        }
        finally
        {
            rebuildingResults = false;
        }

        ResultsView.GroupDescriptions.Clear();
        if (string.IsNullOrWhiteSpace(Query))
        {
            ResultsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KnowledgeHit.SectionName)));
            Summary = $"{Results.Count} Einträge";
        }
        else
        {
            Summary = Results.Count == 1 ? "1 Treffer" : $"{Results.Count} Treffer";
        }

        // Ausgewählten Eintrag behalten, wenn er weiterhin in der Liste ist (Dokument wird nicht neu geladen).
        var previous = Results.FirstOrDefault(r => r.Id == keepSelection);
        if (previous is not null)
        {
            selectedHit = previous;
            OnPropertyChanged(nameof(SelectedHit));
        }
        else if (keepSelection is not null)
        {
            SelectedHit = null;
        }
    }

    private void SelectEntry(string? id)
    {
        SelectedEntry = id is null ? null : entries.FirstOrDefault(e => e.Id == id);

        // Felder ohne Speichern setzen.
        editTitle = SelectedEntry?.Title ?? string.Empty;
        editSection = SelectedEntry?.SectionName ?? string.Empty;
        editTags = SelectedEntry?.Tags ?? string.Empty;
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditSection));
        OnPropertyChanged(nameof(EditTags));
    }

    private async Task LoadDocumentAsync(KnowledgeHit? hit)
    {
        var request = ++documentRequest;
        if (hit is null)
        {
            Document = null;
            return;
        }

        IsLoadingDocument = true;
        try
        {
            // Die erste Aufbereitung großer Dokumente (Bilder umwandeln) kann einige Sekunden dauern.
            var loaded = await Task.Run(() => documents.Load(hit.Id)).ConfigureAwait(true);
            if (request == documentRequest)
            {
                Document = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            logger.LogError(ex, "Dokument {EntryId} konnte nicht geladen werden", hit.Id);
            Document = new KnowledgeDocument("text", "Das Dokument konnte nicht geladen werden: " + ex.Message, []);
        }
        finally
        {
            if (request == documentRequest)
            {
                IsLoadingDocument = false;
            }
        }
    }

    // ------------------------------------------------------------------ Bearbeiten

    /// <summary>Wird von der Ansicht beim automatischen Speichern aufgerufen.</summary>
    public void SaveContent(string entryId, SavedDocument saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        try
        {
            repository.SaveContent(entryId, new KnowledgeContentUpdate(saved.Xaml, saved.PlainText, saved.ImageAssetIds));
            var index = entries.FindIndex(e => e.Id == entryId);
            if (index >= 0)
            {
                // Nur den Suchtext aktualisieren – kein Neuaufbau der Liste während der Arbeit.
                entries[index] = entries[index] with { PlainText = saved.PlainText, UpdatedUtc = DateTimeOffset.UtcNow };
            }

            Status = $"Gespeichert {DateTime.Now:HH:mm:ss}";
            _ = RecognizeNewImagesAsync(entryId);
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
            logger.LogWarning(ex, "Speichern von {EntryId} abgelehnt", entryId);
        }
    }

    /// <summary>Bild aus dem Editor in der Ablage speichern.</summary>
    public string StoreImage(byte[] png) => repository.StoreImage(png, ".png");

    public string? GetAssetFile(string assetId) => repository.GetAssetFile(assetId);

    private void SaveMetadata()
    {
        if (SelectedEntry is not { } entry || !CanEdit)
        {
            return;
        }

        var sectionChanged = !string.Equals(entry.SectionName, EditSection.Trim(), StringComparison.Ordinal);
        var titleChanged = !string.Equals(entry.Title, EditTitle.Trim(), StringComparison.Ordinal);
        if (!sectionChanged && !titleChanged && string.Equals(entry.Tags, EditTags.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            repository.UpdateMetadata(entry.Id, EditTitle, EditSection, EditTags);
            Status = $"Gespeichert {DateTime.Now:HH:mm:ss}";

            // Liste erst nach dem aktuellen Klick neu aufbauen, damit z. B. ein Klick in die Trefferliste nicht verloren geht.
            Application.Current.Dispatcher.BeginInvoke(new Action(Reload), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    private void CreateEntry()
    {
        var section = SelectedEntry?.SectionName ?? KnowledgeRepository.DefaultSection;
        var id = repository.CreateEntry(section, "Neuer Eintrag");
        Query = string.Empty;
        Reload();
        SelectedHit = Results.FirstOrDefault(r => r.Id == id);
        Status = "Neuer Eintrag angelegt – Titel oben eintragen.";
    }

    private void DeleteSelected()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"„{entry.Title}“ in den Papierkorb verschieben?",
            "PixFix",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            repository.MoveToTrash(entry.Id);
            SelectedHit = null;
            Reload();
            Status = $"„{entry.Title}“ liegt im Papierkorb.";
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    private void UpdateFlags(Func<KnowledgeListItem, KnowledgeListItem> change)
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        var updated = change(entry);
        repository.SetFlags(entry.Id, updated.IsFavorite, updated.IsProtected, updated.IsArchived);
        Reload();
    }

    // ------------------------------------------------------------------ Papierkorb

    public IReadOnlyList<KnowledgeTrashItem> LoadTrash() => repository.LoadTrash();

    public void RestoreFromTrash(string trashId)
    {
        var id = repository.Restore(trashId);
        Query = string.Empty;
        Reload();
        SelectedHit = Results.FirstOrDefault(r => r.Id == id);
    }

    public void DeleteFromTrash(string trashId) => repository.DeletePermanently(trashId);

    // ------------------------------------------------------------------ Texterkennung

    /// <summary>Neue Bilder im Hintergrund per OCR auslesen; der Text wird durchsuchbar.</summary>
    private async Task RecognizeNewImagesAsync(string entryId)
    {
        foreach (var image in repository.ImagesWithoutOcr(entryId))
        {
            try
            {
                var text = await ocr.RecognizeAsync(image.File).ConfigureAwait(true);
                if (text.Length == 0)
                {
                    continue;
                }

                repository.AddImageOcr(entryId, image.AssetId, text);
                var index = entries.FindIndex(e => e.Id == entryId);
                if (index >= 0)
                {
                    entries[index] = entries[index] with { OcrText = (entries[index].OcrText + " " + text).Trim() };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                logger.LogWarning(ex, "Texterkennung für Bild {AssetId} fehlgeschlagen", image.AssetId);
            }
        }
    }
}
