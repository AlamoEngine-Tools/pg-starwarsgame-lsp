// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Parsing;

public sealed class LuaGameDocumentParser : IGameDocumentParser
{
    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly ILuaParseCache? _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    // parseCache is optional so minimal test setups can omit it; production wires the shared
    // cache so the indexing parse seeds it - the diagnostics publish and the first request after
    // an edit then reuse this parse instead of re-parsing. A null configProvider (test
    // convenience) means every feature flag reads as enabled.
    public LuaGameDocumentParser(
        ILuaApiSchemaProvider schemaProvider,
        IFileHelper fileHelper,
        ILuaParseCache? parseCache = null,
        ILspConfigurationProvider? configProvider = null)
    {
        _schemaProvider = schemaProvider;
        _fileHelper = fileHelper;
        _parseCache = parseCache;
        _configProvider = configProvider;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".lua", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        var parsed = _parseCache?.GetOrParse(canonicalUri, text) ?? ParsedLuaDocument.Parse(text, canonicalUri);
        var tree = parsed.Tree;
        var root = tree.GetRoot(ct);

        var symbols = CollectSymbols(root, canonicalUri);
        var references = CollectReferences(root, canonicalUri);
        var requireArgs = CollectRequireArgs(root);

        // Index the script as a navigable file-symbol keyed by its extensionless name, so a
        // manifest <Lua_Script> (a workspaceFile reference) resolves to it for go-to / rename.
        symbols.Add(new GameSymbol(
            WorkspaceFileKey.Create(WorkspaceFileKey.LuaScriptType, canonicalUri),
            GameSymbolKind.WorkspaceFile,
            WorkspaceFileKey.LuaScriptType,
            new FileOrigin(canonicalUri, 0, 0),
            null));

        if (_configProvider?.Current.Features.Story.Symbols ?? true)
            LuaStorySymbolCollector.Collect(root, canonicalUri, symbols, references);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            [.. symbols],
            references.ToImmutableArray(),
            requireArgs));
    }

    private static List<GameSymbol> CollectSymbols(SyntaxNode root, string documentUri)
    {
        var symbols = new List<GameSymbol>();

        // A global function is a workspace symbol, its doc comment's prose the description. What the
        // rest of the comment declares is the analyzer's to read (#155).
        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            var id = simpleName.Name.Text;
            if (string.IsNullOrEmpty(id)) continue;

            var lines = LuaDocCommentScanner.CollectLeadingDocLines(funcDecl);
            var description = lines.Count == 0 ? null : EmmyLuaAnnotationParser.Parse(lines).Description;
            var position = simpleName.Name.GetLocation().GetLineSpan().StartLinePosition;
            symbols.Add(new GameSymbol(
                id,
                GameSymbolKind.LuaGlobal,
                null,
                new FileOrigin(documentUri, position.Line, position.Character),
                description));
        }

        // A plan's task forces are globals the engine creates from the TaskForce table, each a
        // TaskForce wrapper, so each name is a symbol defined at the string that declares it.
        foreach (var (name, token) in LuaTaskForceTable.Forces(root))
        {
            var position = token.GetLocation().GetLineSpan().StartLinePosition;
            symbols.Add(new GameSymbol(
                name,
                GameSymbolKind.LuaGlobal,
                LuaTaskForceTable.WrapperTypeName,
                new FileOrigin(documentUri, position.Line, position.Character + 1), // after the quote
                null));
        }

        return symbols;
    }

    private List<GameReference> CollectReferences(SyntaxNode root, string documentUri)
    {
        var references = new List<GameReference>();

        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case FunctionCallExpressionSyntax { Expression: IdentifierNameSyntax callee } call:
                {
                    var functionName = callee.Name;

                    // require() is tracked separately in CollectRequireArgs.
                    if (string.Equals(functionName, "require", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var entries = _schemaProvider.GetXmlRefs(functionName);
                    if (entries.Count > 0)
                    {
                        AddTaggedArguments(entries, call.Argument, documentUri, references);
                    }
                    else
                    {
                        // User-defined or unknown function - track the call site as a LuaGlobal
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

                    break;
                }
                // obj.Method("x") and obj:Method("x"): the receiver's type is not inferred here, so
                // the method name alone selects the tags. An untagged member call is no reference at
                // all - it is not a global function call site either.
                case FunctionCallExpressionSyntax { Expression: MemberAccessExpressionSyntax member } call:
                    AddTaggedArguments(_schemaProvider.GetXmlRefs(member.MemberName.Text), call.Argument,
                        documentUri, references);
                    break;
                case MethodCallExpressionSyntax method:
                    AddTaggedArguments(_schemaProvider.GetXmlRefs(method.Identifier.Text), method.Argument,
                        documentUri, references);
                    break;
            }

        return references;
    }

    private static void AddTaggedArguments(
        IReadOnlyList<XmlRefEntry> entries, FunctionArgumentSyntax argument, string documentUri,
        List<GameReference> references)
    {
        foreach (var entry in entries)
        {
            // A kind the index holds no symbol for (an enum value, a bone) is completion data for
            // the analyzer side, never a reference to resolve.
            if (GameSymbolKinds.FromReferenceKind(entry.Kind) is not { } kind)
                continue;

            if (TryExtractStringArgument(argument, entry.ParamIndex) is not { } value)
                continue;

            // A value the parameter's type names itself (Find_Player("local")) names no object.
            if (entry.Literals?.Contains(value, StringComparer.Ordinal) == true)
                continue;

            if (TryGetArgumentLocation(argument, entry.ParamIndex) is not { } loc)
                continue;

            references.Add(new GameReference(
                value,
                kind,
                entry.ExpectedTypeName,
                documentUri,
                loc.Line,
                loc.Column,
                value.Length));
        }
    }

    private static string? TryExtractStringArgument(FunctionArgumentSyntax argument, int paramIndex)
    {
        switch (argument)
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
        FunctionArgumentSyntax argument, int paramIndex)
    {
        LiteralExpressionSyntax? lit = null;

        switch (argument)
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
                // String shorthand: func "value" - token position after the opening quote
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