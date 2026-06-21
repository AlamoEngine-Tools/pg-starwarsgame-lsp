// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Parsing;

public sealed class LuaGameDocumentParser : IGameDocumentParser
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaGameDocumentParser> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    public LuaGameDocumentParser(
        ILuaApiSchemaProvider schemaProvider,
        IFileHelper fileHelper,
        ILogger<LuaGameDocumentParser> logger)
    {
        _schemaProvider = schemaProvider;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".lua", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        var tree = LuaSyntaxTree.ParseText(text, s_parseOptions, canonicalUri);
        var root = tree.GetRoot(ct);

        var symbols = CollectSymbols(root, canonicalUri);
        var references = CollectReferences(root, canonicalUri, tree);
        var requireArgs = CollectRequireArgs(root);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray(),
            requireArgs));
    }

    private List<GameSymbol> CollectSymbols(SyntaxNode root, string documentUri)
    {
        var symbols = new List<GameSymbol>();

        foreach (var node in root.DescendantNodes())
        {
            if (node is not FunctionDeclarationStatementSyntax funcDecl)
                continue;

            // Only simple top-level names become global symbols.
            // Member names (A.B) and method names (A:B) are module-scoped, not globals.
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName)
                continue;

            var id = simpleName.Name.Text;
            if (string.IsNullOrEmpty(id))
                continue;

            var position = simpleName.Name.GetLocation().GetLineSpan().StartLinePosition;
            symbols.Add(new GameSymbol(
                id,
                GameSymbolKind.LuaGlobal,
                null,
                new FileOrigin(documentUri, position.Line, position.Character),
                ExtractDocComment(funcDecl)));
        }

        return symbols;
    }

    // Walks the leading trivia of the function declaration backwards to collect
    // the `---`-prefixed doc comment block immediately above it. Stops at blank
    // lines or non-comment trivia. Annotation lines (---@...) are skipped so
    // only prose description text is returned.
    private static string? ExtractDocComment(FunctionDeclarationStatementSyntax funcDecl)
    {
        var trivia = funcDecl.GetFirstToken().LeadingTrivia;
        var lines = new List<string>();
        var seenEol = false;

        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var text = trivia[i].ToFullString();

            // End-of-line trivia: one EOL is normal inter-line spacing; two in a row = blank line.
            if (text is "\n" or "\r\n" or "\r")
            {
                if (seenEol) break;
                seenEol = true;
                continue;
            }

            // Horizontal whitespace (indentation) — skip without affecting EOL tracking.
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.TrimStart();

            // Must start with `---` to be a doc comment; bare `--` comments stop collection.
            if (!trimmed.StartsWith("---", StringComparison.Ordinal))
                break;

            // Annotation lines (---@...) are not prose — skip but continue scanning upward.
            if (trimmed.StartsWith("---@", StringComparison.Ordinal))
            {
                seenEol = false;
                continue;
            }

            // Strip the `--- ` prefix and accumulate.
            var content = trimmed[3..].TrimStart(' ', '\t');
            if (content.Length > 0)
                lines.Add(content);
            seenEol = false;
        }

        if (lines.Count == 0) return null;
        lines.Reverse();
        return string.Join("\n", lines);
    }

    private List<GameReference> CollectReferences(
        SyntaxNode root, string documentUri, SyntaxTree tree)
    {
        var references = new List<GameReference>();

        foreach (var node in root.DescendantNodes())
        {
            if (node is not FunctionCallExpressionSyntax call)
                continue;

            // Callee must be a simple identifier (global function call).
            if (call.Expression is not IdentifierNameSyntax callee)
                continue;

            var functionName = callee.Name;

            // require() is tracked separately in CollectRequireArgs.
            if (string.Equals(functionName, "require", StringComparison.OrdinalIgnoreCase))
                continue;

            var entries = _schemaProvider.GetXmlRefs(functionName);
            if (entries.Count > 0)
            {
                // Known EaW API function — emit XML object refs for the relevant arguments.
                foreach (var entry in entries)
                {
                    if (TryExtractStringArgument(call, entry.ParamIndex) is not { } value)
                        continue;

                    if (TryGetArgumentLocation(call, entry.ParamIndex, tree) is not { } loc)
                        continue;

                    references.Add(new GameReference(
                        value,
                        GameSymbolKind.XmlObject,
                        entry.ExpectedTypeName,
                        documentUri,
                        loc.Line,
                        loc.Column,
                        value.Length));
                }
            }
            else
            {
                // User-defined or unknown function — track the call site as a LuaGlobal
                // reference so rename can locate all callers via the index (O(1) lookup).
                var calleeSpan = callee.GetLocation().GetLineSpan().StartLinePosition;
                references.Add(new GameReference(
                    functionName,
                    GameSymbolKind.LuaGlobal,
                    null,
                    documentUri,
                    calleeSpan.Line,
                    calleeSpan.Character,
                    functionName.Length));
            }
        }

        return references;
    }

    private static string? TryExtractStringArgument(FunctionCallExpressionSyntax call, int paramIndex)
    {
        switch (call.Argument)
        {
            case ExpressionListFunctionArgumentSyntax exprList:
            {
                var args = exprList.Expressions;
                if (paramIndex >= args.Count)
                    return null;
                return args[paramIndex] is LiteralExpressionSyntax lit
                       && lit.IsKind(SyntaxKind.StringLiteralExpression)
                    ? lit.Token.ValueText
                    : null;
            }
            case StringFunctionArgumentSyntax strArg when paramIndex == 0:
                return strArg.Expression.Token.ValueText;
            default:
                return null;
        }
    }

    private static (int Line, int Column)? TryGetArgumentLocation(
        FunctionCallExpressionSyntax call, int paramIndex, SyntaxTree tree)
    {
        LiteralExpressionSyntax? lit = null;

        switch (call.Argument)
        {
            case ExpressionListFunctionArgumentSyntax exprList:
            {
                var args = exprList.Expressions;
                if (paramIndex < args.Count && args[paramIndex] is LiteralExpressionSyntax l)
                    lit = l;
                break;
            }
            case StringFunctionArgumentSyntax strArg when paramIndex == 0:
            {
                // String shorthand: func "value" — token position after the opening quote
                var span = strArg.Expression.Token.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line;
                // Column points to the character after the opening quote
                var startCol = span.StartLinePosition.Character + 1;
                return (startLine, startCol);
            }
        }

        if (lit is null)
            return null;

        var lineSpan = lit.Token.GetLocation().GetLineSpan();
        return (
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character + 1); // +1 to skip opening quote
    }

    private static ImmutableArray<string> CollectRequireArgs(SyntaxNode root)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;
            var arg = ExtractRequireStringArg(call);
            if (arg is null) continue;
            builder.Add(arg);
        }

        return builder.ToImmutable();
    }

    private static string? ExtractRequireStringArg(FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
            return strArg.Expression.Token.ValueText;

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
            return lit.Token.ValueText;

        return null;
    }
}