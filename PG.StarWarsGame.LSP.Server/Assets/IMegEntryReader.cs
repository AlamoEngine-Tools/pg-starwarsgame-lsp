// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.Files.MEG.Services;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     Lists what a MEG archive holds, without reading any of it.
/// </summary>
/// <remarks>
///     A seam rather than a direct <see cref="IMegService" /> dependency, so the asset catalog's
///     merge rules can be tested against an in-memory file system that has no real archive in it.
///     Separate from <see cref="IMegArchiveSet" /> because that one answers "give me these bytes"
///     for the game directories and indexes lazily; this one answers "what names are in here" for a
///     named archive, which is the question the catalog asks once at startup.
/// </remarks>
public interface IMegEntryReader
{
    /// <summary>
    ///     The raw entry paths inside <paramref name="megFilePath" />, archive-root-relative and
    ///     spelled however the archive spells them. Empty when the archive cannot be read.
    /// </summary>
    IEnumerable<string> EnumerateEntryPaths(string megFilePath);
}

/// <inheritdoc />
public sealed class MegEntryReader(IMegService megFileService, ILogger<MegEntryReader> logger)
    : IMegEntryReader
{
    public IEnumerable<string> EnumerateEntryPaths(string megFilePath)
    {
        try
        {
            return [.. megFileService.LoadFile(megFilePath).Archive.Select(e => e.Path)];
        }
        catch (Exception e)
        {
            // One unreadable archive must not cost the author the rest of the catalog.
            logger.LogWarning(e, "Could not read the entry table of MEG archive {Path}", megFilePath);
            return [];
        }
    }
}

/// <summary>The reader for a workspace whose archives are of no interest, and for tests.</summary>
public sealed class NoMegEntries : IMegEntryReader
{
    public static readonly NoMegEntries Instance = new();

    public IEnumerable<string> EnumerateEntryPaths(string megFilePath)
    {
        return [];
    }
}

/// <summary>Finds the MEG archives under a set of roots. Extracted so the walk is tested once.</summary>
public static class MegArchiveDiscovery
{
    public static IEnumerable<string> Under(IFileSystem fileSystem, string root)
    {
        if (!fileSystem.Directory.Exists(root))
            return [];

        try
        {
            return fileSystem.Directory.GetFiles(root, "*.meg", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}