// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Semver;

namespace PG.StarWarsGame.LSP.Schema.Versioning;

/// <summary>A published schema release: its git tag and the version that tag names.</summary>
public sealed record SchemaRelease(string Tag, SemVersion Version);

/// <summary>
///     Finds the newest published schema release this server can read.
///     <para>
///         The schema repository publishes by tagging <c>v&lt;schemaVersion&gt;</c> and creating a
///         GitHub release. Drafts, releases flagged as prereleases, tags with a prerelease suffix
///         and tags that are not a strict semantic version are never chosen; of the rest, the
///         highest inside the supported range wins.
///     </para>
///     <para>
///         Every failure - no network, a rate limit, an unexpected body - resolves to
///         <see langword="null" /> rather than throwing, so the caller can fall back to the last
///         release it cached. Only a cancellation by the caller propagates.
///     </para>
/// </summary>
public sealed class SchemaReleaseResolver
{
    private const string ApiBase = "https://api.github.com/";

    private static readonly ProductInfoHeaderValue UserAgent = new(
        "aet-eaw-edit", typeof(SchemaReleaseResolver).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    private readonly HttpClient _http;
    private readonly ILogger<SchemaReleaseResolver> _logger;

    public SchemaReleaseResolver(HttpClient http, ILogger<SchemaReleaseResolver> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <param name="repository"><c>owner/name</c> of the schema repository.</param>
    /// <param name="supportedRange">
    ///     npm-style range; defaults to the range <see cref="SchemaVersionGate" /> enforces, so the
    ///     resolver never picks a release the gate would then refuse.
    /// </param>
    /// <param name="ct">Cancels the request; the only failure that is not swallowed.</param>
    /// <returns>The chosen release, or <see langword="null" /> when none qualifies or none could be listed.</returns>
    public async Task<SchemaRelease?> ResolveAsync(
        string repository, string supportedRange = SchemaVersionGate.SupportedRange, CancellationToken ct = default)
    {
        var url = $"{ApiBase}repos/{repository}/releases?per_page=100";
        List<GitHubRelease>? releases;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // GitHub rejects API requests without a User-Agent.
            request.Headers.UserAgent.Add(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Schema releases of {Repository} not listed - HTTP {Status}",
                    repository, (int)response.StatusCode);
                return null;
            }

            releases = JsonSerializer.Deserialize<List<GitHubRelease>>(await response.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            // OperationCanceledException without a cancelled token is HttpClient's timeout.
            _logger.LogWarning(e, "Schema releases of {Repository} not listed", repository);
            return null;
        }

        var chosen = Choose(releases ?? [], SemVersionRange.ParseNpm(supportedRange));
        if (chosen is null)
            _logger.LogWarning("No schema release of {Repository} matches {Range}", repository, supportedRange);
        else
            _logger.LogInformation("Schema release {Tag} of {Repository} resolved", chosen.Tag, repository);
        return chosen;
    }

    private static SchemaRelease? Choose(IEnumerable<GitHubRelease> releases, SemVersionRange range)
    {
        SchemaRelease? best = null;
        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName)) continue;

            var tag = release.TagName;
            var raw = tag[0] is 'v' or 'V' ? tag[1..] : tag;
            if (!SemVersion.TryParse(raw, SemVersionStyles.Strict, out var version)) continue;
            if (version.IsPrerelease || !range.Contains(version)) continue;

            if (best is null || version.ComparePrecedenceTo(best.Version) > 0)
                best = new SchemaRelease(tag, version);
        }

        return best;
    }

    /// <summary>The fields of a GitHub release this resolver reads.</summary>
    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")]
        string? TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")]
        bool Prerelease);
}
