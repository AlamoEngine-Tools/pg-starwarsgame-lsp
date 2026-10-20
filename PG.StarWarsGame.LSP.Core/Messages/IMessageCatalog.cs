// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Messages;

/// <summary>
///     The server's own texts - diagnostics, hovers, notifications - by key, in the language the
///     configured locale names. Texts the schema supplies are already per locale and do not pass
///     through here.
/// </summary>
public interface IMessageCatalog
{
    /// <summary>
    ///     The text for <paramref name="key" /> with <paramref name="args" /> placed into it.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The catalogue carries no such key - a bug, never a text.</exception>
    string Format(string key, params object?[] args);
}