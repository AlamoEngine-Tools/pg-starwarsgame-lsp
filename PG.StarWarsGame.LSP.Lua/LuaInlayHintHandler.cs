// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaInlayHintHandler : InlayHintsHandlerBase
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly ILuaAnnotationRepository _annotationRepository;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaInlayHintHandler> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaInlayHintHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILuaAnnotationRepository annotationRepository,
        ILogger<LuaInlayHintHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _annotationRepository = annotationRepository;
        _logger = logger;
    }

    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<InlayHintContainer?>(null);

        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<InlayHintContainer?>(null);

        var range = request.Range;
        var index = _indexService.Current;
        var tree = LuaSyntaxTree.ParseText(doc.Text, s_parseOptions);
        var root = tree.GetRoot();
        var hints = new List<InlayHint>();

        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax funcName) continue;
            if (call.Argument is not ExpressionListFunctionArgumentSyntax exprList) continue;

            var args = exprList.Expressions;
            if (args.Count == 0) continue;

            IReadOnlyList<LuaParamAnnotation> @params;
            if (_schemaProvider.AllFunctionNames.Contains(funcName.Name))
            {
                @params = _schemaProvider.GetFunctionParams(funcName.Name);
            }
            else if (index.WorkspaceDefinitions.ContainsKey(funcName.Name))
            {
                @params = _annotationRepository.GetFunctionAnnotation(funcName.Name)?.Params
                          ?? (IReadOnlyList<LuaParamAnnotation>)[];
                if (@params is System.Collections.Immutable.ImmutableArray<LuaParamAnnotation> arr
                    && arr.IsDefaultOrEmpty)
                    @params = [];
            }
            else
            {
                continue;
            }

            if (@params.Count == 0) continue;

            var limit = Math.Min(args.Count, @params.Count);
            for (var i = 0; i < limit; i++)
            {
                var arg = args[i];
                var param = @params[i];

                var argSpan = arg.GetLocation().GetLineSpan();
                var argLine = argSpan.StartLinePosition.Line;
                var argChar = argSpan.StartLinePosition.Character;

                if (argLine < range.Start.Line || argLine > range.End.Line) continue;

                var argText = ExtractArgumentText(arg);
                if (!ShouldShowHint(param.Name, argText)) continue;

                _logger.LogDebug("InlayHint at {Line}:{Char} for param {Param}", argLine, argChar, param.Name);
                hints.Add(new InlayHint
                {
                    Position = new Position(argLine, argChar),
                    Label = ((StringOrInlayHintLabelParts?)(param.Name + ": "))!,
                    Kind = InlayHintKind.Parameter,
                    PaddingRight = false
                });
            }
        }

        return Task.FromResult<InlayHintContainer?>(new InlayHintContainer(hints));
    }

    public override Task<InlayHint> Handle(InlayHint request, CancellationToken ct)
        => Task.FromResult(request);

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
    {
        return new InlayHintRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            ResolveProvider = false
        };
    }

    private static string? ExtractArgumentText(ExpressionSyntax arg) => arg switch
    {
        IdentifierNameSyntax id => id.Name,
        LiteralExpressionSyntax lit => lit.Token.ValueText,
        _ => null
    };

    private static bool ShouldShowHint(string paramName, string? argText)
    {
        if (string.IsNullOrEmpty(argText)) return true;
        return argText.Length < 3
            ? !paramName.Contains(argText, StringComparison.OrdinalIgnoreCase)
            : !argText.Contains(paramName, StringComparison.OrdinalIgnoreCase);
    }
}
