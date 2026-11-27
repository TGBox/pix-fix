using PixFix.Infrastructure.Remote;
using PixFix.Infrastructure.Ti;

namespace PixFix.Infrastructure.Tests;

/// <summary>Auswertung der gematik-Antworten (Regeln aus PixFix 69) und Suche der AnyDesk-Verknüpfung.</summary>
public sealed class TiMonitorTests
{
    [Fact]
    public void OpenIncidentsWithStatus1Or4AreActive()
    {
        var check = TiMonitorClient.ParseIncidents("""
            {"success":true,"data":[
              {"id":11,"title":"KIM gestört","status":1,"closedAt":null},
              {"id":12,"title":"E-Rezept langsam","status":4},
              {"id":13,"title":"Behoben","status":1,"closedAt":"2026-11-27T08:00:00Z"},
              {"id":14,"title":"Nur Info","status":2,"closedAt":null}
            ]}
            """);

        Assert.Equal(TiState.Incident, check.State);
        Assert.Equal(new[] { "KIM gestört", "E-Rezept langsam" }, check.Details);
        Assert.Equal(new[] { "incident:11", "incident:12" }, check.Keys);
        Assert.StartsWith("2 aktive Einschränkung(en)", check.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOpenIncidentsIsOk()
    {
        var check = TiMonitorClient.ParseIncidents("""{"success":true,"data":[]}""");

        Assert.Equal(TiState.Ok, check.State);
        Assert.Equal("Keine aktive Einschränkung", check.Text);
    }

    [Theory]
    [InlineData("""{"success":false,"data":[]}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""[]""")]
    [InlineData("kein json")]
    public void InvalidIncidentAnswersAreUnknown(string json) =>
        Assert.Equal(TiState.Unknown, TiMonitorClient.ParseIncidents(json).State);

    [Fact]
    public void LageListsAffectedApplications()
    {
        var check = TiMonitorClient.ParseLage("""
            {"appStatus":{"kim":{"outage":"partial"},"epa":{"outage":"none"},"tianschluss":{"outage":"full"},"neu":{"outage":"partial"}}}
            """);

        Assert.Equal(TiState.Incident, check.State);
        Assert.Equal(new[] { "KIM (partial)", "TI-Anschluss (full)", "neu (partial)" }, check.Details);
        Assert.Contains("lage:kim:partial", check.Keys);
    }

    [Fact]
    public void LageWithoutOutageIsOkAndInvalidIsUnknown()
    {
        Assert.Equal(TiState.Ok, TiMonitorClient.ParseLage("""{"appStatus":{"kim":{"outage":"none"}}}""").State);
        Assert.Equal(TiState.Unknown, TiMonitorClient.ParseLage("""{"status":"ok"}""").State);
    }

    [Fact]
    public void OverallStateFollowsPixFix69()
    {
        var ok = new TiCheck(TiState.Ok, "ok", [], []);
        var unknown = TiCheck.Failed("Zeitüberschreitung");
        var incident = new TiCheck(TiState.Incident, "x", ["x"], ["k"]);

        Assert.Equal(TiState.Ok, new TiResult(ok, ok, DateTime.Now).Overall);
        Assert.Equal(TiState.Unknown, new TiResult(ok, unknown, DateTime.Now).Overall);
        Assert.Equal(TiState.Incident, new TiResult(incident, unknown, DateTime.Now).Overall);
        Assert.Equal("STÖRUNG IN DER TI", new TiResult(incident, unknown, DateTime.Now).Headline);
    }

    [Fact]
    public void AnyDeskShortcutIsFoundAtUsualPlacesFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "pixfix-anydesk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var deep = Path.Combine(root, "start", "Programs", "Tools", "Fernwartung");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "AnyDesk (alt).lnk"), "x");
            File.WriteAllText(Path.Combine(deep, "AnyDesk.exe"), "x");

            // Nur rekursiv auffindbar.
            Assert.Equal(new[] { Path.Combine(deep, "AnyDesk (alt).lnk") }, RemoteSupportLauncher.AnyDeskShortcutCandidates([Path.Combine(root, "start")]));

            // Direkter Ort hat Vorrang vor der rekursiven Suche.
            var desktop = Path.Combine(root, "desktop");
            Directory.CreateDirectory(desktop);
            File.WriteAllText(Path.Combine(desktop, "AnyDesk.lnk"), "x");
            Assert.Equal(
                new[] { Path.Combine(desktop, "AnyDesk.lnk") },
                RemoteSupportLauncher.AnyDeskShortcutCandidates([desktop, Path.Combine(root, "start")]));

            Assert.False(RemoteSupportLauncher.IsValidAnyDeskShortcut(Path.Combine(deep, "AnyDesk.exe")));
            Assert.False(RemoteSupportLauncher.IsValidAnyDeskShortcut(Path.Combine(root, "fehlt", "AnyDesk.lnk")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
