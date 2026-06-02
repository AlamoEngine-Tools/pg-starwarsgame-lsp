// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaMemberCompletionProvider
{
    public static IEnumerable<CompletionItem> Provide(
        MemberAccessContext ctx,
        IReadOnlyList<ScopeEntry> scope,
        ILuaApiSchemaProvider schema)
    {
        if (ctx.ReceiverName is null) yield break;

        // Resolve receiver type: check if receiver name is a known engine API function
        string? typeName = null;
        foreach (var entry in scope)
        {
            if (!string.Equals(entry.Name, ctx.ReceiverName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Kind == ScopeEntryKind.EngineApi)
            {
                typeName = schema.GetReturnTypeName(entry.Name);
                break;
            }
        }

        if (typeName is null) yield break;

        foreach (var member in schema.GetMembersOf(typeName))
        {
            // For ":" (method call) show only methods; for "." (field access) show only non-methods
            if (ctx.IsMethodCall != member.IsMethod) continue;

            yield return new CompletionItem
            {
                Label = member.Name,
                Kind = member.IsMethod ? CompletionItemKind.Method : CompletionItemKind.Field,
                Detail = member.Description,
                Documentation = member.ReturnTypeName is not null
                    ? new StringOrMarkupContent($"→ {member.ReturnTypeName}")
                    : null
            };
        }
    }
}
