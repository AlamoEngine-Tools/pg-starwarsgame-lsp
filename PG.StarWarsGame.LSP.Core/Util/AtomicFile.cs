// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text;

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     Writes a file so that a reader in another process sees either the previous content or the
///     new content, never a partial write: the bytes go to a sibling temp file first, which is
///     then moved over the target.
/// </summary>
/// <remarks>
///     Every cache file this server keeps - the schema mirror and baseline under <c>~/.aetswg</c>,
///     the index and bone snapshots under a project's <c>.aetswg/</c> - is shared between the
///     server processes on one machine, one per open project. Two caches had hand-rolled this
///     temp-and-move and five writers had not; this is the one copy.
///     <para>
///         The temp name is unique per call, so two processes racing on the same target cannot
///         clobber each other's temp file; the moves then serialize at the filesystem and the last
///         one wins whole. Serializing the WRITERS themselves, where the content must not be
///         computed twice, is <see cref="ICrossProcessLock" />'s job.
///     </para>
/// </remarks>
public static class AtomicFile
{
    public static void WriteAllBytes(IFileSystem fs, string path, byte[] bytes)
    {
        var directory = fs.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            fs.Directory.CreateDirectory(directory);

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            fs.File.WriteAllBytes(tempPath, bytes);
            fs.File.Move(tempPath, path, true);
        }
        finally
        {
            // Only reached with the temp file still present when the write or the move threw.
            if (fs.File.Exists(tempPath))
                try
                {
                    fs.File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best effort: a stray temp file is harmless, an exception here would mask the
                    // real failure.
                }
        }
    }

    public static void WriteAllText(IFileSystem fs, string path, string text)
    {
        WriteAllBytes(fs, path, new UTF8Encoding(false).GetBytes(text));
    }
}
