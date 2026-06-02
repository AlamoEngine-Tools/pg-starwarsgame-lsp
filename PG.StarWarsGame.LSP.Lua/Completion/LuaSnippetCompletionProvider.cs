// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaSnippetCompletionProvider
{
    public static IReadOnlyList<CompletionItem> Snippets { get; } =
    [
        Snippet("if", "if $1 then\n\t$0\nend"),
        Snippet("if/else", "if $1 then\n\t$2\nelse\n\t$0\nend"),
        Snippet("for (numeric)", "for ${1:i} = ${2:1}, ${3:n} do\n\t$0\nend"),
        Snippet("for (generic)", "for ${1:k}, ${2:v} in pairs($3) do\n\t$0\nend"),
        Snippet("while", "while $1 do\n\t$0\nend"),
        Snippet("repeat", "repeat\n\t$0\nuntil $1"),
        Snippet("function", "function ${1:name}($2)\n\t$0\nend"),
        Snippet("local function", "local function ${1:name}($2)\n\t$0\nend"),
        Snippet("local", "local ${1:name} = $0"),
    ];

    private static CompletionItem Snippet(string label, string insertText) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Snippet,
        InsertText = insertText,
        InsertTextFormat = InsertTextFormat.Snippet
    };
}
