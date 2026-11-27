using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PixFix.Infrastructure.Data;

/// <summary>Zugriff auf <c>pixfix.db</c>: Verbindungen öffnen und Schema migrieren.</summary>
public sealed class PixFixDatabase
{
    private readonly string connectionString;

    public PixFixDatabase(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        FilePath = paths.DatabaseFile;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }

    public string FilePath { get; }

    public static int LatestSchemaVersion => Schema.Migrations.Length;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        return connection;
    }

    /// <summary>Bringt das Schema auf den neuesten Stand. Jede Migration läuft in einer eigenen Transaktion.</summary>
    public void Migrate()
    {
        using var connection = Open();
        var current = GetSchemaVersion(connection);
        if (current > LatestSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Die Datenbank hat Schemaversion {current}, dieses Programm kennt nur bis {LatestSchemaVersion}. " +
                "Bitte eine neuere PixFix-Version verwenden.");
        }

        for (var version = current; version < LatestSchemaVersion; version++)
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(Schema.Migrations[version], transaction);
            connection.Execute(
                "PRAGMA user_version = " + (version + 1).ToString(CultureInfo.InvariantCulture) + ";",
                transaction);
            transaction.Commit();
        }
    }

    public static int GetSchemaVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return Convert.ToInt32(connection.Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture);
    }

    /// <summary>True, wenn noch keine Nutzdaten vorhanden sind.</summary>
    public static bool HasNoUserData(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        foreach (var table in Schema.UserDataTables)
        {
            if (connection.Count(table, transaction) > 0)
            {
                return false;
            }
        }

        return true;
    }
}
