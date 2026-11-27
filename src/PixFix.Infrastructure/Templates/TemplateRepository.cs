using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Templates;

/// <summary>Vorlage aus der Tabelle <c>templates</c>; die Art steht in <see cref="Core.Templates.TemplateKind"/>.</summary>
public sealed record Template(long Id, string Kind, string Name, string Subject, string Body);

/// <summary>Lesender Zugriff auf Vorlagen (Pflege der Vorlagen folgt mit SPEC F6).</summary>
public sealed class TemplateRepository(PixFixDatabase database)
{
    public IReadOnlyList<Template> LoadByKind(string kind)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT id, kind, name, subject, body FROM templates WHERE kind = $kind ORDER BY sort_order, name;", null, ("$kind", kind));
        using var reader = command.ExecuteReader();
        var result = new List<Template>();
        while (reader.Read())
        {
            result.Add(new Template(
                reader.GetInteger("id"), reader.GetText("kind"), reader.GetText("name"), reader.GetText("subject"), reader.GetText("body")));
        }

        return result;
    }
}
