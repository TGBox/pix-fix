using System.Net;
using System.Text;
using PixFix.Infrastructure.Updates;

namespace PixFix.Infrastructure.Tests;

/// <summary>Auswertung von GitHub-Releases und Fehlerverhalten der Update-Prüfung (ohne echtes Netz).</summary>
public sealed class UpdateCheckerTests
{
    private const string Release = """
        {
          "tag_name": "v1.4.2",
          "html_url": "https://github.com/TGBox/pix-fix/releases/tag/v1.4.2",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-11-27T08:00:00Z",
          "body": "## Neu\n- Hilfe und Einführungstour",
          "assets": [
            { "name": "PixFix.exe.sha256", "browser_download_url": "https://github.com/TGBox/pix-fix/releases/download/v1.4.2/PixFix.exe.sha256" },
            { "name": "PixFix.exe", "browser_download_url": "https://github.com/TGBox/pix-fix/releases/download/v1.4.2/PixFix.exe" }
          ]
        }
        """;

    [Fact]
    public void ParseReleaseReadsVersionPagesAndExeDownload()
    {
        var info = UpdateChecker.ParseRelease(Release);

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 4, 2), info.Version);
        Assert.Equal("1.4.2", info.VersionText);
        Assert.Equal("v1.4.2", info.Tag);
        Assert.Equal("https://github.com/TGBox/pix-fix/releases/tag/v1.4.2", info.ReleasePage.AbsoluteUri);
        Assert.Equal("https://github.com/TGBox/pix-fix/releases/download/v1.4.2/PixFix.exe", info.Download!.AbsoluteUri);
        Assert.Equal(info.Download, info.DownloadOrReleasePage);
        Assert.StartsWith("## Neu", info.Notes, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 8, 0, 0, TimeSpan.Zero), info.PublishedAt);
    }

    [Fact]
    public void ParseReleaseWithoutExeFallsBackToReleasePage()
    {
        var info = UpdateChecker.ParseRelease("""
            {"tag_name":"v2.0.0","html_url":"https://github.com/TGBox/pix-fix/releases/tag/v2.0.0","assets":[]}
            """);

        Assert.NotNull(info);
        Assert.Null(info.Download);
        Assert.Equal(info.ReleasePage, info.DownloadOrReleasePage);
    }

    [Fact]
    public void ParseReleaseIgnoresLinksOutsideGitHub()
    {
        var info = UpdateChecker.ParseRelease("""
            {"tag_name":"v2.0.0","html_url":"http://github.com/TGBox/pix-fix/releases/tag/v2.0.0",
             "assets":[{"name":"PixFix.exe","browser_download_url":"https://downloads.invalid/PixFix.exe"}]}
            """);

        Assert.NotNull(info);
        Assert.Null(info.Download);
        Assert.Equal(UpdateChecker.ReleasesPage, info.ReleasePage.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("""{"tag_name":"v2.0.0","draft":true}""")]
    [InlineData("""{"tag_name":"v2.0.0-beta.1","prerelease":true}""")]
    [InlineData("""{"tag_name":"nightly"}""")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""[]""")]
    [InlineData("kein json")]
    public void UnusableAnswersGiveNull(string json) =>
        Assert.Null(UpdateChecker.ParseRelease(json));

    [Fact]
    public async Task CheckAsyncReturnsLatestRelease()
    {
        using var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, Release)));

        var result = await new UpdateChecker(http).CheckAsync();

        Assert.Null(result.Error);
        Assert.Equal(new Version(1, 4, 2), result.Latest?.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "noch keine Version")]
    [InlineData(HttpStatusCode.Forbidden, "zu viele Anfragen")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP-Status 500")]
    public async Task CheckAsyncTurnsHttpErrorsIntoMessages(HttpStatusCode status, string expected)
    {
        using var http = new HttpClient(new StubHandler(_ => Json(status, "{}")));

        var result = await new UpdateChecker(http).CheckAsync();

        Assert.Null(result.Latest);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsyncNeverThrowsOnNetworkErrors()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("Netz weg")));

        var result = await new UpdateChecker(http).CheckAsync();

        Assert.Null(result.Latest);
        Assert.Equal("Netz weg", result.Error);
    }

    [Fact]
    public async Task CheckAsyncAsksTheGitHubReleaseApi()
    {
        Uri? requested = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            requested = request.RequestUri;
            return Json(HttpStatusCode.OK, Release);
        }));

        await new UpdateChecker(http).CheckAsync();

        Assert.Equal(UpdateChecker.LatestReleaseApi, requested?.AbsoluteUri);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
