// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaIdentifierCompletionProvider
{
    public static IEnumerable<CompletionItem> Provide(IReadOnlyList<ScopeEntry> scope)
    {
        foreach (var entry in scope)
        {
            var kind = entry.Kind switch
            {
                ScopeEntryKind.LocalVariable => CompletionItemKind.Variable,
                ScopeEntryKind.Parameter => CompletionItemKind.Variable,
                ScopeEntryKind.OwnGlobal or ScopeEntryKind.RequiredGlobal => CompletionItemKind.Function,
                ScopeEntryKind.EngineApi => CompletionItemKind.Function,
                ScopeEntryKind.Lua51Builtin => CompletionItemKind.Keyword,
                _ => CompletionItemKind.Text
            };
            yield return new CompletionItem
            {
                Label = entry.Name,
                Kind = kind,
                Detail = entry.Detail
            };
        }
    }
}
