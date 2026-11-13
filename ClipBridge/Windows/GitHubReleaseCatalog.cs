using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal sealed record GitHubRollbackCandidate(
    string TagName,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    AvailableUpdate? Update,
    string? UnavailableReason);

internal sealed class GitHubReleaseCatalog : IDisposable
{
    private const string ApiRoot = "https://api.github.com/repos/ailuo04/clipbridge/releases?per_page=100";
    private const string WebRoot = "https://github.com/ailuo04/clipbridge/releases";
    private const string RawRoot = "https://raw.githubusercontent.com/ailuo04/clipbridge/master/";
    private const int MaxReleasePageBytes = 2 * 1024 * 1024;
    private const int MaxManifestBytes = 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    internal GitHubReleaseCatalog(HttpClient? client = null)
    {
        if (client != null)
        {
            _client = client;
            _ownsClient = false;
            return;
        }

        _client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _ownsClient = true;
    }

    internal async Task<IReadOnlyList<GitHubRollbackCandidate>> LoadAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        currentVersion = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        var releases = new List<ReleaseRecord>();
        var requested = new HashSet<string>(StringComparer.Ordinal);
        string next = ApiRoot;
        bool useWeb = false;
        while (true)
        {
            if (!requested.Add(next))
            {
                throw new UpdateProtocolException("GitHub 发布列表分页循环");
            }
            using HttpResponseMessage response = await SendAsync(next, useWeb ? "GitHub 发布网页" : "GitHub 发布列表", cancellationToken, allowApiRateLimit: !useWeb).ConfigureAwait(false);
            if (!useWeb && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                // A later API page can also be rate limited. Restart discovery rather than
                // merging a partial API result with a potentially different public-page snapshot.
                releases.Clear();
                requested.Clear();
                useWeb = true;
                next = WebRoot;
                continue;
            }
            string content = await ReadLimitedStringAsync(response.Content, MaxReleasePageBytes, cancellationToken).ConfigureAwait(false);
            ReleaseRecord[] page;
            string? nextLink;
            if (useWeb)
            {
                (page, nextLink) = ParseWebPage(content);
            }
            else
            {
                page = ParseReleases(content);
                nextLink = GetNextLink(response);
            }
            releases.AddRange(page);
            if (nextLink == null)
            {
                break;
            }
            next = nextLink;
        }

        var candidates = new List<GitHubRollbackCandidate>();
        var seen = new HashSet<Version>();
        foreach (ReleaseRecord release in releases)
        {
            if (release.Draft || !TryParseReleaseVersion(release.TagName, out Version version) || version >= currentVersion || !seen.Add(version))
            {
                continue;
            }

            string versionText = version.ToString(3);
            try
            {
                string manifestUrl = RawRoot + "manifest-" + versionText + ".json";
                using HttpResponseMessage response = await SendAsync(manifestUrl, "历史版本清单", cancellationToken, allowNotFound: true).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    candidates.Add(new GitHubRollbackCandidate(release.TagName, release.PublishedAt, release.Prerelease, null, "未找到对应的签名清单"));
                    continue;
                }
                string manifest = await ReadLimitedStringAsync(response.Content, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
                AvailableUpdate update = UpdateProtocol.ParseAndVerifyRollback(manifest, versionText, currentVersion);
                candidates.Add(new GitHubRollbackCandidate(release.TagName, release.PublishedAt, release.Prerelease, update, null));
            }
            catch (UpdateProtocolException error)
            {
                candidates.Add(new GitHubRollbackCandidate(release.TagName, release.PublishedAt, release.Prerelease, null, error.Message));
            }
        }

