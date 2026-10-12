// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Files.MEG.Data.EntryLocations;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     The archives the game loads - the workspace's projects' and the configured game folders' -
///     indexed on first use.
/// </summary>
/// <remarks>
///     <para>
///         Lazy on purpose. Indexing every archive of a full install means opening a dozen files and
///         reading tens of thousands of entry records, and most editing sessions never open a preview -
///         the same reasoning that keeps <c>IconCatalogProvider</c> lazy. The index is rebuilt when the
///         project folders or the configured game folders change.
///     </para>
///     <para>
///         Which archives, and in which order, is the game's rule, <see cref="MegArchiveLoadOrder" />:
///         the archives the list names, the patch and the sound archives, searched in the game's
///         order. The first archive that holds a name answers - the one the game would open.
///     </para>
///     <para>
///         Entries are keyed the way the asset catalog spells them, sound archives' flat names under
///         <c>data/audio/sfx/</c> included, so whatever the catalog reports present resolves here.
///     </para>
/// </remarks>
public sealed class MegArchiveSet(
    ILspConfigurationProvider config,
    IModProjectReloadService? projects,
    MegArchiveLoadOrder loadOrder,
    IMegService megFileService,
    ILogger<MegArchiveSet> logger) : IMegArchiveSet
{
    private readonly Lock _gate = new();
    private Index? _index;

    public int ArchiveCount => EnsureIndexed().ArchiveCount;

    public byte[]? TryRead(string normalizedPath)
    {
        if (!EnsureIndexed().Entries.TryGetValue(normalizedPath, out var location))
            return null;

        try
        {
            using var stream = location.GetData();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception e)
        {
            // A corrupt entry must not take down a whole scene; the caller falls through to a miss.
            logger.LogWarning(e, "Could not extract {Path} from its MEG archive", normalizedPath);
            return null;
        }
    }

    private Index EnsureIndexed()
    {
        var roots = MegArchiveRoots.For(projects?.LastWorkspaceConfig, config.Current);
        var key = string.Join('|', [.. roots.ModRoots, roots.GameRoot ?? "", roots.BaseGameRoot ?? ""]);
        if (_index is { } current && current.Key == key)
            return current;

        lock (_gate)
        {
            if (_index is { } raced && raced.Key == key)
                return raced;

            var entries = new Dictionary<string, MegDataEntryLocationReference>(StringComparer.OrdinalIgnoreCase);
            var archives = 0;

            foreach (var archive in loadOrder.SearchOrder(roots))
                try
                {
                    var megFile = megFileService.LoadFile(archive.Path);
                    var megName = Path.GetFileName(archive.Path);
                    foreach (var entry in megFile.Archive)
                    {
                        var path = MegAssetCatalogBuilder.ApplySfxConventions(
                            MegAssetCatalogBuilder.NormalizeMegPath(entry.Path), megName);
                        // First in search order wins: the archive the game searches first answers.
                        entries.TryAdd(path, new MegDataEntryLocationReference(megFile, entry));
                    }

                    archives++;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not load MEG archive {Path}", archive.Path);
                }

            logger.LogInformation("Indexed {Entries} entries from {Archives} MEG archive(s)",
                entries.Count, archives);

            return _index = new Index(key, entries, archives);
        }
    }

    private sealed record Index(
        string Key,
        Dictionary<string, MegDataEntryLocationReference> Entries,
        int ArchiveCount);
}