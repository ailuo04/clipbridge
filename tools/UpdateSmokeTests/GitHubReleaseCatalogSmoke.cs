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

    public static async Task VerifyAsync()
    {
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
        Check(rateLimited, "GitHub API failure stops catalog loading instead of returning unverified candidates");

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
    }
}