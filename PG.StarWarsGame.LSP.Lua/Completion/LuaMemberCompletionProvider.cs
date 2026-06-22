// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaMemberCompletionProvider
{
    public static IEnumerable<CompletionItem> Provide(
        MemberAccessContext ctx,
        IReadOnlyList<ScopeEntry> scope,
        ILuaApiSchemaProvider schema,
        ILuaTypeIndex typeIndex)
    {
        if (ctx.ReceiverName is null) yield break;

        // Resolve receiver type.
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

            // Local/parameter with explicit @type annotation.
            if (entry.TypeName is not null)
            {
                typeName = entry.TypeName;
                break;
            }
        }

        if (typeName is null) yield break;

        // Engine API member functions (TypeName.Method / TypeName:Method patterns).
        foreach (var member in schema.GetMembersOf(typeName))
        {
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

        // Workspace class fields from the type index.
        if (!ctx.IsMethodCall && typeIndex.GetClass(typeName) is { } workspaceCls)
            foreach (var field in workspaceCls.Fields)
                yield return new CompletionItem
                {
                    Label = field.Name,
                    Kind = CompletionItemKind.Field,
                    Detail = field.Type.Raw
                };

        // Engine API class fields (from @class blocks in api.d.lua).
        if (!ctx.IsMethodCall && schema.GetClassDefinition(typeName) is { } apiCls)
            foreach (var field in apiCls.Fields)
                yield return new CompletionItem
                {
                    Label = field.Name,
                    Kind = CompletionItemKind.Field,
                    Detail = field.Type.Raw
                };
    }
}
