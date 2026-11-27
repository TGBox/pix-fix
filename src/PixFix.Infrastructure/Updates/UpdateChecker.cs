using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PixFix.Core.Updates;

namespace PixFix.Infrastructure.Updates;

/// <summary>Neueste veröffentlichte Version auf GitHub mit Link zur Release-Seite und direktem Download der EXE.</summary>
public sealed record UpdateInfo(Version Version, string Tag, Uri ReleasePage, Uri? Download, string Notes, DateTimeOffset? PublishedAt)
{
    public string VersionText => AppVersion.Format(Version);

    /// <summary>Direkter Download der <c>PixFix.exe</c>, sonst die Release-Seite.</summary>
    public Uri DownloadOrReleasePage => Download ?? ReleasePage;
}

/// <summary>Ergebnis einer Prüfung: <see cref="Latest"/> oder eine verständliche <see cref="Error"/>-Meldung.</summary>
public sealed record UpdateCheckResult(UpdateInfo? Latest, string? Error)
{
    public static UpdateCheckResult Failed(string message) => new(null, message);
}

/// <summary>
/// Fragt das neueste GitHub-Release von PixFix ab (<c>releases/latest</c>; Entwürfe und Vorabversionen liefert GitHub dort
/// nicht). <see cref="CheckAsync"/> wirft nie – Netz-, HTTP- und JSON-Fehler werden zu einer Meldung, damit die Oberfläche
/// unberührt bleibt (wie beim TI-Monitor).
/// </summary>
public sealed class UpdateChecker(HttpClient http)
{
    public const string Repository = "TGBox/pix-fix";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    public const string AssetName = "PixFix.exe";

    public static HttpClient CreateHttpClient(string currentVersion)
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        // GitHub verlangt einen User-Agent.
        var agentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0" : currentVersion;
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PixFix", agentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(LatestReleaseApi), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.Failed("Es ist noch keine Version veröffentlicht.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return UpdateCheckResult.Failed("GitHub hat zu viele Anfragen gemeldet – später erneut versuchen.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed("HTTP-Status " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseRelease(json) is { } latest
                ? new UpdateCheckResult(latest, null)
                : UpdateCheckResult.Failed("Die Antwort von GitHub war nicht lesbar.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return UpdateCheckResult.Failed(ex is TaskCanceledException ? "Zeitüberschreitung" : ex.Message);
        }
    }

    /// <summary>
    /// Liest ein GitHub-Release (<c>tag_name</c>, <c>html_url</c>, <c>body</c>, <c>published_at</c>, <c>assets[].name</c>,
    /// <c>assets[].browser_download_url</c>). Entwürfe, Vorabversionen, unlesbare Tags und Links, die nicht per HTTPS auf
    /// github.com zeigen, ergeben <c>null</c> bzw. werden verworfen.
    /// </summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || IsTrue(root, "draft")
                || IsTrue(root, "prerelease")
                || !AppVersion.TryParse(GetString(root, "tag_name"), out var version))
            {
                return null;
            }

            var releasePage = ToGitHubUri(GetString(root, "html_url")) ?? new Uri(ReleasesPage);
            Uri? download = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (string.Equals(GetString(asset, "name"), AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        download = ToGitHubUri(GetString(asset, "browser_download_url"));
                        break;
                    }
                }
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(
                GetString(root, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : null;

            var tag = GetString(root, "tag_name")!.Trim();
            var notes = GetString(root, "body")?.Trim() ?? string.Empty;
            return new UpdateInfo(version, tag, releasePage, download, notes, published);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Nur HTTPS-Links auf github.com (bzw. Unterdomänen) werden geöffnet.</summary>
    private static Uri? ToGitHubUri(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }
}
