// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Messages;

/// <summary>
///     An exception whose message is meant for the reader, carried as a <see cref="CatalogueText" />
///     so the code that reports it can say it in the reader's language.
/// </summary>
/// <remarks>
///     The exception's own <see cref="Exception.Message" /> stays English, for the log. Whoever
///     surfaces the error formats <see cref="Text" /> with the catalogue it has.
/// </remarks>
public interface ICatalogueTextError
{
    /// <summary>The message, unformatted.</summary>
    CatalogueText Text { get; }

    /// <summary>The English text of <paramref name="text" />, for an exception's own message.</summary>
    public static string English(CatalogueText text)
    {
        return ResxMessageCatalog.Server("en").Format(text);
    }
}