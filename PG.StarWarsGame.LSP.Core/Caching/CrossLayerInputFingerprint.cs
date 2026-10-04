// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Names everything a layer's documents read from OUTSIDE their own layer, so a persisted
///     index can be kept when a dependency changed in a way that cannot affect it - and so a
///     dependency opened under different leaves keeps one snapshot per context instead of the
///     contexts overwriting each other's.
/// </summary>
/// <remarks>
///     <para>
///         Replaces the dependency-<c>OverallHash</c> key, which asked the far coarser question
///         "did the dependency change at all" and discarded a whole dependent layer for one
///         changed line. On EaWX that meant an edit in Core re-parsed all four leaves.
///     </para>
///     <para>
///         Two inputs, VERIFIED against the parsers on 2026-09-30 rather than assumed:
///     </para>
///     <list type="number">
///         <item>
///             The <b>file-type registration</b> of each of this layer's own files. Metafiles in
///             ANY layer register types for a file, and the registered types decide which symbol
///             passes run - so a dependency's metafile genuinely changes how a leaf's file parses.
///             Only THIS layer's files are folded in, which is what makes a dependency's own
///             content edits invisible here.
///         </item>
///         <item>
///             The <b>xml roots that can match this layer's files</b>. A story manifest or thread
///             file is indexed as a workspace-file symbol keyed by its path relative to the LONGEST
///             matching xml root, and any layer can contribute a root - so a dependency adding a
///             nested xml directory silently re-keys a leaf's symbols. Only roots that lie inside
///             or contain one of this layer's own xml directories can ever be that longest match;
///             a sibling project's roots cannot, and are left out. MEASURED 2026-10-04: folding in
///             the whole workspace union made EaWX core's key differ between "opened alone" and
///             "under Rev", and its snapshot was discarded on four of seven starts.
///         </item>
///     </list>
///     <para>
///         Everything else the parsers touch is either covered elsewhere or carries no project
///         state: the schema and the story feature flag fold into <see cref="SchemaFingerprint" />;
///         the file helper, parse caches and loggers do not vary by layer.
///     </para>
///     <para>
///         The value doubles as the snapshot's CONTEXT KEY: <see cref="ProjectIndexLocator" />
///         puts it in the file name, so two contexts of one layer are two files.
///     </para>
///     <para>
///         <b>The hazard.</b> A stale index fails SILENTLY - it replays an old parse rather than
///         erroring - and this key is only as good as that list. A parser that gains a new
///         injected input which varies by layer must fold it in here, or bump
///         <see cref="ProjectIndexSnapshot.CurrentSchemaVersion" />.
///     </para>
/// </remarks>
public static class CrossLayerInputFingerprint
{
    /// <summary>
    ///     Computes the key for one layer. <paramref name="layerFileUris" /> are that layer's own
    ///     parseable files, <paramref name="layerXmlDirectories" /> its own xml roots, and
    ///     <paramref name="workspaceXmlDirectories" /> the workspace-wide union they are part of.
    /// </summary>
    public static string Compute(
        IFileTypeRegistry fileTypeRegistry,
        IReadOnlyList<string> layerFileUris,
        IReadOnlyList<string> layerXmlDirectories,
        IReadOnlyList<string> workspaceXmlDirectories)
    {
        var entries = new List<string>(layerFileUris.Count);

        foreach (var uri in layerFileUris)
        {
            var types = fileTypeRegistry.GetTypesForFile(uri);

            // Folded, like every other lookup keyed by a document path: the same file may be
            // spelled in a different case by the snapshot, this scan, or the host, and a
            // case-sensitive key would miss and re-parse the whole layer on every start. Lowering
            // here is safe where storing a lowered path would not be - this string is hashed and
            // discarded, never handed back to the filesystem.
            var sb = new StringBuilder(uri.ToLowerInvariant());

            // Sorted: the registry makes no ordering promise, and two orders of the same types
            // mean the same thing to the parser.
            foreach (var type in types.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                sb.Append('|').Append(type);

            entries.Add(sb.ToString());
        }

        entries.Sort(StringComparer.Ordinal);

        var all = new StringBuilder();
        foreach (var entry in entries)
            all.Append("f:").Append(entry).Append('\n');

        foreach (var dir in RelevantRoots(layerXmlDirectories, workspaceXmlDirectories))
            all.Append("x:").Append(dir).Append('\n');

        return ContentHasher.Hash(all.ToString()).ToString("x16");
    }

    /// <summary>
    ///     The workspace roots that can be the longest match for a file of this layer: those that
    ///     contain one of its own xml directories, or lie inside one. Normalized so the same
    ///     directory spelled as a path, as a URI, or in another case is one root.
    /// </summary>
    private static IEnumerable<string> RelevantRoots(
        IReadOnlyList<string> layerXmlDirectories, IReadOnlyList<string> workspaceXmlDirectories)
    {
        var own = layerXmlDirectories.Select(NormalizeRoot).Distinct(StringComparer.Ordinal).ToArray();

        return workspaceXmlDirectories
            .Select(NormalizeRoot)
            .Where(root => own.Any(o =>
                root.StartsWith(o, StringComparison.Ordinal) || o.StartsWith(root, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => d, StringComparer.Ordinal);
    }

    private static string NormalizeRoot(string directory)
    {
        var normalized = directory.Replace('\\', '/').ToLowerInvariant();
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }
}