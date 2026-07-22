// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Go-to-definition for object-reference arguments in story-dialog scripts: DIALOG speech
///     events, MOVIE/MOVIE_ONCE movies, SFX sound events jump to their defining XML. Localisation
///     keys deliberately don't navigate — the localisation index is membership-only (no recorded
///     file/line), matching the XML side. Scope-gated like the dialog diagnostics.
/// </summary>
public sealed class DialogDefinitionHandler : DefinitionHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly DialogFactProducer _factProducer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<DialogDefinitionHandler> _logger;
    private readonly IStoryDialogScope _scope;
    private readonly IDocumentTextSource _textSource;

    public DialogDefinitionHandler(
        IGameIndexService indexService,
        IStoryDialogScope scope,
        DialogFactProducer factProducer,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        ILogger<DialogDefinitionHandler> logger,
        ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _scope = scope;
        _factProducer = factProducer;
        _fileHelper = fileHelper;
        _textSource = textSource;
        _logger = logger;
        _config = config;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Dialog.GoToDefinition)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_scope.IsInScope(uri))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var text = _textSource.GetText(uri)?.Text;
        if (text is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var line = request.Position.Line;
        var character = request.Position.Character;
        var document = StoryDialogParser.Parse(text);

        foreach (var fact in _factProducer.Produce(document, uri))
        {
            ct.ThrowIfCancellationRequested();
            if (fact.Command.Line != line || fact.Def?.Params is null) continue;

            foreach (var param in fact.Def.Params)
            {
                if (param.ReferenceKind != ReferenceKind.XmlObject) continue;
                if (param.Position >= fact.Command.Args.Count) continue;

                var arg = fact.Command.Args[param.Position];
                if (character < arg.Column || character > arg.Column + arg.Text.Length) continue;

                var symbol = _indexService.Current.Resolve(arg.Text, param.ObjectType?.TypeName);
                if (symbol?.Origin is not FileOrigin origin)
                {
                    _logger.LogDebug("Dialog go-to-def: '{Id}' has no navigable origin", arg.Text);
                    return Task.FromResult<LocationOrLocationLinks?>(null);
                }

                var originSelectionRange = new LspRange(
                    new Position(fact.Command.Line, arg.Column),
                    new Position(fact.Command.Line, arg.Column + arg.Text.Length));
                _logger.LogDebug("Dialog go-to-def: '{Id}' → {Uri}:{Line}", arg.Text, origin.Uri, origin.Line);
                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(new LocationOrLocationLink(origin.ToLspLocationLink(originSelectionRange))));
            }
        }

        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.txt")
        };
    }
}