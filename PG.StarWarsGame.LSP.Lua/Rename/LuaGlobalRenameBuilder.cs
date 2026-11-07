// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Lua.Parsing;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Rename;

public static class LuaGlobalRenameBuilder
{
    // inScope: which files the global's state spans (#249) - a definition or reference elsewhere
    // is another script's global of the same name. Null spans every file.
    public static WorkspaceEdit? Build(
        string id, string newName, GameIndex index,
        ILuaParseCache parseCache, ILogger logger, Func<string, bool>? inScope = null)
    {
        inScope ??= _ => true;
        if (!IsLeafOwned(id, index, inScope))
        {
            logger.LogDebug("Rename blocked: {Id} is not exclusively defined in the leaf layer", id);
            return null;
        }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();
        // The files the rename edits, as the index spells them: each is searched for uses by value.
        var files = new HashSet<string>(StringComparer.Ordinal);

        // Definition edits - find `function <id>(` in the definition file.
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            foreach (var sym in defs)
            {
                if (sym.Kind != GameSymbolKind.LuaGlobal) continue;
                if (sym.Origin is not FileOrigin fo || !inScope(fo.Uri)) continue;
                var parsed = parseCache.GetOrParse(fo.Uri);
                if (parsed is null) continue;
                var range = FindFunctionNameRange(parsed.Tree, id);
                if (range is null) continue;
                AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = range });
                files.Add(fo.Uri);
            }

        // Reference edits - use indexed LuaGlobal refs (O(1) lookup, no per-file re-parse).
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
            {
                if (r.ExpectedKind != GameSymbolKind.LuaGlobal || !inScope(r.DocumentUri)) continue;
                AddEdit(changes, r.DocumentUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                });
                files.Add(r.DocumentUri);
            }

        // A use by value - handed to Define_State or Register_Timer - is no call site, so the index
        // holds none (it would be 28% more references to keep, almost none of them a function's):
        // found here, in the files the rename edits, the parse cache's trees scoped by Loretta.
        foreach (var file in files)
        {
            if (parseCache.GetOrParse(file) is not { } parsed) continue;
            var edited = changes[DocumentUri.From(file)];
            foreach (var use in GlobalUses(parsed.Tree, id))
                if (edited.All(e => e.Range.Start != use.Start))
                    edited.Add(new TextEdit { NewText = newName, Range = use });
        }

        if (changes.Count == 0)
            return null;

        logger.LogDebug("Rename LuaGlobal {Id} -> {NewName}: {Count} file(s)", id, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    /// <summary>Every definition in scope is a file in the leaf layer - the project's own to edit.</summary>
    public static bool IsLeafOwned(string id, GameIndex index, Func<string, bool> inScope)
    {
        if (!index.WorkspaceDefinitions.TryGetValue(id, out var defs)) return false;
        var mine = defs.Where(s => s.Origin is not FileOrigin fo || inScope(fo.Uri)).ToList();
        return mine.Count > 0 && mine.All(s => s.Origin is FileOrigin && index.IsLeafSymbol(s));
    }

    internal static LspRange? FindFunctionNameRange(SyntaxTree tree, string globalName)
    {
        var root = tree.GetRoot();

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            if (!string.Equals(simpleName.Name.Text, globalName, StringComparison.OrdinalIgnoreCase)) continue;

            var span = simpleName.Name.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;
            return new LspRange(
                new Position(start.Line, start.Character),
                new Position(end.Line, end.Character));
        }

        return null;
    }

    /// <summary>Every place <paramref name="tree" /> names the global <paramref name="id" />; a local of that name is not it.</summary>
    internal static IEnumerable<LspRange> GlobalUses(SyntaxTree tree, string id)
    {
        var script = new Script([tree]);
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
            if (name.Name == id && script.GetVariable(name) is { Kind: VariableKind.Global })
                yield return RangeOf(name);
    }

    /// <summary>The global named at a position, by its use: an argument, an assignment, a call.</summary>
    internal static (string Name, LspRange Range)? GlobalAt(SyntaxTree tree, int line, int character)
    {
        var script = new Script([tree]);
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var range = RangeOf(name);
            if (range.Start.Line != line || character < range.Start.Character || character > range.End.Character)
                continue;
            return script.GetVariable(name) is { Kind: VariableKind.Global } ? (name.Name, range) : null;
        }

        return null;
    }

    private static LspRange RangeOf(IdentifierNameSyntax name)
    {
        var span = name.GetLocation().GetLineSpan();
        return new LspRange(new Position(span.StartLinePosition.Line, span.StartLinePosition.Character),
            new Position(span.EndLinePosition.Line, span.EndLinePosition.Character));
    }

    private static void AddEdit(
        Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }
}