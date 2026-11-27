using Microsoft.Data.Sqlite;

namespace PixFix.Infrastructure.Data;

/// <summary>Reihenfolge per <c>sort_order</c> (Vorlagen, Schnelltexte, Fax-Dokumente): Eintrag um eine Position verschieben.</summary>
internal static class SortOrder
{
    /// <summary>
    /// Verschiebt <paramref name="id"/> innerhalb von <paramref name="orderedIds"/> um <paramref name="delta"/> und schreibt die
    /// Reihenfolge neu (0, 1, 2 …). Liefert <c>false</c>, wenn es nichts zu verschieben gibt (Rand der Liste, unbekannte Id).
    /// <paramref name="table"/> nur mit festen Tabellennamen aus dem Code aufrufen.
    /// </summary>
    public static bool Move(SqliteConnection db, SqliteTransaction tx, string table, IReadOnlyList<long> orderedIds, long id, int delta)
    {
        var list = orderedIds.ToList();
        var index = list.IndexOf(id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return false;
        }

        list.RemoveAt(index);
        list.Insert(target, id);
        var sql = "UPDATE " + table + " SET sort_order = $order WHERE id = $id;";
        for (var i = 0; i < list.Count; i++)
        {
            db.Execute(sql, tx, ("$order", i), ("$id", list[i]));
        }

        return true;
    }

    /// <summary>Nächste freie Position (ans Ende).</summary>
    public static int Next(SqliteConnection db, SqliteTransaction? tx, string table, string? where = null, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt32(
            db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM " + table + (where is null ? string.Empty : " WHERE " + where) + ";", tx, parameters),
            System.Globalization.CultureInfo.InvariantCulture);

    public static List<long> Ids(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = SqliteExtensions.Create(db, sql, tx, parameters);
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}
