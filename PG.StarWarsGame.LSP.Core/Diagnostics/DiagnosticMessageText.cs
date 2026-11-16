// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A diagnostic's message as text, whichever form it travels in.
/// </summary>
/// <remarks>
///     Since LSP 3.18 a diagnostic's message is a string or markup, and the protocol library models it
///     as <see cref="StringOrMarkupContent" />. Every message this server produces is plain text; a
///     markup message reads as its source text. The type's own <c>ToString()</c> is not that: for
///     markup it prefixes the kind (<c>[Markdown] ...</c>), and sorting the type itself throws, since it
///     is not comparable.
/// </remarks>
public static class DiagnosticMessageText
{
    /// <summary>The message's words: its string, or its markup's source text; empty when there is none.</summary>
    public static string Text(this StringOrMarkupContent? message)
    {
        return message?.String ?? message?.MarkupContent?.Value ?? string.Empty;
    }
}