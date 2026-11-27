using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PixFix.Infrastructure.Ti;

/// <summary>Zustand einer Quelle des TI-Monitors (grün/rot/orange).</summary>
public enum TiState
{
    Unknown,
    Ok,
    Incident,
}

/// <summary>Ergebnis einer Quelle: Kurztext, Einzelmeldungen und Schlüssel (zum Erkennen neuer Störungen).</summary>
public sealed record TiCheck(TiState State, string Text, IReadOnlyList<string> Details, IReadOnlyList<string> Keys)
{
    public static TiCheck Failed(string message) => new(TiState.Unknown, "Abfrage fehlgeschlagen: " + message, [], []);
}

/// <summary>Beide Quellen zusammen (gematik TI-Status und TI-Lagebild).</summary>
public sealed record TiResult(TiCheck Incidents, TiCheck Lage, DateTime CheckedLocal)
{
    public bool HasIncident => Incidents.State == TiState.Incident || Lage.State == TiState.Incident;

    public bool IsIncomplete => Incidents.State == TiState.Unknown || Lage.State == TiState.Unknown;

    public TiState Overall => HasIncident ? TiState.Incident : IsIncomplete ? TiState.Unknown : TiState.Ok;

    public string Headline => HasIncident ? "STÖRUNG IN DER TI" : IsIncomplete ? "PRÜFUNG NICHT VOLLSTÄNDIG" : "TI OHNE AKTIVE STÖRUNG";

    public IEnumerable<string> Keys => Incidents.Keys.Concat(Lage.Keys);
}

/// <summary>
/// TI-Monitor (SPEC F9) mit denselben gematik-Quellen und Regeln wie PixFix 69. Jeder Fehler (Netz, HTTP, JSON) wird zu
/// einem Ergebnis „Abfrage fehlgeschlagen“ – <see cref="CheckAsync"/> wirft nie, damit die Oberfläche unberührt bleibt.
/// </summary>
public sealed class TiMonitorClient(HttpClient http)
{
    public const string StatusPage = "https://fachportal.gematik.de/ti-status";
    public const string LagePage = "https://ti-lage.prod.ccs.gematik.solutions/d/oGvaba47k/monitoring-lagebild-der-ti?orgId=1&kiosk=&from=now-8m&to=now&timezone=browser";
    public const string IncidentApi = "https://ti-lage.prod.ccs.gematik.solutions/lageapi/v1/tistatus/incident";
    public const string LageApi = "https://ti-lage.prod.ccs.gematik.solutions/lageapi/v2/tilage";

    private static readonly Dictionary<string, string> AppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["erezept"] = "E-Rezept",
        ["epa"] = "ePA",
        ["kim"] = "KIM",
        ["wanda"] = "WANDA",
        ["ogd"] = "OGD",
        ["vsdm"] = "VSDM",
        ["tianschluss"] = "TI-Anschluss",
    };

    /// <summary>HttpClient mit Zeitlimit und Kennung wie PixFix 69 (15 s, eigener User-Agent).</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PixFix-TI-Monitor", "2.0"));
        return client;
    }

    public async Task<TiResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var incidents = Fetch(IncidentApi, ParseIncidents, cancellationToken);
        var lage = Fetch(LageApi, ParseLage, cancellationToken);
        return new TiResult(await incidents.ConfigureAwait(false), await lage.ConfigureAwait(false), DateTime.Now);
    }

    private async Task<TiCheck> Fetch(string url, Func<string, TiCheck> parse, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return TiCheck.Failed("HTTP-Status " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            return parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return TiCheck.Failed(ex is TaskCanceledException ? "Zeitüberschreitung" : ex.Message);
        }
    }

    /// <summary>
    /// TI-Status: <c>{"success":true,"data":[{"id":…,"title":…,"status":1|4,"closedAt":null}]}</c>. Aktiv sind offene Meldungen
    /// (ohne <c>closedAt</c>) mit Status 1 oder 4.
    /// </summary>
    public static TiCheck ParseIncidents(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new TiCheck(TiState.Unknown, "Ungültige Antwort", [], []);
            }

            var details = new List<string>();
            var keys = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var code = item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.TryGetDouble(out var number)
                    ? (int)number
                    : 0;
                var open = !item.TryGetProperty("closedAt", out var closed) || closed.ValueKind == JsonValueKind.Null;
                if (open && code is 1 or 4)
                {
                    details.Add(item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                        ? title.GetString()!
                        : "Aktive Einschränkung");
                    keys.Add("incident:" + (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                        ? ((long)id.GetDouble()).ToString(CultureInfo.InvariantCulture)
                        : (keys.Count + 1).ToString(CultureInfo.InvariantCulture)));
                }
            }

            return details.Count == 0
                ? new TiCheck(TiState.Ok, "Keine aktive Einschränkung", [], [])
                : new TiCheck(TiState.Incident, $"{details.Count} aktive Einschränkung(en): {string.Join(", ", details.Take(2))}", details, keys);
        }
        catch (JsonException ex)
        {
            return TiCheck.Failed(ex.Message);
        }
    }

    /// <summary>TI-Lagebild: <c>{"appStatus":{"kim":{"outage":"none"|…}}}</c>. Betroffen ist jede Anwendung mit outage ≠ none.</summary>
    public static TiCheck ParseLage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("appStatus", out var apps) || apps.ValueKind != JsonValueKind.Object)
            {
                return new TiCheck(TiState.Unknown, "Ungültige Antwort", [], []);
            }

            var details = new List<string>();
            var keys = new List<string>();
            foreach (var app in apps.EnumerateObject())
            {
                if (app.Value.ValueKind == JsonValueKind.Object
                    && app.Value.TryGetProperty("outage", out var outage)
                    && outage.ValueKind == JsonValueKind.String
                    && !string.Equals(outage.GetString(), "none", StringComparison.OrdinalIgnoreCase))
                {
                    var name = AppNames.TryGetValue(app.Name, out var display) ? display : app.Name;
                    details.Add($"{name} ({outage.GetString()})");
                    keys.Add($"lage:{app.Name}:{outage.GetString()}");
                }
            }

            return details.Count == 0
                ? new TiCheck(TiState.Ok, "Alle Anwendungen verfügbar", [], [])
                : new TiCheck(TiState.Incident, "Betroffen: " + string.Join(", ", details), details, keys);
        }
        catch (JsonException ex)
        {
            return TiCheck.Failed(ex.Message);
        }
    }
}
