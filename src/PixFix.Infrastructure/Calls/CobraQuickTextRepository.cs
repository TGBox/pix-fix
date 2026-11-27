using PixFix.Core.Calls;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Calls;

/// <summary>Cobra-Schnelltexte (Tabelle <c>cobra_quick_texts</c>, aus PixFix 69 übernommen und hier gepflegt).</summary>
public sealed class CobraQuickTextRepository(PixFixDatabase database)
{
    public IReadOnlyList<CobraQuickText> LoadAll()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT * FROM cobra_quick_texts ORDER BY sort_order, id;", null);
        using var reader = command.ExecuteReader();
        var result = new List<CobraQuickText>();
        while (reader.Read())
        {
            result.Add(new CobraQuickText(
                reader.GetInteger("id"),
                reader.GetText("name"),
                reader.GetText("cobra_text"),
                reader.GetText("notes_text"),
                reader.GetText("work_text"),
                reader.GetFlag("complete_call"),
                reader.GetFlag("schedule_recall")));
        }

        return result;
    }

    public CobraQuickText Create(string name)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var order = SortOrder.Next(db, tx, "cobra_quick_texts");
        var id = Convert.ToInt64(
            db.Scalar("INSERT INTO cobra_quick_texts(name, sort_order) VALUES($name, $order) RETURNING id;", tx, ("$name", name), ("$order", order)),
            System.Globalization.CultureInfo.InvariantCulture);
        tx.Commit();
        return new CobraQuickText(id, name, string.Empty, string.Empty, string.Empty, false, false);
    }

    public void Save(CobraQuickText quick)
    {
        ArgumentNullException.ThrowIfNull(quick);
        using var db = database.Open();
        db.Execute(
            """
            UPDATE cobra_quick_texts SET name = $name, cobra_text = $cobra, notes_text = $notes, work_text = $work,
                complete_call = $complete, schedule_recall = $recall
            WHERE id = $id;
            """,
            null,
            ("$name", quick.Name),
            ("$cobra", quick.CobraText),
            ("$notes", quick.NotesText),
            ("$work", quick.WorkText),
            ("$complete", quick.CompleteCall.ToDb()),
            ("$recall", quick.ScheduleRecall.ToDb()),
            ("$id", quick.Id));
    }

    public void Delete(long id)
    {
        using var db = database.Open();
        db.Execute("DELETE FROM cobra_quick_texts WHERE id = $id;", null, ("$id", id));
    }

    public bool Move(long id, int delta)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var ids = SortOrder.Ids(db, tx, "SELECT id FROM cobra_quick_texts ORDER BY sort_order, id;");
        var moved = SortOrder.Move(db, tx, "cobra_quick_texts", ids, id, delta);
        tx.Commit();
        return moved;
    }
}
