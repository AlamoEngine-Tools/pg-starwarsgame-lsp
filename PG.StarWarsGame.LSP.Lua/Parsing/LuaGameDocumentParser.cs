// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Parsing;

public sealed class LuaGameDocumentParser : IGameDocumentParser, ICacheStatisticsSource
{
    private readonly ILuaAnnotationRepository _annotationRepository;

    // documentUri -> the annotation contribution of its most recent parse, ready to persist.
    // Concurrent because the workspace scan parses in parallel.
    private readonly ConcurrentDictionary<string, byte[]> _lastState = new(DocumentUris.Comparer);
    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LuaGameDocumentParser> _logger;
    private readonly ILuaParseCache? _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    // parseCache is optional so minimal test setups can omit it; production wires the shared
    // cache so the indexing parse seeds it - the diagnostics publish and the first request after
    // an edit then reuse this parse instead of re-parsing. A null configProvider (test
    // convenience) means every feature flag reads as enabled.
    public LuaGameDocumentParser(
        ILuaApiSchemaProvider schemaProvider,
        IFileHelper fileHelper,
        ILogger<LuaGameDocumentParser> logger,
        ILuaAnnotationRepository annotationRepository,
        ILuaParseCache? parseCache = null,
        ILspConfigurationProvider? configProvider = null)
    {
        _schemaProvider = schemaProvider;
        _fileHelper = fileHelper;
        _logger = logger;
        _annotationRepository = annotationRepository;
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

        var (symbols, annotations, functionAnnotations) = CollectSymbols(root, canonicalUri);
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

        _annotationRepository.Update(canonicalUri, [.. annotations]);
        _annotationRepository.UpdateFunctionAnnotations(canonicalUri, functionAnnotations);
        // Kept so a snapshot written after this scan can carry what the repository was told. The
        // repository stores the annotations but not the function PAIRING, and re-deriving that
        // would mean parsing again - which is exactly what the snapshot exists to avoid.
        _lastState[canonicalUri] = LuaAnnotationStateCodec.Serialize([.. annotations], functionAnnotations);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            [.. symbols],
            references.ToImmutableArray(),
            requireArgs));
    }

    /// <inheritdoc />
    /// <summary>One serialized annotation state per file ever parsed; exact bytes.</summary>
    public CacheStatistics Snapshot()
    {
        long bytes = 0;
        foreach (var state in _lastState.Values)
            bytes += state.Length;
        return new CacheStatistics("lua-parser-state", _lastState.Count, bytes);
    }

    public byte[]? CaptureParserState(string documentUri)
    {
        return _lastState.GetValueOrDefault(_fileHelper.NormalizeUri(documentUri));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Replays exactly what <see cref="ParseAsync" /> told the repository. Without this, a Lua
    ///     document served from a cached index contributes NO annotations for the whole session -
    ///     and on a layered mod most indexed files are Lua, so nearly every annotation in the
    ///     workspace went missing on a warm start.
    /// </remarks>
    public void RestoreParserState(string documentUri, byte[] state)
    {
        var restored = LuaAnnotationStateCodec.Deserialize(state);
        if (restored is not { } value)
        {
            // Bytes from another build, or truncated. The session loses this file's annotations,
            // which is what would have happened anyway; failing the scan would be far worse.
            _logger.LogDebug("Unreadable cached Lua annotations for '{Uri}'; skipping", documentUri);
            return;
        }

        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        _annotationRepository.Update(canonicalUri, value.Annotations);
        _annotationRepository.UpdateFunctionAnnotations(canonicalUri, value.Functions);
        _lastState[canonicalUri] = state;
    }

    private (List<GameSymbol> Symbols, List<EmmyLuaAnnotations> Annotations,
        List<(string Name, EmmyLuaAnnotations Ann)> FunctionAnnotations) CollectSymbols(
            SyntaxNode root, string documentUri)
    {
        var symbols = new List<GameSymbol>();
        var annotations = new List<EmmyLuaAnnotations>();
        var functionAnnotations = new List<(string Name, EmmyLuaAnnotations Ann)>();

        foreach (var node in root.DescendantNodes())
            if (node is FunctionDeclarationStatementSyntax funcDecl)
            {
                if (funcDecl.Name is SimpleFunctionNameSyntax simpleName)
                {
                    // Simple global function: Foo() - becomes a workspace symbol and a named annotation.
                    var id = simpleName.Name.Text;
                    if (string.IsNullOrEmpty(id))
                        continue;

                    var ann = ExtractAnnotations(funcDecl);
                    annotations.Add(ann);
                    functionAnnotations.Add((id, ann));

                    var position = simpleName.Name.GetLocation().GetLineSpan().StartLinePosition;
                    symbols.Add(new GameSymbol(
                        id,
                        GameSymbolKind.LuaGlobal,
                        null,
                        new FileOrigin(documentUri, position.Line, position.Character),
                        ann.Description));
                }
                else if (funcDecl.Name is MemberFunctionNameSyntax memberName)
                {
                    // Obj.Foo() - not a global symbol; index annotation by simple name for hover.
                    var id = memberName.Name.Text;
                    if (!string.IsNullOrEmpty(id))
                        functionAnnotations.Add((id, ExtractAnnotations(funcDecl)));
                }
                else if (funcDecl.Name is MethodFunctionNameSyntax methodName)
                {
                    // Obj:Foo() - not a global symbol; index annotation by simple name for hover.
                    var id = methodName.Name.Text;
                    if (!string.IsNullOrEmpty(id))
                        functionAnnotations.Add((id, ExtractAnnotations(funcDecl)));
                }
            }
            else if (node is LocalFunctionDeclarationStatementSyntax localFunc)
            {
                // local function Foo() - not a global symbol; index annotation by name for hover.
                var id = localFunc.Name.Name;
                if (!string.IsNullOrEmpty(id))
                    functionAnnotations.Add((id, ExtractAnnotations(localFunc)));
            }
            else if (node is StatementSyntax stmt)
            {
                // Scan non-function statements for @class / @alias / @enum doc blocks.
                // These drive the workspace type index and appear in .d.lua declaration files
                // and in regular .lua files that define user-facing types.
                var ann = ExtractAnnotations(stmt);
                if (ann.ClassDef is not null || ann.AliasDef is not null || ann.EnumDef is not null)
                    annotations.Add(ann);
            }

        // A plan's task forces are globals the engine creates from the TaskForce table, so each
        // name is a symbol defined at the string that declares it.
        foreach (var (name, token) in LuaTaskForceTable.Forces(root))
        {
            var position = token.GetLocation().GetLineSpan().StartLinePosition;
            symbols.Add(new GameSymbol(
                name,
                GameSymbolKind.LuaGlobal,
                null,
                new FileOrigin(documentUri, position.Line, position.Character + 1), // after the quote
                null));
        }

        return (symbols, annotations, functionAnnotations);
    }

    private static EmmyLuaAnnotations ExtractAnnotations(SyntaxNode node)
    {
        var lines = CollectDocCommentLines(node);
        return lines.Count == 0 ? EmmyLuaAnnotations.Empty : EmmyLuaAnnotationParser.Parse(lines);
    }

    private static IReadOnlyList<string> CollectDocCommentLines(SyntaxNode node)
    {
        return LuaDocCommentScanner.CollectLeadingDocLines(node);
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