// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Core.Util;

/// <summary>
///     The one completion capability a client registering statically gets for every language this
///     server completes (#489).
/// </summary>
/// <remarks>
///     A static capability has no document selector, so the initialize result can name only one set of
///     trigger characters for the whole server. With each handler declaring its own, the library put
///     one handler's options there, a different one per start (measured): half the starts lost the Lua
///     trigger characters and the resolve of the analyzer's items, the other half the XML ones. So the
///     initialize result carries the union, and a handler answers nothing to a character that opens
///     the list for another language only.
/// </remarks>
public static class CompletionTriggers
{
    /// <summary>
    ///     True when a trigger character opened the list and it is not one of <paramref name="own" />.
    /// </summary>
    public static bool IsForeign(CompletionParams request, Container<string>? own)
    {
        return request.Context is
               {
                   TriggerKind: CompletionTriggerKind.TriggerCharacter, TriggerCharacter: { } character
               } &&
               own?.Contains(character, StringComparer.Ordinal) != true;
    }

    /// <summary>The handlers' options as one: every trigger character, and a resolve if any resolves.</summary>
    public static CompletionRegistrationOptions.StaticOptions Merge(
        IEnumerable<CompletionRegistrationOptions> handlers)
    {
        var all = handlers.ToList();
        return new CompletionRegistrationOptions.StaticOptions
        {
            TriggerCharacters = new Container<string>(all
                .SelectMany(h => h.TriggerCharacters ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)),
            ResolveProvider = all.Any(h => h.ResolveProvider)
        };
    }
}