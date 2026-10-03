// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Schema.Cache;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Schema.Providers;

/// <summary>Why the schema is read from where it is.</summary>
public enum SchemaOrigin
{
    /// <summary>A URL the user configured; no release resolution.</summary>
    Explicit,

    /// <summary>The newest release in range, resolved at this start.</summary>
    Release,

    /// <summary>Releases could not be listed; the release cached by an earlier start.</summary>
    CachedRelease,

    /// <summary>No release resolvable and none cached; the repository's default branch.</summary>
    Branch
}

/// <summary>Where to read the schema from: the base URL of its <c>eaw/</c> folder.</summary>
/// <param name="BaseUrl">Ends in <c>eaw/</c>; <c>_index.json</c> and the YAML files are relative to it.</param>
/// <param name="Tag">The release tag, or null for a branch or an explicit URL.</param>
/// <param name="Origin">How the location was chosen.</param>
public sealed record SchemaLocation(string BaseUrl, string? Tag, SchemaOrigin Origin);

/// <summary>
///     Decides where the schema comes from, in order: an explicit URL; the newest release inside
///     the supported range; the release cached by an earlier start; the default branch. The last
///     step keeps a server working before the first release exists and on an offline first start.
/// </summary>
public sealed class SchemaLocationResolver
{
    private readonly SchemaHttpCache _cache;
    private readonly ILogger<SchemaLocationResolver> _logger;
    private readonly SchemaReleaseResolver _releases;

    public SchemaLocationResolver(SchemaReleaseResolver releases, SchemaHttpCache cache,
        ILogger<SchemaLocationResolver> logger)
    {
        _releases = releases;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Base URL of the <c>eaw/</c> folder at a release tag.</summary>
    public static string TagUrl(string repository, string tag)
    {
        return $"https://raw.githubusercontent.com/{repository}/refs/tags/{tag}/eaw/";
    }

    /// <summary>Base URL of the <c>eaw/</c> folder on the default branch.</summary>
    public static string BranchUrl(string repository)
    {
        return $"https://raw.githubusercontent.com/{repository}/refs/heads/main/eaw/";
    }

    public async Task<SchemaLocation> ResolveAsync(string repository, string? explicitUrl,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
            return new SchemaLocation(explicitUrl, null, SchemaOrigin.Explicit);

        if (await _releases.ResolveAsync(repository, ct: ct) is { } release)
            return new SchemaLocation(TagUrl(repository, release.Tag), release.Tag, SchemaOrigin.Release);

        if (_cache.CachedTag is { } cached)
        {
            _logger.LogWarning("Schema releases unavailable; using cached release {Tag}", cached);
            return new SchemaLocation(TagUrl(repository, cached), cached, SchemaOrigin.CachedRelease);
        }

        _logger.LogWarning("No schema release available or cached; using the default branch of {Repository}",
            repository);
        return new SchemaLocation(BranchUrl(repository), null, SchemaOrigin.Branch);
    }
}