using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using PixFix.Core.Common;

namespace PixFix.App.Editing;

/// <summary>Dokument für den Editor.</summary>
/// <param name="EntryId">Eintrag, zu dem gespeichert wird; <c>null</c> = nur anzeigen (z. B. Fehlermeldung).</param>
/// <param name="Format"><c>xaml</c>, <c>rtf</c> oder <c>text</c>.</param>
/// <param name="Text">Inhalt im jeweiligen Format.</param>
/// <param name="PictureAssetIds">Bei RTF: Asset-Id je Bild in Dokumentreihenfolge (siehe KnowledgeDocument).</param>
public sealed record EditorDocument(string? EntryId, string Format, string Text, IReadOnlyList<string?> PictureAssetIds);

/// <summary>
/// Was der <see cref="DocumentEditor"/> vom umgebenden ViewModel braucht (Wissensdatenbank, Schreibbereiche).
/// Der Editor beobachtet die Eigenschaften <c>Document</c>, <c>Query</c> und <c>PendingImageAssetId</c>.
/// </summary>
public interface IDocumentEditorHost : INotifyPropertyChanged
{
    EditorDocument? Document { get; }

    bool IsLoadingDocument { get; }

    bool CanEdit { get; }

    bool IsReadOnly { get; }

    /// <summary>Suchbegriff, dessen Fundstellen im Dokument markiert werden.</summary>
    string Query { get; }

    /// <summary>Bild, zu dem nach dem Laden gesprungen wird; der Editor setzt es danach auf <c>null</c>.</summary>
    string? PendingImageAssetId { get; set; }

    string Status { get; set; }

    ObservableCollection<CopyAction> CopyActions { get; }

    ICommand CopyCommand { get; }

    ICommand DeleteCopyActionCommand { get; }

    string StoreImage(byte[] png);

    string? GetAssetFile(string assetId);

    void SaveContent(string entryId, SavedDocument saved);

    void AddCopyAction(string text, int occurrence);
}
