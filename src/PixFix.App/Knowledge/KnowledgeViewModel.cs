using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Knowledge;

namespace PixFix.App.Knowledge;

public sealed record KnowledgeHit(string Id, string Title, string SectionName, string Snippet, bool IsFavorite)
{
    /// <summary>In der Trefferliste: Textausschnitt bei Suche, sonst nichts (der Abschnitt steht als Gruppenkopf darüber).</summary>
    public string SnippetOrSection => Snippet.Length > 0 ? SectionName + " · " + Snippet : string.Empty;
}

/// <summary>Wissensdatenbank zum Nachschlagen (SPEC F1/F2, Phase 2a: lesen und suchen).</summary>
public sealed class KnowledgeViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly KnowledgeRepository repository;
    private readonly KnowledgeDocumentService documents;
    private readonly ILogger<KnowledgeViewModel> logger;
    private IReadOnlyList<KnowledgeListItem> entries = [];
    private CancellationTokenSource? searchDelay;
    private int documentRequest;

    private string query = string.Empty;
    private string summary = string.Empty;
    private KnowledgeHit? selectedHit;
    private KnowledgeDocument? document;
    private bool isLoadingDocument;
    private bool isLoaded;
    private bool rebuildingResults;

    public KnowledgeViewModel(KnowledgeRepository repository, KnowledgeDocumentService documents, ILogger<KnowledgeViewModel> logger)
    {
        this.repository = repository;
        this.documents = documents;
        this.logger = logger;
        ResultsView = CollectionViewSource.GetDefaultView(Results);
        ReloadCommand = new RelayCommand(Reload);
        ClearSearchCommand = new RelayCommand(() => Query = string.Empty);
    }

    public ObservableCollection<KnowledgeHit> Results { get; } = [];

    /// <summary>Ohne Suchbegriff nach Abschnitten gruppiert, mit Suchbegriff nach Relevanz sortiert.</summary>
    public ICollectionView ResultsView { get; }

    public RelayCommand ReloadCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

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

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
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
                _ = LoadDocumentAsync(value);
            }
        }
    }

    /// <summary>Aktuell angezeigtes Dokument; die Ansicht lädt es in den Viewer.</summary>
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
        entries = repository.LoadAll();
        isLoaded = true;
        logger.LogInformation("Wissensdatenbank geladen: {Count} Einträge", entries.Count);
        ApplySearch();
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
        var hits = KnowledgeSearch.Search(entries, Query);
        var keepSelection = SelectedHit?.Id;

        rebuildingResults = true;
        try
        {
            Results.Clear();
            foreach (var hit in hits)
            {
                Results.Add(new KnowledgeHit(hit.Entry.Id, hit.Entry.Title, hit.Entry.SectionName, hit.Snippet, hit.Entry.IsFavorite));
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
            Document = new KnowledgeDocument("text", "Das Dokument konnte nicht geladen werden: " + ex.Message);
        }
        finally
        {
            if (request == documentRequest)
            {
                IsLoadingDocument = false;
            }
        }
    }
}
