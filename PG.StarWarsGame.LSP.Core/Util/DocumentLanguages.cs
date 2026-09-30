// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     The language id a document carries, by its extension. Every text-document sync handler
///     answers OmniSharp's "what is this document" question with this, so the three of them agree
///     and a didOpen for an XML file is routed to the XML handler whichever of them the server asks
///     first. A handler that answered with its own language for every URI made that routing depend
///     on enumeration order: some server starts sent every XML open to the dialog handler, which
///     ignores anything but .txt, and the document was never indexed.
/// </summary>
public static class DocumentLanguages
{
    public const string Xml = "xml";
    public const string Lua = "lua";
    public const string PlainText = "plaintext";

    /// <summary>The language id for <paramref name="uriOrPath" />; plain text for anything unknown.</summary>
    public static string LanguageIdOf(string uriOrPath)
    {
        var path = uriOrPath;
        var query = path.IndexOfAny(['?', '#']);
        if (query >= 0) path = path[..query];
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return Xml;
        if (path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) return Lua;
        return PlainText;
    }
}
