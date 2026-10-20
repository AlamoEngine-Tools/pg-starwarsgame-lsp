// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Messages;

/// <summary>
///     A text by catalogue key and arguments, kept unformatted until it reaches the reader.
/// </summary>
/// <remarks>
///     For code that finds a problem where no catalogue is at hand - a parser, a validator - and
///     hands it on to code that has one. Formatting at the edge keeps the finding the same whatever
///     the locale, so a locale change needs no re-parse.
/// </remarks>
/// <param name="Key">The catalogue key.</param>
/// <param name="Args">The values placed into the text, in placeholder order.</param>
public sealed record CatalogueText(string Key, params object?[] Args);

/// <summary>Formats a <see cref="CatalogueText" />.</summary>
public static class CatalogueTextExtensions
{
    /// <summary>
    ///     The text for <paramref name="text" /> in the catalogue's language. An argument that is itself
    ///     a <see cref="CatalogueText" /> is formatted first, so a fragment such as a noun travels the
    ///     same way as the message it sits in.
    /// </summary>
    public static string Format(this IMessageCatalog messages, CatalogueText text)
    {
        return messages.Format(text.Key,
            text.Args.Select(a => a is CatalogueText inner ? messages.Format(inner) : a).ToArray());
    }
}