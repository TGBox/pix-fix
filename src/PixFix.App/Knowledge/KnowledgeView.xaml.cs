using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PixFix.Core.Common;
using PixFix.Core.Knowledge;
using PixFix.Infrastructure.Knowledge;

namespace PixFix.App.Knowledge;

/// <summary>
/// Ansicht der Wissensdatenbank. Der Code-Behind lädt nur das Dokument in den Viewer und springt zu Fundstellen –
/// beides geht in WPF nicht per Datenbindung. Die Anzeige ist schreibgeschützt; Fundstellen werden nur markiert,
/// der Inhalt wird nie verändert.
/// </summary>
public partial class KnowledgeView : UserControl
{
    private const int MaxHits = 500;

    private KnowledgeViewModel? viewModel;
    private List<TextRange> hits = [];
    private int currentHit = -1;

    public KnowledgeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => viewModel?.EnsureLoaded();
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveHit(+1)), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveHit(-1)), Key.F3, ModifierKeys.Shift));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = e.NewValue as KnowledgeViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ShowDocument(viewModel.Document);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KnowledgeViewModel.Document))
        {
            ShowDocument(viewModel?.Document);
        }
        else if (e.PropertyName == nameof(KnowledgeViewModel.Query))
        {
            // Neue Fundstellen erst nach der verzögerten Suche markieren.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(HighlightQuery));
        }
    }

    private void ShowDocument(KnowledgeDocument? document)
    {
        var flow = new FlowDocument { PagePadding = new Thickness(12) };
        if (document is not null)
        {
            if (document.Format == ContentFormat.Rtf)
            {
                try
                {
                    var range = new TextRange(flow.ContentStart, flow.ContentEnd);
                    using var stream = new MemoryStream(Encoding.Latin1.GetBytes(document.Text));
                    range.Load(stream, DataFormats.Rtf);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
                {
                    flow = new FlowDocument(new Paragraph(new Run("Das Dokument konnte nicht angezeigt werden: " + ex.Message)));
                }
            }
            else
            {
                flow.Blocks.Add(new Paragraph(new Run(document.Text)));
            }
        }

        Viewer.Document = flow;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(HighlightQuery));
    }

    /// <summary>Sucht alle Fundstellen des Suchbegriffs (bzw. seiner Wörter) und springt zur ersten.</summary>
    private void HighlightQuery()
    {
        hits = [];
        currentHit = -1;
        var query = viewModel?.Query?.Trim() ?? string.Empty;
        if (query.Length > 0 && Viewer.Document is not null)
        {
            hits = FindAll(Viewer.Document, query);
            if (hits.Count == 0)
            {
                foreach (var token in KnowledgeSearch.Tokens(query))
                {
                    hits = FindAll(Viewer.Document, token);
                    if (hits.Count > 0)
                    {
                        break;
                    }
                }
            }
        }

        HitNavigation.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hits.Count > 0)
        {
            MoveHit(+1);
        }
    }

    private void OnNextHit(object sender, RoutedEventArgs e) => MoveHit(+1);

    private void OnPreviousHit(object sender, RoutedEventArgs e) => MoveHit(-1);

    private void MoveHit(int direction)
    {
        if (hits.Count == 0)
        {
            return;
        }

        currentHit = (currentHit + direction + hits.Count) % hits.Count;
        var hit = hits[currentHit];
        Viewer.Selection.Select(hit.Start, hit.End);
        HitCounter.Text = $"Treffer {currentHit + 1} von {hits.Count}{(hits.Count == MaxHits ? "+" : string.Empty)}";

        // Fundstelle mit etwas Abstand zum oberen Rand anzeigen.
        var rect = hit.Start.GetCharacterRect(LogicalDirection.Forward);
        if (!rect.IsEmpty)
        {
            Viewer.ScrollToVerticalOffset(Math.Max(0, Viewer.VerticalOffset + rect.Top - 60));
        }
    }

    /// <summary>Fundstellen innerhalb einzelner Textabschnitte (Runs), ohne Groß-/Kleinschreibung.</summary>
    private static List<TextRange> FindAll(FlowDocument document, string text)
    {
        var result = new List<TextRange>();
        var pointer = document.ContentStart;
        while (pointer is not null && result.Count < MaxHits)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                var index = 0;
                while ((index = run.IndexOf(text, index, StringComparison.CurrentCultureIgnoreCase)) >= 0 && result.Count < MaxHits)
                {
                    var start = pointer.GetPositionAtOffset(index);
                    var end = start?.GetPositionAtOffset(text.Length);
                    if (start is not null && end is not null)
                    {
                        result.Add(new TextRange(start, end));
                    }

                    index += text.Length;
                }
            }

            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }

        return result;
    }
}