        return candidates
            .OrderByDescending(candidate => Version.Parse(candidate.TagName[1..]))
            .ThenByDescending(candidate => candidate.PublishedAt)
            .ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string description, CancellationToken cancellationToken, bool allowNotFound = false, bool allowApiRateLimit = false)
    {
        if (!IsTrustedRequestUrl(url))
        {
            throw new UpdateProtocolException(description + "地址不受信任");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ClipBridge-Windows/0.2");
        request.Headers.Accept.ParseAdd(IsTrustedWebUrl(url) ? "text/html" : "application/vnd.github+json");
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateProtocolException(description + "请求超时");
        }
        if (response.RequestMessage?.RequestUri is Uri finalUri && !string.Equals(finalUri.AbsoluteUri, url, StringComparison.Ordinal))
        {
            response.Dispose();
            throw new UpdateProtocolException(description + "响应地址发生重定向");
        }
        if (response.StatusCode == HttpStatusCode.OK || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) ||
            (allowApiRateLimit && IsTrustedApiUrl(url) && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return response;
        }
        response.Dispose();
        throw new UpdateProtocolException(description + "请求失败：HTTP " + (int)response.StatusCode);
    }

    private static ReleaseRecord[] ParseReleases(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new UpdateProtocolException("GitHub 发布列表格式无效");
            }
            var releases = new List<ReleaseRecord>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("tag_name", out JsonElement tagElement) || tagElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string? tag = tagElement.GetString();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                try
                {
                    bool draft = item.TryGetProperty("draft", out JsonElement draftElement) && draftElement.ValueKind == JsonValueKind.True;
                    bool prerelease = item.TryGetProperty("prerelease", out JsonElement prereleaseElement) && prereleaseElement.ValueKind == JsonValueKind.True;
                    DateTimeOffset? publishedAt = null;
                    if (item.TryGetProperty("published_at", out JsonElement publishedElement) && publishedElement.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
                    {
                        publishedAt = parsed;
                    }
                    releases.Add(new ReleaseRecord(tag, draft, prerelease, publishedAt));
                }
                catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is FormatException)
                {
                    continue;
                }
            }
            return releases.ToArray();
        }
        catch (UpdateProtocolException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException || error is InvalidOperationException)
        {
            throw new UpdateProtocolException("GitHub 发布列表格式无效", error);
        }
    }
    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            return null;
        }
        foreach (string header in values)
        {
            foreach (string part in header.Split(','))
            {
                string[] segments = part.Trim().Split(';');
                if (segments.Length < 2 || !segments.Skip(1).Any(segment => string.Equals(segment.Trim(), "rel=\"next\"", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                string link = segments[0].Trim();
                if (link.Length < 3 || link[0] != '<' || link[^1] != '>')
                {
                    throw new UpdateProtocolException("GitHub 发布列表分页地址无效");
                }
                string url = link[1..^1];
                if (!IsTrustedApiUrl(url))
                {
                    throw new UpdateProtocolException("GitHub 发布列表分页地址不受信任");
                }
                return url;
            }
        }
        return null;
    }

    private static (ReleaseRecord[] Releases, string? Next) ParseWebPage(string html)
    {
        try
        {
            // Match GitHub's published-release sections, never links or badges in release notes.
            MatchCollection sections = HtmlMatches(html, """<section\b(?=[^>]*\bid\s*=\s*["']release-)[^>]*>.*?</section\s*>""");
            if (sections.Count == 0) throw new UpdateProtocolException("GitHub 发布网页格式无效");
            var releases = new List<ReleaseRecord>();
            foreach (Match section in sections)
            {
                string entry = section.Value;
                string? id = HtmlAttribute(entry[..(entry.IndexOf('>') + 1)], "id");
                if (id == null || !id.StartsWith("release-", StringComparison.Ordinal)) continue;
                string tag = id["release-".Length..];
                if (!TryParseReleaseVersion(tag, out _)) continue;

                Match? notes = HtmlMatches(entry, @"<div\b[^>]*>").Cast<Match>()
                    .FirstOrDefault(match => HasHtmlClass(match.Value, "markdown-body"));
                string metadata = notes == null ? entry : entry[..notes.Index];
                string tagPath = "/ailuo04/clipbridge/releases/tag/" + tag;
                if (!HtmlMatches(metadata, @"<a\b[^>]*>").Cast<Match>()
                    .Any(match => HtmlAttribute(match.Value, "href") is string href && (href == tagPath || href == "https://github.com" + tagPath)))
                {
                    throw new UpdateProtocolException("GitHub 发布网页条目格式无效");
                }
                string[] labels = HtmlMatches(metadata, @"<span\b(?<attributes>[^>]*)>(?<label>[^<]*)</span\s*>")
                    .Cast<Match>().Where(match => HasHtmlClass(match.Groups["attributes"].Value, "Label"))
                    .Select(match => WebUtility.HtmlDecode(match.Groups["label"].Value).Trim()).ToArray();
                DateTimeOffset? publishedAt = null;
                Match? time = HtmlMatches(metadata, @"<relative-time\b[^>]*>").Cast<Match>().FirstOrDefault();
                if (time != null && DateTimeOffset.TryParse(HtmlAttribute(time.Value, "datetime"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)) publishedAt = parsed;
                releases.Add(new ReleaseRecord(tag, labels.Contains("Draft", StringComparer.OrdinalIgnoreCase),
                    labels.Contains("Pre-release", StringComparer.OrdinalIgnoreCase), publishedAt));
            }
            return (releases.ToArray(), GetNextWebLink(html));
        }
        catch (RegexMatchTimeoutException error)
        {
            throw new UpdateProtocolException("GitHub 发布网页解析超时", error);
        }
    }

    private static string? GetNextWebLink(string html)
    {
        foreach (Match navigation in HtmlMatches(html, """<div\b(?=[^>]*\bclass\s*=\s*["'][^"']*\bpagination\b)[^>]*>.*?</div\s*>"""))
        {
            string opening = navigation.Value[..(navigation.Value.IndexOf('>') + 1)];
            if (!HasHtmlClass(opening, "pagination")) continue;
            foreach (Match link in HtmlMatches(navigation.Value, @"<a\b[^>]*>"))
            {
                string? rel = HtmlAttribute(link.Value, "rel");
                if (rel == null || !rel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("next", StringComparer.OrdinalIgnoreCase)) continue;
                string? href = HtmlAttribute(link.Value, "href");
                if (href == null || !Uri.TryCreate(new Uri(WebRoot), href, out Uri? next) || !IsTrustedWebUrl(next.AbsoluteUri))
                    throw new UpdateProtocolException("GitHub 发布网页分页地址不受信任");
                return next.AbsoluteUri;
            }
        }
        return null;
    }

    private static MatchCollection HtmlMatches(string html, string pattern) => Regex.Matches(html, pattern,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? HtmlAttribute(string tag, string name)
    {
        Match? match = HtmlMatches(tag, @"(?:^|\s)" + Regex.Escape(name) + """\s*=\s*(?:"([^"]*)"|'([^']*)')""")
            .Cast<Match>().FirstOrDefault();
        return match == null ? null : WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    private static bool HasHtmlClass(string tag, string name) => HtmlAttribute(tag, "class") is string classes &&
        classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    private static bool IsTrustedWebUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/ailuo04/clipbridge/releases" || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (uri.Query.Length == 0) return true;
        const string prefix = "?page=";
        return uri.Query.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(uri.Query[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int page) && page >= 1 &&
            uri.Query[prefix.Length..] == page.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsTrustedRequestUrl(string url) => IsTrustedApiUrl(url) || IsTrustedWebUrl(url) || IsTrustedManifestUrl(url);

    private static bool IsTrustedApiUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, "/repos/ailuo04/clipbridge/releases", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string[] parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parameters.Length is < 1 or > 2) return false;
        bool hasPageSize = false;
        bool hasPage = false;
        foreach (string parameter in parameters)
        {
            int separator = parameter.IndexOf('=');
            if (separator <= 0 || parameter.IndexOf('=', separator + 1) >= 0) return false;
            string key = parameter[..separator];
            string value = parameter[(separator + 1)..];
            if (key == "per_page" && value == "100" && !hasPageSize) hasPageSize = true;
            else if (key == "page" && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int page) && page >= 2 && !hasPage) hasPage = true;
            else return false;
        }
        return hasPageSize && (parameters.Length == 1 || hasPage);
    }
    private static bool IsTrustedManifestUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/ailuo04/clipbridge/master/manifest-", StringComparison.Ordinal) ||
            !uri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }
        string fileName = uri.AbsolutePath["/ailuo04/clipbridge/master/manifest-".Length..^5];
        return Version.TryParse(fileName, out Version? version) && fileName == version.ToString(3);
    }

    private static bool TryParseReleaseVersion(string tag, out Version version)
    {
        version = null!;
        if (tag.Length < 2 || tag[0] != 'v' || !Version.TryParse(tag[1..], out Version? parsed) || tag[1..] != parsed.ToString(3))
        {
            return false;
        }
        version = parsed;
        return true;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new UpdateProtocolException("GitHub 发布列表缺少字段：" + property);
        }
        return value.GetString()!;
    }

    private static async Task<string> ReadLimitedStringAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > maxBytes)
        {
            throw new UpdateProtocolException("网络响应过大");
        }
        await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > maxBytes) throw new UpdateProtocolException("网络响应过大");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private sealed record ReleaseRecord(string TagName, bool Draft, bool Prerelease, DateTimeOffset? PublishedAt);
}