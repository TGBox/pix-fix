namespace PixFix.App.Common;

/// <summary>Bereichsansicht, die auf die Tastenkürzel des Hauptfensters reagiert (Strg+F, Strg+N).</summary>
public interface IShortcutTarget
{
    /// <summary>Strg+F: Suchfeld bzw. Liste fokussieren.</summary>
    void FocusSearch();

    /// <summary>Strg+N: neuen Eintrag/Reiter/Anruf anlegen.</summary>
    void CreateNew();
}
