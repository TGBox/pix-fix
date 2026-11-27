namespace PixFix.Core.Templates;

/// <summary>Arten von Vorlagen in der Tabelle <c>templates</c> (SPEC F6).</summary>
public static class TemplateKind
{
    public const string Email = "email";
    public const string Fax = "fax";
    public const string Snippet = "snippet";

    /// <summary>Textbaustein, der als Code (Monospace, ohne Formatierung) eingefügt wird.</summary>
    public const string CodeSnippet = "snippet-code";

    public const string Entry = "entry";
    public const string CallReason = "call-reason";
    public const string CallSolution = "call-solution";
}
