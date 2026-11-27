using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PixFix.Infrastructure.Data;

/// <summary>Kleine Helfer, damit Datenzugriffe ohne ORM lesbar bleiben. Werte immer als Parameter übergeben.</summary>
public static class SqliteExtensions
{
    public static int Execute(
        this SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Create(connection, sql, transaction, parameters);
        return command.ExecuteNonQuery();
    }

    public static object? Scalar(
        this SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Create(connection, sql, transaction, parameters);
        return command.ExecuteScalar();
    }

    /// <summary>Anzahl Zeilen einer Tabelle. Nur mit festen Tabellennamen aus dem Code aufrufen.</summary>
    public static long Count(this SqliteConnection connection, string table, SqliteTransaction? transaction = null)
    {
        var value = connection.Scalar("SELECT COUNT(*) FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";", transaction);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static SqliteCommand Create(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction,
        params (string Name, object? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static string GetText(this SqliteDataReader reader, string column)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public static long GetInteger(this SqliteDataReader reader, string column)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public static bool GetFlag(this SqliteDataReader reader, string column) => reader.GetInteger(column) != 0;

    public static string? ToDb(this DateTimeOffset? value) => value?.ToDb();

    public static string ToDb(this DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static int ToDb(this bool value) => value ? 1 : 0;
}
