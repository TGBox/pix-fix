using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Templates;

/// <summary>Vorlage aus der Tabelle <c>templates</c>; die Art steht in <see cref="Core.Templates.TemplateKind"/>.</summary>
public sealed record Template(long Id, string Kind, string Name, string Subject, string Body);

/// <summary>Vorlagen aller Arten (SPEC F6): E-Mail, Fax, Schnelltextbausteine, Eintragsvorlagen, Anrufgründe, Lösungstexte.</summary>
public sealed class TemplateRepository(PixFixDatabase database)
{
    public IReadOnlyList<Template> LoadByKind(string kind)
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT id, kind, name, subject, body FROM templates WHERE kind = $kind ORDER BY sort_order, id;", null, ("$kind", kind));
        using var reader = command.ExecuteReader();
        var result = new List<Template>();
        while (reader.Read())
        {
            result.Add(new Template(
                reader.GetInteger("id"), reader.GetText("kind"), reader.GetText("name"), reader.GetText("subject"), reader.GetText("body")));
        }

        return result;
    }

    /// <summary>Neue, leere Vorlage am Ende der Liste ihrer Art.</summary>
    public Template Create(string kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var order = SortOrder.Next(db, tx, "templates", "kind = $kind", ("$kind", kind));
        var id = Convert.ToInt64(
            db.Scalar(
                "INSERT INTO templates(kind, name, subject, body, sort_order) VALUES($kind, $name, '', '', $order) RETURNING id;",
                tx,
                ("$kind", kind),
                ("$name", name),
                ("$order", order)),
            System.Globalization.CultureInfo.InvariantCulture);
        tx.Commit();
        return new Template(id, kind, name, string.Empty, string.Empty);
    }

    public void Save(Template template)
    {
        ArgumentNullException.ThrowIfNull(template);
        using var db = database.Open();
        db.Execute(
            "UPDATE templates SET name = $name, subject = $subject, body = $body WHERE id = $id;",
            null,
            ("$name", template.Name),
            ("$subject", template.Subject),
            ("$body", template.Body),
            ("$id", template.Id));
    }

    public void Delete(long id)
    {
        using var db = database.Open();
        db.Execute("DELETE FROM templates WHERE id = $id;", null, ("$id", id));
    }

    /// <summary>Innerhalb der eigenen Art um <paramref name="delta"/> Positionen verschieben (−1 = nach oben).</summary>
    public bool Move(long id, int delta)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var kind = db.Scalar("SELECT kind FROM templates WHERE id = $id;", tx, ("$id", id)) as string;
        if (kind is null)
        {
            return false;
        }

        var ids = SortOrder.Ids(db, tx, "SELECT id FROM templates WHERE kind = $kind ORDER BY sort_order, id;", ("$kind", kind));
        var moved = SortOrder.Move(db, tx, "templates", ids, id, delta);
        tx.Commit();
        return moved;
    }
}
