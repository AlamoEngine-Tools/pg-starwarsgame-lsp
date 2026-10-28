// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     The two keys a layer's index snapshot is valid under: the inputs that decide what every parse
///     emits (the schema and the Lua stubs), and the inputs from outside the layer that decide what
///     its own files emit (their file-type registrations and the xml roots that can match them).
/// </summary>
public sealed class IndexFingerprints(
    ISchemaProvider schema,
    // The loaded stubs: their fingerprint is part of the index cache's, so an index built under
    // other stubs is not replayed.
    ILuaApiSchemaProvider luaSchema,
    IFileTypeRegistry fileTypeRegistry,
    IFileHelper fileHelper)
{
    /// <summary>
    ///     The schema's fingerprint, with the Lua stubs' appended when stubs are loaded. Snapshots
    ///     built under a different schema are stale even when every file's content hash matches.
    /// </summary>
    public string Schema()
    {
        var fingerprint = SchemaFingerprint.Compute(schema);
        // The Lua stubs decide which string arguments a script's index records as references.
        // Measured: an index cached under the released stubs (---@xmlref, no longer read) held no
        // Lua reference at all, and a server on the current stubs replayed it.
        if (luaSchema.Fingerprint is { Length: > 0 } luaFingerprint)
            fingerprint += ";lua:" + luaFingerprint;
        return fingerprint;
    }

    /// <summary>
    ///     What a layer's <paramref name="files" /> read from outside the layer. Only meaningful once
    ///     the metafile stage has filled the file-type registry.
    /// </summary>
    public string CrossLayer(IEnumerable<string> files, IReadOnlyList<string> layerXmlDirectories,
        IReadOnlyList<string> allXmlDirectories)
    {
        return CrossLayerInputFingerprint.Compute(
            fileTypeRegistry, [.. files.Select(fileHelper.PathToFileUri)], layerXmlDirectories, allXmlDirectories);
    }
}
