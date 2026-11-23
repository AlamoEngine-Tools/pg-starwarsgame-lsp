// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaImportAnalyzer
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    public static IReadOnlyList<LspDiagnostic> Analyze(
        string documentUri, string text, IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper,
        IMessageCatalog messages, IAssetFileIndex? shippedScripts = null)
    {
        return Analyze(documentUri, LuaSyntaxTree.ParseText(text, s_parseOptions), documents, fileHelper,
            messages, shippedScripts ?? EmptyAssetFileIndex.Instance);
    }

    /// <summary>The unresolved requires of a script.</summary>
    /// <param name="documentUri">The script.</param>
    /// <param name="tree">Its syntax tree.</param>
    /// <param name="documents">Every indexed document: the project's and its dependencies'.</param>
    /// <param name="fileHelper">The URI normalization of the server.</param>
    /// <param name="messages">The message catalogue.</param>
    /// <param name="shippedScripts">
    ///     The asset catalogue, which names every script the game ships. The game packs its Lua
    ///     libraries into its archives, so a mod that requires one carries no copy of it and no document
    ///     is indexed for it - the game loads it all the same.
    /// </param>
    public static IReadOnlyList<LspDiagnostic> Analyze(
        string documentUri, SyntaxTree tree, IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper, IMessageCatalog messages, IAssetFileIndex shippedScripts)
    {
        var root = tree.GetRoot();
        var diagnostics = new List<LspDiagnostic>();

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            var requireArg = ExtractStringArg(call);
            if (requireArg is null) continue;

            var resolved = LuaRequireResolver.Resolve(requireArg, documents, fileHelper, documentUri);
            // Unresolved relative requires are silently skipped - target may be outside the workspace.
            // An unresolved absolute require is a real error, unless the game ships a script of that name
            // where it searches for one.
            if (resolved is null && !LuaRequireResolver.IsRelative(requireArg) &&
                !LuaScriptLookup.Candidates(shippedScripts, requireArg).Any())
                diagnostics.Add(BuildDiagnostic(call, requireArg, messages));
        }

        return diagnostics;
    }

    private static string? ExtractStringArg(FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
            return strArg.Expression.Token.ValueText;

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
            return lit.Token.ValueText;

        return null;
    }

    private static LspDiagnostic BuildDiagnostic(FunctionCallExpressionSyntax call, string requireArg,
        IMessageCatalog messages)
    {
        var span = call.GetLocation().GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        return new LspDiagnostic
        {
            Code = new LspDiagnosticCode(DiagnosticIds.LuaUnresolvedModule.ToString()),
            Severity = LspDiagnosticSeverity.Error,
            Message = messages.Format("LuaImport_CannotFindModule", requireArg),
            Range = new LspRange(
                new LspPosition(start.Line, start.Character),
                new LspPosition(end.Line, end.Character)),
            Source = AppProperties.LspServerId
        };
    }
}