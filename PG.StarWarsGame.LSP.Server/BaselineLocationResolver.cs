// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>Where the baseline came from.</summary>
public enum BaselineOrigin
{
    /// <summary>A URL the configuration named outright.</summary>
    Explicit,

    /// <summary>The newest release inside the DTO version this server reads.</summary>
    Release,

    /// <summary>The default branch - no release qualified or none could be listed.</summary>
    Branch
}

/// <summary>Where the baseline file is read from.</summary>
/// <param name="Url">The <c>.aet</c> file; its icon sidecar sits beside it.</param>
/// <param name="Tag">The release tag, when <paramref name="Origin" /> is a release.</param>
[PublicAPI]
public sealed record BaselineLocation(string Url, string? Tag, BaselineOrigin Origin);

/// <summary>
///     Decides where the baseline comes from, in order: an explicit URL; the newest release inside
///     the DTO version this server reads; the default branch.
///     <para>
///         A merge to the baseline repository's <c>main</c> used to publish to every installed
///         client, because the server downloaded from <c>refs/heads/main</c>. Released servers now
///         read a release, the way they read the schema, and the supported range is the DTO version
///         <see cref="SerializedBaseline.CurrentSchemaVersion" /> - so a server never downloads a
///         baseline it would then refuse. The branch is the no-release fallback that keeps a server
///         working before the first release exists; the bytes cached by an earlier start are the
///         loaders' own fallback below that.
///     </para>
///     <para>
///         Resolved once per source: the baseline and its icon sidecar are loaded by two loaders at
///         startup, and both read the same tag.
///     </para>
/// </summary>
public sealed class BaselineLocationResolver
{
    /// <summary>The baseline DTO versions this server reads, as the release resolver's npm-style range.</summary>
    public static readonly string SupportedRange =
        $">={SerializedBaseline.CurrentSchemaVersion}.0.0 <{SerializedBaseline.CurrentSchemaVersion + 1}.0.0";

    private readonly ILogger<BaselineLocationResolver> _logger;
    private readonly SchemaReleaseResolver _releases;
    private readonly ConcurrentDictionary<BaselineSourceConfig, Task<BaselineLocation>> _resolved = new();

    public BaselineLocationResolver(SchemaReleaseResolver releases, ILogger<BaselineLocationResolver> logger)
    {
        _releases = releases;
        _logger = logger;
    }

    /// <summary>URL of the baseline file at a release tag.</summary>
    public static string TagUrl(string repository, string tag, string filePath)
    {
        return $"https://raw.githubusercontent.com/{repository}/refs/tags/{tag}/{filePath}";
    }

    /// <summary>URL of the baseline file on the default branch.</summary>
    public static string BranchUrl(string repository, string filePath)
    {
        return $"https://raw.githubusercontent.com/{repository}/refs/heads/main/{filePath}";
    }

    public async Task<BaselineLocation> ResolveAsync(BaselineSourceConfig source, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(source.Url))
            return new BaselineLocation(source.Url, null, BaselineOrigin.Explicit);

        // A record: two configurations naming the same repository and file share one lookup.
        var lookup = _resolved.GetOrAdd(source, s => ResolveOfficialAsync(s, ct));
        try
        {
            return await lookup;
        }
        catch (OperationCanceledException)
        {
            // A cancelled start must not pin the next one to a cancelled lookup.
            _resolved.TryRemove(source, out _);
            throw;
        }
    }

    private async Task<BaselineLocation> ResolveOfficialAsync(BaselineSourceConfig source, CancellationToken ct)
    {
        if (await _releases.ResolveAsync(source.Repository, SupportedRange, ct) is { } release)
            return new BaselineLocation(TagUrl(source.Repository, release.Tag, source.FilePath), release.Tag,
                BaselineOrigin.Release);

        _logger.LogWarning(
            "No baseline release of {Repository} inside {Range} available; using its default branch",
            source.Repository, SupportedRange);
        return new BaselineLocation(BranchUrl(source.Repository, source.FilePath), null, BaselineOrigin.Branch);
    }
}