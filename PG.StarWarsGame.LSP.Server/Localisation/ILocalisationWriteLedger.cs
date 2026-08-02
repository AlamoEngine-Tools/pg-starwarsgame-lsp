// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Remembers what the server itself last wrote to each localisation file, so the file watcher
///     can tell the server's own write apart from someone else's edit.
/// </summary>
/// <remarks>
///     Saving from the localisation editor already reloads the localisation index, and the OS
///     watcher then reports that same write - which reloaded it a second time and announced the
///     index had moved twice. Every open tab re-read its file for each announcement. Nothing about
///     the file had changed between them.
///     <para>
///         Matching on content rather than on a timestamp or a flag is what makes this safe: if
///         someone else's edit lands between the write and the watcher event, the hash differs and
///         the reload happens exactly as it should.
///     </para>
/// </remarks>
public interface ILocalisationWriteLedger
{
    /// <summary>Records that the server wrote <paramref name="contentHash" /> to this file.</summary>
    void Record(string filePath, string contentHash);

    /// <summary>
    ///     Whether this file currently holds exactly what the server last wrote to it. Consumes the
    ///     record, so a second change to the same file is never mistaken for the same echo.
    /// </summary>
    bool ConsumeEcho(string filePath, string currentContentHash);
}

public sealed class LocalisationWriteLedger : ILocalisationWriteLedger
{
    private readonly ConcurrentDictionary<string, string> _lastWritten = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFileHelper _fileHelper;

    public LocalisationWriteLedger(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    public void Record(string filePath, string contentHash)
    {
        _lastWritten[Key(filePath)] = contentHash;
    }

    public bool ConsumeEcho(string filePath, string currentContentHash)
    {
        if (!_lastWritten.TryRemove(Key(filePath), out var written)) return false;
        return string.Equals(written, currentContentHash, StringComparison.Ordinal);
    }

    // Watcher events and handler parameters reach us as differently shaped paths for the same file;
    // normalising is the house rule for anything used as a key.
    private string Key(string filePath)
    {
        return _fileHelper.NormalizeUri(_fileHelper.PathToFileUri(filePath));
    }
}
