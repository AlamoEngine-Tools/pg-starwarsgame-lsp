// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Files.MEG.Data.EntryLocations;
using PG.StarWarsGame.Files.MEG.Services;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     The MEG archives of the configured game directories, indexed on first use.
/// </summary>
/// <remarks>
///     <para>
///         Lazy on purpose. Indexing every archive of a full install means opening a dozen files and
///         reading tens of thousands of entry records, and most editing sessions never open a preview -
///         the same reasoning that keeps <c>IconCatalogProvider</c> lazy.
///     </para>
///     <para>
///         Load order is the engine's, via <see cref="MegLoadOrderResolver" />: base game archives
///         first, then expansion archives on top, and within each the non-patch archives before
///         <c>patch.meg</c>, <c>patch2.meg</c> and <c>64patch.meg</c>. A later archive overrides an
///         earlier one, so the index simply overwrites - last writer wins, which IS the engine rule.
///     </para>
/// </remarks>
public sealed class MegArchiveSet(
    IFileSystem fileSystem,
    ILspConfigurationProvider config,
    IMegFileService megFileService,
    IMegFileExtractor megExtractor,
    ILogger<MegArchiveSet> logger) : IMegArchiveSet
{
    private readonly Lock _gate = new();
    private Dictionary<string, MegDataEntryLocationReference>? _entries;
    private int _archiveCount;

    public int ArchiveCount
    {
        get
        {
            EnsureIndexed();
            return _archiveCount;
        }
    }

    public byte[]? TryRead(string normalizedPath)
    {
        EnsureIndexed();

        if (_entries is null || !_entries.TryGetValue(normalizedPath, out var location))
            return null;

        try
        {
            using var stream = megExtractor.GetData(location);
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

    private void EnsureIndexed()
    {
        if (_entries is not null)
            return;

        lock (_gate)
        {
            if (_entries is not null)
                return;

            var entries = new Dictionary<string, MegDataEntryLocationReference>(
                StringComparer.OrdinalIgnoreCase);
            var archives = 0;

            foreach (var megPath in OrderedArchivePaths())
                try
                {
                    var megFile = megFileService.Load(megPath);
                    foreach (var entry in megFile.Archive)
                        entries[MegAssetCatalogBuilder.NormalizeMegPath(entry.Path)] =
                            new MegDataEntryLocationReference(megFile, entry);

                    archives++;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not load MEG archive {Path}", megPath);
                }

            _archiveCount = archives;
            _entries = entries;

            logger.LogInformation("Indexed {Entries} entries from {Archives} MEG archive(s)",
                entries.Count, archives);
        }
    }

    /// <summary>
    ///     Every archive of the configured directories, base game first so the expansion overrides it.
    /// </summary>
    private List<string> OrderedArchivePaths()
    {
        var ordered = new List<string>();

        // Base game first, expansion second: later entries win, and FoC layers over EaW.
        foreach (var root in new[] { config.Current.GamePath, config.Current.ExpansionPath })
        {
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.Directory.Exists(root))
                continue;

            try
            {
                var found = fileSystem.Directory.GetFiles(root, "*.meg", SearchOption.AllDirectories);
                ordered.AddRange(MegLoadOrderResolver.Resolve(found, root));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not enumerate MEG archives under {Root}", root);
            }
        }

        return ordered;
    }
}
