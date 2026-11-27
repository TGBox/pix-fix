using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Settings;

namespace PixFix.Infrastructure.Tests;

public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pixfix-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ThemeDefaultsToLightWhenUnset()
    {
        var repository = CreateRepository();

        Assert.Equal(SettingKeys.ThemeLight, repository.Get(SettingKeys.Theme, SettingKeys.ThemeLight));
    }

    [Theory]
    [InlineData(SettingKeys.ThemeLight)]
    [InlineData(SettingKeys.ThemeDark)]
    [InlineData(SettingKeys.ThemeSystem)]
    public void ThemeRoundTripsThroughDatabase(string theme)
    {
        var repository = CreateRepository();

        repository.Set(SettingKeys.Theme, theme);

        Assert.Equal(theme, repository.Get(SettingKeys.Theme, SettingKeys.ThemeLight));
    }

    [Fact]
    public void SidebarCollapsedDefaultsToFalseAndRoundTrips()
    {
        var repository = CreateRepository();

        Assert.False(repository.GetFlag(SettingKeys.SidebarCollapsed, false));

        repository.SetFlag(SettingKeys.SidebarCollapsed, true);
        Assert.True(repository.GetFlag(SettingKeys.SidebarCollapsed, false));

        repository.SetFlag(SettingKeys.SidebarCollapsed, false);
        Assert.False(repository.GetFlag(SettingKeys.SidebarCollapsed, false));
    }

    private SettingsRepository CreateRepository()
    {
        var paths = new AppPaths(directory);
        paths.EnsureCreated();
        var database = new PixFixDatabase(paths);
        database.Migrate();
        return new SettingsRepository(database);
    }
}
