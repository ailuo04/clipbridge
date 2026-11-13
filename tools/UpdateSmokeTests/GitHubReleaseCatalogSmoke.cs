using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClipBridge.Windows;

internal static class GitHubReleaseCatalogSmoke
{
    private const string ApiPage1 = "https://api.github.com/repos/ailuo04/clipbridge/releases?per_page=100";
    private const string ApiPage2 = "https://api.github.com/repos/ailuo04/clipbridge/releases?per_page=100&page=2";
    private const string RawRoot = "https://raw.githubusercontent.com/ailuo04/clipbridge/master/";

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL catalog: " + description);
        Console.WriteLine("PASS catalog: " + description);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClipBridge.csproj")) && File.Exists(Path.Combine(directory.FullName, "manifest-0.5.0.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ClipBridge repository fixtures");
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(RepositoryRoot(), name));

    private static HttpResponseMessage JsonResponse(object value, string? link = null, string? finalUrl = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
        if (link != null) response.Headers.TryAddWithoutValidation("Link", link);
        if (finalUrl != null) response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl);
        return response;
    }

    private static object Release(string version, bool draft = false, bool prerelease = false) => new
    {
        url = "https://api.github.com/repos/ailuo04/clipbridge/releases/1",
        assets_url = "https://api.github.com/repos/ailuo04/clipbridge/releases/1/assets",
        upload_url = "https://uploads.github.com/repos/ailuo04/clipbridge/releases/1/assets{?name,label}",
        html_url = "https://github.com/ailuo04/clipbridge/releases/tag/v" + version,
        id = 1,
        author = new { login = "ailuo04", id = 1, type = "User" },
        node_id = "R_test",
        tag_name = "v" + version,
        target_commitish = "master",
        name = "ClipBridge " + version,
        draft,
        prerelease,
        created_at = "2026-01-01T00:00:00Z",
        published_at = "2026-01-02T00:00:00Z",
        assets = Array.Empty<object>(),
        tarball_url = "https://api.github.com/repos/ailuo04/clipbridge/tarball/v" + version,
        zipball_url = "https://api.github.com/repos/ailuo04/clipbridge/zipball/v" + version,
        body = "Synthetic published release fixture"
    };

    private static string ReleaseVersion(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;
        string version = path[(path.LastIndexOf('/') + 1)..].Replace("manifest-", "").Replace(".json", "");
        return version;
    }


    private const string WebPage1 = "https://github.com/ailuo04/clipbridge/releases";
    private const string WebPage2 = "https://github.com/ailuo04/clipbridge/releases?page=2";

    private static HttpResponseMessage HtmlResponse(string html, string? finalUrl = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };
        if (finalUrl != null) response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl);
        return response;
    }

    // Match the release section, metadata and pagination returned by GitHub's public page.
    private static string WebRelease(string version, bool prerelease = false, bool draft = false) => $$"""
        <section id="release-v{{version}}" aria-labelledby="heading-{{version}}" data-release-anchor="release-v{{version}}">
          <h2 class="sr-only" id="heading-{{version}}">ClipBridge {{version}}</h2>
          <div class="Box"><div class="Box-body">
            <span class="f1 text-bold"><a href="/ailuo04/clipbridge/releases/tag/v{{version}}" class="Link--primary Link">ClipBridge {{version}}</a></span>
            {{(prerelease ? "<span class=\"Label Label--warning\">Pre-release</span>" : "")}}
            {{(draft ? "<span class=\"Label Label--secondary\">Draft</span>" : "")}}
            <relative-time class="no-wrap" datetime="2026-01-02T00:00:00Z">2 Jan</relative-time>
            <div class="markdown-body tmp-my-3">
              <a href="/ailuo04/clipbridge/releases/tag/v0.3.8">An older version mentioned in release notes</a>
              <span class="Label">Pre-release</span>
            </div>
          </div></div>
        </section>
        """;

    private static string WebPage(string releases, string? next = null) => $$"""
        <!doctype html><html><body><section aria-label="Releases results">
        <rails-partial data-partial-name="releasesIndexRoute.Results">
          <div hidden id="release_page_title">Releases · ailuo04/clipbridge</div>
          <div data-pjax data-hpc>{{releases}}</div>
          <div class="paginate-container"><div role="navigation" aria-label="Pagination" class="pagination">
            {{(next == null ? "<span class=\"next_page disabled\">Next</span>" : "<a class=\"next_page\" rel=\"next\" href=\"" + next + "\">Next</a>")}}
          </div></div>
        </rails-partial></section></body></html>
        """;

    private static async Task VerifyWebFallbackAsync()
    {
        foreach (HttpStatusCode limited in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            var requests = new List<string>();
            using JsonDocument signed = JsonDocument.Parse(Fixture("manifest-0.5.4.json"));
            string badSignature = JsonSerializer.Serialize(new
            {
                payload = signed.RootElement.GetProperty("payload").GetString(),
                signature = Convert.ToBase64String(new byte[384])
            });
            using var handler = new ScriptedHandler(request =>
            {
                string url = request.RequestUri!.AbsoluteUri;
                requests.Add(url);
                if (url == ApiPage1) return new HttpResponseMessage(limited);
                if (url == WebPage1)
                {
                    Check(request.Headers.Accept.Any(value => value.MediaType == "text/html"), "fallback requests HTML from the public release page");
                    return HtmlResponse(WebPage(WebRelease("0.5.6") + WebRelease("0.5.7") + WebRelease("0.5.5", draft: true) + WebRelease("0.5.4") + WebRelease("0.5.3") + WebRelease("0.5.2"), "/ailuo04/clipbridge/releases?page=2"));
                }
                if (url == WebPage2) return HtmlResponse(WebPage(WebRelease("0.5.4") + WebRelease("0.4.0", prerelease: true) + WebRelease("0.3.9") + WebRelease("0.3.10")));
                if (url == RawRoot + "manifest-0.5.4.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("manifest-0.5.4.json")) };
                if (url == RawRoot + "manifest-0.5.3.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("manifest-0.5.0.json")) };
                if (url == RawRoot + "manifest-0.5.2.json") return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (url == RawRoot + "manifest-0.4.0.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("manifest-0.4.0.json")) };
                if (url == RawRoot + "manifest-0.3.9.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("manifest-0.3.9.json")) };
                if (url == RawRoot + "manifest-0.3.10.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(badSignature) };
                throw new Exception("Unexpected fallback request: " + url);
            });
            using var client = new HttpClient(handler);
            using var catalog = new GitHubReleaseCatalog(client);
            IReadOnlyList<GitHubRollbackCandidate> candidates = await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
            Check(candidates.Select(candidate => candidate.TagName).SequenceEqual(new[] { "v0.5.4", "v0.5.3", "v0.5.2", "v0.4.0", "v0.3.10", "v0.3.9" }), "HTTP " + (int)limited + " falls back to all public release pages with numeric sorting and deduplication");
            Check(candidates[0].Update is { IsRollback: true, Version: "0.5.4" } && !candidates[0].IsPrerelease, "fallback uses signed manifests and ignores release-note links and badges");
            Check(candidates[0].PublishedAt == new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "fallback retains release publication dates");
            Check(candidates[1].Update == null && candidates[2].Update == null && candidates[4].Update == null, "fallback cannot enable mismatched, missing or invalidly signed manifests");
            Check(candidates[3].IsPrerelease && candidates[3].Update?.Version == "0.4.0" && candidates[5].Update?.Version == "0.3.9", "fallback retains prerelease marking and the oldest published version");
            Check(requests.Count(url => url == WebPage2) == 1 && requests.Count(url => url == RawRoot + "manifest-0.5.4.json") == 1, "fallback paginates once and verifies duplicate releases only once");
        }

        using (var handler = new ScriptedHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            ApiPage1 => JsonResponse(new[] { Release("0.5.4") }, $"<{ApiPage2}>; rel=\"next\""),
            ApiPage2 => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            WebPage1 => HtmlResponse(WebPage(WebRelease("0.3.9"))),
            RawRoot + "manifest-0.3.9.json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("manifest-0.3.9.json")) },
            _ => throw new Exception("Unexpected partial-API request: " + request.RequestUri)
        }))
        using (var client = new HttpClient(handler))
        using (var catalog = new GitHubReleaseCatalog(client))
        {
            var candidates = await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
            Check(candidates.Count == 1 && candidates[0].Update?.Version == "0.3.9", "rate limiting on later API pages restarts a complete public-page scan");
        }

        foreach (string next in new[]
        {
            "https://evil.example/ailuo04/clipbridge/releases?page=2",
            "http://github.com/ailuo04/clipbridge/releases?page=2",
            "https://github.com:444/ailuo04/clipbridge/releases?page=2",
            "/other/repo/releases?page=2",
            "/ailuo04/clipbridge/releases?page=2&amp;other=1",
            "/ailuo04/clipbridge/releases?page=2#fragment",
            "/ailuo04/clipbridge/releases?page=0",
            "/ailuo04/clipbridge/releases"
        })
        {
            int webRequests = 0;
            using var handler = new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsoluteUri == ApiPage1) return new HttpResponseMessage(HttpStatusCode.Forbidden);
                webRequests++;
                if (request.RequestUri.AbsoluteUri != WebPage1) throw new Exception("Unsafe pagination was requested");
                return HtmlResponse(WebPage(WebRelease("0.5.4"), next));
            });
            using var client = new HttpClient(handler);
            using var catalog = new GitHubReleaseCatalog(client);
            bool rejected = false;
            try { await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
            catch (UpdateProtocolException) { rejected = true; }
            Check(rejected && webRequests == 1, "fallback rejects unsafe or looping pagination before requesting it: " + next);
        }

        foreach (string failure in new[] { "unavailable", "malformed", "oversized", "redirected" })
        {
            using var handler = new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsoluteUri == ApiPage1) return new HttpResponseMessage(HttpStatusCode.Forbidden);
                if (request.RequestUri.AbsoluteUri != WebPage1) throw new Exception("Unexpected failed-fallback request");
                return failure switch
                {
                    "unavailable" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    "malformed" => HtmlResponse("<html><body>Sign in or try again</body></html>"),
                    "oversized" => HtmlResponse(new string('x', 2 * 1024 * 1024 + 1)),
                    _ => HtmlResponse(WebPage(WebRelease("0.5.4")), "https://evil.example/releases")
                };
            });
            using var client = new HttpClient(handler);
            using var catalog = new GitHubReleaseCatalog(client);
            bool rejected = false;
            try { await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
            catch (UpdateProtocolException) { rejected = true; }
            Check(rejected, "fallback fails closed for " + failure + " responses");
        }

        using (var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri != ApiPage1) throw new Exception("Unexpected fallback for a non-rate-limit failure");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }))
        using (var client = new HttpClient(handler))
        using (var catalog = new GitHubReleaseCatalog(client))
        {
            bool rejected = false;
            try { await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
            catch (UpdateProtocolException) { rejected = true; }
            Check(rejected, "non-rate-limit API errors still stop loading");
        }
    }

    public static async Task VerifyAsync()
    {
        await VerifyWebFallbackAsync();
        string manifest54 = Fixture("manifest-0.5.4.json");
        string manifest50 = Fixture("manifest-0.5.0.json");
        string manifest40 = Fixture("manifest-0.4.0.json");
        string manifest39 = Fixture("manifest-0.3.9.json");
        var requests = new List<string>();
        using var handler = new ScriptedHandler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            if (url == ApiPage1)
            {
                return JsonResponse(new object[]
                {
                    Release("0.5.6"),
                    Release("0.5.7"),
                    Release("0.5.5", draft: true),
                    Release("0.5.4"),
                    Release("0.5.3"),
                    Release("0.5.2")
                }, $"<{ApiPage2}>; rel=\"next\"");
            }
            if (url == ApiPage2)
                return JsonResponse(new object[] { Release("0.4.0", prerelease: true), Release("0.3.9") });
            if (url == RawRoot + "manifest-0.5.4.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest54) };
            if (url == RawRoot + "manifest-0.5.3.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest50) };
            if (url == RawRoot + "manifest-0.5.2.json") return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (url == RawRoot + "manifest-0.4.0.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest40) };
            if (url == RawRoot + "manifest-0.3.9.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest39) };
            throw new Exception("Unexpected request: " + url);
        });
        using var client = new HttpClient(handler);
        using var catalog = new GitHubReleaseCatalog(client);
        IReadOnlyList<GitHubRollbackCandidate> candidates = await catalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
        Check(candidates.Select(candidate => candidate.TagName).SequenceEqual(new[] { "v0.5.4", "v0.5.3", "v0.5.2", "v0.4.0", "v0.3.9" }), "all old releases across API pages are sorted by numeric version");
        Check(candidates.All(candidate => Version.Parse(candidate.Update?.Version ?? candidate.TagName[1..]) < new Version(0, 5, 6)), "current and newer releases are not rollback candidates");
        Check(candidates.All(candidate => candidate.TagName != "v0.5.5"), "draft releases are excluded");
        Check(candidates[0].Update is { IsRollback: true, Version: "0.5.4" }, "valid historical manifest becomes an installable rollback target");
        Check(candidates[1].Update == null && candidates[1].UnavailableReason != null, "tag and signed manifest version mismatch remains disabled");
        Check(candidates[2].Update == null && candidates[2].UnavailableReason != null, "missing historical manifest remains disabled");
        Check(candidates[3].IsPrerelease && candidates[3].Update?.Version == "0.4.0", "published prerelease is retained and marked");
        Check(candidates[4].Update?.Version == "0.3.9", "oldest page candidate remains discoverable");
        Check(requests.Contains(ApiPage2) && requests.Count(url => url.Contains("/releases?", StringComparison.Ordinal)) == 2, "pagination follows next link and stops when it ends");

        using var rateLimitHandler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var rateLimitClient = new HttpClient(rateLimitHandler);
        using var rateLimitCatalog = new GitHubReleaseCatalog(rateLimitClient);
        bool rateLimited = false;
        try { await rateLimitCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
        catch (UpdateProtocolException) { rateLimited = true; }
        Check(rateLimited, "API and public-page failures stop loading instead of returning unverified candidates");

        using var hostileLinkHandler = new ScriptedHandler(request => JsonResponse(Array.Empty<object>(), "<https://example.com/releases?page=2>; rel=\"next\""));
        using var hostileLinkClient = new HttpClient(hostileLinkHandler);
        using var hostileLinkCatalog = new GitHubReleaseCatalog(hostileLinkClient);
        bool hostileLinkRejected = false;
        try { await hostileLinkCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
        catch (UpdateProtocolException) { hostileLinkRejected = true; }
        Check(hostileLinkRejected, "pagination cannot escape the fixed GitHub API host");

        int nonstandardPortRequests = 0;
        using var portLinkHandler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Port != 443) nonstandardPortRequests++;
            return JsonResponse(Array.Empty<object>(), "<https://api.github.com:444/repos/ailuo04/clipbridge/releases?per_page=100&page=2>; rel=\"next\"");
        });
        using var portLinkClient = new HttpClient(portLinkHandler);
        using var portLinkCatalog = new GitHubReleaseCatalog(portLinkClient);
        bool portLinkRejected = false;
        try { await portLinkCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
        catch (UpdateProtocolException) { portLinkRejected = true; }
        Check(portLinkRejected && nonstandardPortRequests == 0, "pagination rejects nonstandard ports before issuing another request");

        using var extraQueryHandler = new ScriptedHandler(_ => JsonResponse(Array.Empty<object>(), "<https://api.github.com/repos/ailuo04/clipbridge/releases?per_page=100&page=2&other=1>; rel=\"next\""));
        using var extraQueryClient = new HttpClient(extraQueryHandler);
        using var extraQueryCatalog = new GitHubReleaseCatalog(extraQueryClient);
        bool extraQueryRejected = false;
        try { await extraQueryCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
        catch (UpdateProtocolException) { extraQueryRejected = true; }
        Check(extraQueryRejected, "pagination rejects unexpected query parameters");
        using var malformedHandler = new ScriptedHandler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == ApiPage1) return JsonResponse(new object[] { Release("release-not-a-version"), new { draft = false, prerelease = false }, Release("0.5.4") });
            if (url == RawRoot + "manifest-0.5.4.json") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest54) };
            throw new Exception("Unexpected malformed-entry request: " + url);
        });
        using var malformedClient = new HttpClient(malformedHandler);
        using var malformedCatalog = new GitHubReleaseCatalog(malformedClient);
        IReadOnlyList<GitHubRollbackCandidate> afterMalformed = await malformedCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
        Check(afterMalformed.Count == 1 && afterMalformed[0].Update?.Version == "0.5.4", "malformed release entries are ignored without aborting valid history");

        using var apiRedirectHandler = new ScriptedHandler(_ => JsonResponse(Array.Empty<object>(), finalUrl: "https://evil.example/repos/ailuo04/clipbridge/releases?per_page=100"));
        using var apiRedirectClient = new HttpClient(apiRedirectHandler);
        using var apiRedirectCatalog = new GitHubReleaseCatalog(apiRedirectClient);
        bool apiRedirectRejected = false;
        try { await apiRedirectCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None); }
        catch (UpdateProtocolException) { apiRedirectRejected = true; }
        Check(apiRedirectRejected, "final redirected API response cannot come from another host");

        using var manifestRedirectHandler = new ScriptedHandler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == ApiPage1) return JsonResponse(new object[] { Release("0.5.4") });
            if (url == RawRoot + "manifest-0.5.4.json")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest54), RequestMessage = new HttpRequestMessage(HttpMethod.Get, RawRoot + "latest-v2.json") };
            throw new Exception("Unexpected manifest redirect request: " + url);
        });
        using var manifestRedirectClient = new HttpClient(manifestRedirectHandler);
        using var manifestRedirectCatalog = new GitHubReleaseCatalog(manifestRedirectClient);
        IReadOnlyList<GitHubRollbackCandidate> redirectedManifest = await manifestRedirectCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
        Check(redirectedManifest.Count == 1 && redirectedManifest[0].Update == null, "manifest redirect to a different raw path is rejected");

        using var crossHostManifestHandler = new ScriptedHandler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == ApiPage1) return JsonResponse(new object[] { Release("0.5.4") });
            if (url == RawRoot + "manifest-0.5.4.json")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest54), RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/manifest-0.5.4.json") };
            throw new Exception("Unexpected cross-host manifest request: " + url);
        });
        using var crossHostManifestClient = new HttpClient(crossHostManifestHandler);
        using var crossHostManifestCatalog = new GitHubReleaseCatalog(crossHostManifestClient);
        IReadOnlyList<GitHubRollbackCandidate> crossHostManifest = await crossHostManifestCatalog.LoadAsync(new Version(0, 5, 6), CancellationToken.None);
        Check(crossHostManifest.Count == 1 && crossHostManifest[0].Update == null, "manifest redirect to another host is rejected");

        var assemblyVersionCandidates = await catalog.LoadAsync(new Version(0, 5, 4, 0), CancellationToken.None);
        Check(assemblyVersionCandidates.All(candidate => candidate.TagName != "v0.5.4"), "three-part release matching the four-part assembly version is not a rollback target");
    }
}