using PixFix.Core.Calls;
using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Calls;
using PixFix.Infrastructure.Communication;
using PixFix.Infrastructure.Data;
using PixFix.Infrastructure.Legacy;
using PixFix.Infrastructure.Settings;

namespace PixFix.Infrastructure.Tests;

public sealed class CallRepositoryTests : IDisposable
{
    private readonly LegacyFixture fixture = new();
    private readonly PixFixDatabase database;
    private readonly AssetStore assets;
    private readonly CallRepository calls;

    public CallRepositoryTests()
    {
        var paths = new AppPaths(fixture.TargetDirectory);
        paths.EnsureCreated();
        database = new PixFixDatabase(paths);
        assets = new AssetStore(paths);
        fixture.WithDefaultData();
        Assert.True(new LegacyImporter(database, assets, TimeZoneInfo.Utc).Import(fixture.ProgramDirectory).Succeeded);
        calls = new CallRepository(database);
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void LoadsImportedCallsNewestFirst()
    {
        var all = calls.LoadAll();

        Assert.Equal(new[] { "c2", "c1" }, all.Select(c => c.Id));
        var c1 = all[1];
        Assert.Equal(CallStatus.Done, c1.Status);
        Assert.Equal("1888", c1.CustomerId);
        Assert.True(c1.IsBillable);
        Assert.Equal(TimeSpan.FromMinutes(7), CallRules.BillingDuration(c1));
        Assert.True(all[0].ReminderActive);
    }

    [Fact]
    public void SaveRoundTripsAllFields()
    {
        var call = calls.Create() with
        {
            CustomerNumber = "00099",
            Notes = "NE,",
            ReasonText = "Frage",
            ReasonRtf = "{\\rtf1 Frage}",
            Status = CallStatus.CallBackTomorrow,
            ReminderActive = true,
            ReminderUtc = new DateTimeOffset(2026, 11, 28, 8, 0, 0, TimeSpan.Zero),
            CallbackTarget = "Mobil",
        };

        calls.Save(call);

        var loaded = calls.LoadAll().Single(c => c.Id == call.Id);
        Assert.Equal(call.CustomerNumber, loaded.CustomerNumber);
        Assert.Equal(call.Notes, loaded.Notes);
        Assert.Equal(call.ReasonRtf, loaded.ReasonRtf);
        Assert.Equal(CallStatus.CallBackTomorrow, loaded.Status);
        Assert.Equal(call.ReminderUtc, loaded.ReminderUtc);
        Assert.Equal("Mobil", loaded.CallbackTarget);
    }

    [Fact]
    public void TrashAndRestore()
    {
        calls.MoveToTrash("c1");

        Assert.DoesNotContain(calls.LoadAll(), c => c.Id == "c1");
        var item = Assert.Single(calls.LoadTrash(), t => t.CustomerNumber == "00074");

        var restored = calls.Restore(item.TrashId);

        Assert.Equal("c1", restored.Id);
        Assert.Contains(calls.LoadAll(), c => c.Id == "c1" && c.ReasonText == "Frage KIM");
    }

    [Fact]
    public void LegacyDeletedCallCanBeRestored()
    {
        var legacy = Assert.Single(calls.LoadTrash(), t => t.CustomerNumber == "00001");

        var restored = calls.Restore(legacy.TrashId);

        Assert.Equal(CallStatus.Done, restored.Status);
        Assert.False(restored.ReminderActive);
        Assert.Contains(calls.LoadAll(), c => c.Id == restored.Id);
    }

    [Fact]
    public void CobraQuickTextsSettingsAndFaxDocumentsAreAvailable()
    {
        var quick = Assert.Single(calls.LoadCobraQuickTexts());
        Assert.Equal("NE,", quick.NotesText);
        Assert.True(quick.ScheduleRecall);

        var settings = new SettingsRepository(database);
        Assert.Equal("Outlook Classic", settings.Get(SettingKeys.MailProgram));
        settings.Set(SettingKeys.DialMethod, SettingKeys.DialMethodTapi);
        Assert.Equal(SettingKeys.DialMethodTapi, settings.Get(SettingKeys.DialMethod));
        Assert.Equal("fehlt", settings.Get("gibt.es.nicht", "fehlt"));

        var fax = new FaxDocumentRepository(database, assets);
        var document = Assert.Single(fax.LoadAll());
        Assert.Equal("Fax nicht erreicht", document.Name);
        Assert.True(File.Exists(document.File));
        fax.Remove(document.Id);
        Assert.Empty(fax.LoadAll());
    }
}
