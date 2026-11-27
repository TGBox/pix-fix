using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PixFix.App.Editing;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Workspaces;
using PixFix.App.Common;

namespace PixFix.App.Workspaces;

/// <summary>Ansicht der Schreibbereiche; der Code-Behind öffnet nur die Bereichsverwaltung.</summary>
public partial class WorkspacesView : UserControl, IShortcutTarget
{
    private WorkspacesViewModel? viewModel;

    public WorkspacesView()
    {
        InitializeComponent();
        Loaded += (_, _) => viewModel?.EnsureLoaded();
        DataContextChanged += (_, e) =>
        {
            if (viewModel is not null)
            {
                viewModel.ManageWorkspacesRequested -= OnManageWorkspaces;
                viewModel.TrashRequested -= OnTrashRequested;
            }

            viewModel = e.NewValue as WorkspacesViewModel;
            if (viewModel is not null)
            {
                viewModel.ManageWorkspacesRequested += OnManageWorkspaces;
                viewModel.TrashRequested += OnTrashRequested;
            }
        };
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void CreateNew()
    {
        EditorPanel.FlushSave();
        viewModel?.NewEntryCommand.Execute(null);
    }

    private void OnAttachmentDragOver(object sender, DragEventArgs e)
    {
        e.Effects = viewModel?.AddAttachmentCommand.CanExecute(null) == true && DroppedPdfs(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnAttachmentDrop(object sender, DragEventArgs e)
    {
        if (viewModel?.AddAttachmentCommand.CanExecute(null) == true && DroppedPdfs(e) is { Count: > 0 } files)
        {
            viewModel.AddAttachments(files);
        }
    }

    private static List<string> DroppedPdfs(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => string.Equals(System.IO.Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

    /// <summary>„Reiter per Mail“: Empfänger wählen, Outlook-Entwurf mit Inhalt (HTML, Bilder) und PDF-Anhängen.</summary>
    private void OnSendMail(object sender, RoutedEventArgs e)
    {
        if (viewModel is not { SelectedItem: { } item } vm)
        {
            return;
        }

        EditorPanel.FlushSave();
        var owner = Window.GetWindow(this);
        var recipients = vm.MailRecipients(out var preselect);
        var to = PixFix.App.Calls.ChoiceWindow.Pick(
            owner,
            "PixFix – Reiter per Mail",
            "An wen soll der Reiter gehen? Angeboten werden die E-Mail-Adressen des gerade geöffneten Anrufs.",
            recipients,
            preselect);
        if (to is null)
        {
            return;
        }

        var subject = string.IsNullOrWhiteSpace(item.Title) ? "PixFix-Reiter" : item.Title.Trim();
        var pdfs = CopyAttachmentsWithNames(vm.Attachments);
        var summary = pdfs.Count == 0 ? null : "PDF-Anlagen:\n" + string.Join("\n", vm.Attachments.Select(a => "• " + a.DisplayName));
        try
        {
            if (vm.UseOutlook)
            {
                var (html, images) = FlowDocumentHtml.Convert(
                    EditorPanel.CurrentDocument,
                    image => FlowDocumentStore.AssetId(image) is { } assetId ? vm.GetAssetFile(assetId) : null,
                    summary);
                MailComposer.OpenOutlookHtmlDraft(to, subject, html, images.Select(i => new MailComposer.InlineImage(i.ContentId, i.File)).ToList(), pdfs);
                vm.Status = $"E-Mail-Entwurf für „{subject}“ wurde geöffnet.";
            }
            else
            {
                var document = EditorPanel.CurrentDocument;
                var text = new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd() + (summary is null ? string.Empty : "\n\n" + summary);
                MailComposer.OpenDraft(to, subject, text, useOutlook: false);
                vm.Status = "Standard-Mailprogramm: nur der Text wurde übergeben – Bilder und PDFs bitte von Hand anhängen (oder unter Einstellungen → Anrufe Outlook wählen).";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException
            or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner!, "Der E-Mail-Entwurf konnte nicht erstellt werden.\n\n" + ex.Message, "PixFix – Reiter per Mail", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>PDF-Anhänge unter ihrem Anzeigenamen in einen Temp-Ordner kopieren (in der Ablage heißen sie nach ihrer Prüfsumme).</summary>
    private static List<string> CopyAttachmentsWithNames(IEnumerable<InformationAttachmentFile> attachments)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pixfix-mail", Guid.NewGuid().ToString("N"));
        var result = new List<string>();
        foreach (var attachment in attachments.Where(a => File.Exists(a.File)))
        {
            Directory.CreateDirectory(folder);
            var name = string.Concat(attachment.DisplayName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Anhang.pdf";
            }

            var target = Path.Combine(folder, name);
            for (var n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            }

            File.Copy(attachment.File, target);
            result.Add(target);
        }

        return result;
    }

    private void OnTrashRequested(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not { } vm)
        {
            return;
        }

        TrashListWindow.ShowFor(
            Window.GetWindow(this),
            "PixFix – Papierkorb (Reiter)",
            "Gelöschte Reiter der Schreibbereiche. Wiederhergestellte Reiter kommen in ihren Bereich und Abschnitt zurück (ans Ende).",
            ("Reiter", "Bereich / Abschnitt"),
            () => vm.LoadTrash()
                .Select(t => new TrashRow(
                    t.TrashId,
                    t.DeletedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                    t.Title,
                    t.Workspace + " / " + t.Section))
                .ToList(),
            vm.RestoreFromTrash,
            vm.DeleteFromTrash);
    }

    private void OnManageWorkspaces(object? sender, EventArgs e)
    {
        EditorPanel.FlushSave();
        if (viewModel is not null)
        {
            new WorkspacesWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
