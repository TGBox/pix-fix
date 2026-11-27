using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using PixFix.Core.Templates;

namespace PixFix.App.Calls;

/// <summary>
/// Anrufansicht. Die beiden Textfelder „Warum wird angerufen?“ und „Lösung“ sind RichTextBoxen (RTF wie in PixFix 69);
/// sie werden 1 s nach der letzten Eingabe, beim Verlassen des Feldes und beim Wechsel des Anrufs gespeichert.
/// </summary>
public partial class CallsView : UserControl
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer saveTimer;
    private CallsViewModel? viewModel;
    private CallItemViewModel? current;
    private Window? window;
    private bool loading;

    public CallsView()
    {
        InitializeComponent();
        saveTimer = new DispatcherTimer { Interval = SaveDelay };
        saveTimer.Tick += (_, _) => FlushTexts();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        viewModel = DataContext as CallsViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.AppendTextRequested += OnAppendText;
        viewModel.TrashRequested += OnTrashRequested;
        window = Window.GetWindow(this);
        if (window is not null)
        {
            window.Closing += OnWindowClosing;
        }

        ShowTexts(viewModel.SelectedCall);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FlushTexts();
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel.AppendTextRequested -= OnAppendText;
            viewModel.TrashRequested -= OnTrashRequested;
        }

        if (window is not null)
        {
            window.Closing -= OnWindowClosing;
        }

        viewModel = null;
        current = null;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e) => FlushTexts();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CallsViewModel.SelectedCall))
        {
            FlushTexts();
            ShowTexts(viewModel?.SelectedCall);
        }
    }

    // ------------------------------------------------------------------ Texte laden/speichern

    private void ShowTexts(CallItemViewModel? item)
    {
        current = item;
        loading = true;
        try
        {
            Load(ReasonBox, item?.Model.ReasonRtf, item?.Model.ReasonText);
            Load(SolutionBox, item?.Model.SolutionRtf, item?.Model.SolutionText);
        }
        finally
        {
            loading = false;
        }
    }

    private static void Load(RichTextBox box, string? rtf, string? text)
    {
        var document = new FlowDocument { PagePadding = new Thickness(2) };
        document.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        box.Document = document;
        if (!string.IsNullOrWhiteSpace(rtf))
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
                new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Rtf);
                return;
            }
            catch (ArgumentException)
            {
                // Beschädigtes RTF: auf den Klartext ausweichen.
            }
        }

        new TextRange(document.ContentStart, document.ContentEnd).Text = text ?? string.Empty;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loading && current is not null)
        {
            saveTimer.Stop();
            saveTimer.Start();
        }
    }

    private void OnTextLostFocus(object sender, RoutedEventArgs e) => FlushTexts();

    private void FlushTexts()
    {
        if (!saveTimer.IsEnabled || current is null)
        {
            saveTimer.Stop();
            return;
        }

        saveTimer.Stop();
        var (reasonText, reasonRtf) = Read(ReasonBox);
        var (solutionText, solutionRtf) = Read(SolutionBox);
        current.UpdateTexts(reasonText, reasonRtf, solutionText, solutionRtf);
    }

    private static (string Text, string Rtf) Read(RichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        var text = range.Text.TrimEnd('\r', '\n');
        if (text.Trim().Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        return (text, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private void OnAppendText(object? sender, (bool Reason, string Text) e)
    {
        var box = e.Reason ? ReasonBox : SolutionBox;
        var document = box.Document;
        if (new TextRange(document.ContentStart, document.ContentEnd).Text.Trim().Length == 0)
        {
            document.Blocks.Clear();
        }

        foreach (var line in e.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            document.Blocks.Add(new Paragraph(new Run(line)));
        }

        box.CaretPosition = document.ContentEnd;
        saveTimer.Start();
        FlushTexts();
    }

    // ------------------------------------------------------------------ Menüs

    private void OnQuickTexts(object sender, RoutedEventArgs e)
    {
        if (viewModel is not { SelectedCall: not null } vm)
        {
            return;
        }

        FlushTexts();
        var quickTexts = vm.LoadQuickTexts();
        ShowMenu((FrameworkElement)sender, quickTexts.Select(q => (q.Name, (Action)(() => vm.ApplyQuickText(q)))), "Keine Cobra-Schnelltexte vorhanden");
    }

    private void OnReasonTemplates(object sender, RoutedEventArgs e) => ShowTemplates(sender, TemplateKind.CallReason, reason: true);

    private void OnSolutionTemplates(object sender, RoutedEventArgs e) => ShowTemplates(sender, TemplateKind.CallSolution, reason: false);

    private void ShowTemplates(object sender, string kind, bool reason)
    {
        if (viewModel is not { SelectedCall: not null } vm)
        {
            return;
        }

        var items = vm.LoadTemplates(kind).Select(t => (t.Name, (Action)(() => vm.ApplyTextTemplate(t, reason))));
        ShowMenu((FrameworkElement)sender, items, "Keine Vorlagen vorhanden");
    }

    private static void ShowMenu(FrameworkElement target, IEnumerable<(string Name, Action Run)> items, string emptyText)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (name, run) in items)
        {
            var item = new MenuItem { Header = new TextBlock { Text = name } };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = emptyText, IsEnabled = false });
        }

        menu.IsOpen = true;
    }

    private void OnTrashRequested(object? sender, EventArgs e)
    {
        if (viewModel is { } vm)
        {
            FlushTexts();
            CallTrashWindow.ShowFor(Window.GetWindow(this), vm);
        }
    }
}
