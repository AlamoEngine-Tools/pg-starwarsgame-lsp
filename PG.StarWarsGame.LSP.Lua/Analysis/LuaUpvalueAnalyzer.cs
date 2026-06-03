// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaUpvalueAnalyzer
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    public static IReadOnlyList<LspDiagnostic> Analyze(string text, string? documentUri = null)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions);
        var root = tree.GetRoot();
        var diagnostics = new List<LspDiagnostic>();

        var fileLevelLocals = CollectFileLevelLocals(root);
        if (fileLevelLocals.Count == 0)
            return diagnostics;

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
                     .Where(f => !IsInsideFunction(f)))
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            var functionName = simpleName.Name.Text;

            var ownLocals = CollectOwnLocals(funcDecl);
            EmitUpvalueWarnings(funcDecl, functionName, fileLevelLocals, ownLocals, diagnostics, documentUri);
        }

        return diagnostics;
    }

    private static Dictionary<string, LocalInfo> CollectFileLevelLocals(SyntaxNode root)
    {
        var locals = new Dictionary<string, LocalInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var localDecl in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
        {
            if (IsInsideFunction(localDecl)) continue;

            var span = localDecl.GetLocation().GetLineSpan();
            var line = span.StartLinePosition.Line;
            var suppressed = HasUpvalueOkAnnotation(localDecl);
            foreach (var nameDecl in localDecl.Names)
                locals[nameDecl.Name] = new LocalInfo(suppressed, line);
        }

        return locals;
    }

    private static bool IsInsideFunction(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            if (parent is FunctionDeclarationStatementSyntax or
                LocalFunctionDeclarationStatementSyntax or
                AnonymousFunctionExpressionSyntax)
                return true;
            parent = parent.Parent;
        }

        return false;
    }

    private static bool HasUpvalueOkAnnotation(SyntaxNode node)
    {
        var leadingTrivia = node.GetFirstToken().LeadingTrivia;
        foreach (var trivia in leadingTrivia)
        {
            var text = trivia.ToFullString().Trim();
            if (text.StartsWith("---@upvalue-ok", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // Collects all names that are locally bound WITHIN a top-level function:
    // parameters + local declarations at any depth inside the function body.
    private static HashSet<string> CollectOwnLocals(FunctionDeclarationStatementSyntax funcDecl)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Parameters
        CollectParameterNames(funcDecl.Parameters, names);

        // All local declarations inside the function body (any nesting depth)
        foreach (var local in funcDecl.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
        foreach (var nameDecl in local.Names)
            names.Add(nameDecl.Name);

        foreach (var localFunc in funcDecl.DescendantNodes().OfType<LocalFunctionDeclarationStatementSyntax>())
            names.Add(localFunc.Name.Name);

        // Parameters of nested functions (to avoid false positives)
        foreach (var nested in funcDecl.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
            CollectParameterNames(nested.Parameters, names);

        foreach (var nestedExpr in funcDecl.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            CollectParameterNames(nestedExpr.Parameters, names);

        return names;
    }

    private static void CollectParameterNames(ParameterListSyntax? paramList, HashSet<string> names)
    {
        if (paramList is null) return;
        foreach (var p in paramList.Parameters)
            if (p is NamedParameterSyntax np)
                names.Add(np.Identifier.Text);
    }

    private static void EmitUpvalueWarnings(
        FunctionDeclarationStatementSyntax funcDecl,
        string functionName,
        Dictionary<string, LocalInfo> fileLevelLocals,
        HashSet<string> ownLocals,
        List<LspDiagnostic> diagnostics,
        string? documentUri)
    {
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var funcLine = funcDecl.GetLocation().GetLineSpan().StartLinePosition.Line;

        foreach (var id in funcDecl.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = id.Name;
            if (ownLocals.Contains(name)) continue;
            if (!fileLevelLocals.TryGetValue(name, out var info)) continue;
            if (info.Suppressed) continue;
            if (!warned.Add(name)) continue;

            var span = id.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            Container<DiagnosticRelatedInformation>? relatedInfo = null;
            if (documentUri is not null)
            {
                var docUri = DocumentUri.From(documentUri);
                relatedInfo = new Container<DiagnosticRelatedInformation>(
                    new DiagnosticRelatedInformation
                    {
                        Location = new Location
                        {
                            Uri = docUri,
                            Range = new LspRange(new LspPosition(info.Line, 0), new LspPosition(info.Line, 0))
                        },
                        Message = "local declaration"
                    },
                    new DiagnosticRelatedInformation
                    {
                        Location = new Location
                        {
                            Uri = docUri,
                            Range = new LspRange(new LspPosition(funcLine, 0), new LspPosition(funcLine, 0))
                        },
                        Message = "function declaration"
                    });
            }

            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(LuaDiagnosticCodes.EngineUpvalue),
                Severity = LspDiagnosticSeverity.Warning,
                Message = $"File-level local '{name}' is captured as an upvalue by '{functionName}'. " +
                          "This persists across save/load cycles and may corrupt save games. " +
                          "Move the variable inside the function, or use a global.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character),
                    new LspPosition(end.Line, end.Character)),
                RelatedInformation = relatedInfo,
                Source = AppProperties.LspServerId
            });
        }
    }

    private readonly record struct LocalInfo(bool Suppressed, int Line);
}