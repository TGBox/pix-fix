using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pixfix-db-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MigrateCreatesLatestSchemaAndIsIdempotent()
    {
        var paths = new AppPaths(directory);
        paths.EnsureCreated();
        var database = new PixFixDatabase(paths);

        database.Migrate();
        database.Migrate();

        using var db = database.Open();
        Assert.Equal(PixFixDatabase.LatestSchemaVersion, PixFixDatabase.GetSchemaVersion(db));
        Assert.True(PixFixDatabase.HasNoUserData(db));
    }

    [Fact]
    public void ForeignKeysAreEnforced()
    {
        var paths = new AppPaths(directory);
        paths.EnsureCreated();
        var database = new PixFixDatabase(paths);
        database.Migrate();

        using var db = database.Open();

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => db.Execute(
            "INSERT INTO knowledge_entries(id, section_id, title, content_format, sort_order, created_utc, updated_utc) " +
            "VALUES('x', 'fehlt', 't', 'xaml', 0, '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');"));
    }
}
