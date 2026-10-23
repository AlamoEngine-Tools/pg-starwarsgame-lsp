// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeLens;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeLensHandler : CodeLensHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlCodeLensHandler> _logger;
    private readonly IXmlCodeLensRegistry _registry;

    public XmlCodeLensHandler(
        IGameIndexService indexService,
        ILogger<XmlCodeLensHandler> logger,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        IXmlCodeLensRegistry registry,
        ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _registry = registry;
        _config = config;
    }

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<CodeLensContainer?>(null);

        var index = _indexService.Current;
        // A dynamic enum value's definition is no symbol, and a file defining only values - an enum file
        // under Data/XML/Enum - may hold none.
        var enumLenses = EnumValueLenses(uri, index).ToList();
        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(enumLenses));

        var lenses = new List<LspCodeLens>();
        foreach (var symbol in docIndex.Symbols)
        {
            if (symbol.Origin is not FileOrigin fo)
                continue;

            var ctx = new CodeLensSymbolContext(symbol, fo, index) { Messages = new ConfiguredMessageCatalog(_config) };
            foreach (var lens in _registry.Dispatch(ctx))
            {
                _logger.LogDebug("CodeLens: {Id} at line {Line}", symbol.Id, fo.Line);
                lenses.Add(lens);
            }
        }

        // Grouping-key tags (Campaign_Set / SFXEvent Overlap_Test) are not symbols, so the registry
        // never sees them. Surface a lens on each group tag stating how many members share the value,
        // clickable to peek them - making the otherwise-invisible grouping obvious in the editor.
        foreach (var lens in GroupMembershipLenses(uri, docIndex, index))
            lenses.Add(lens);
        lenses.AddRange(enumLenses);

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    // One lens per dynamic enum value this document defines - in an enum file, or a GameConstants list -
    // counting its uses: references to enum:<Enum>/<Value>, as the parser records them and rename edits them.
    private IEnumerable<LspCodeLens> EnumValueLenses(string uri, GameIndex index)
    {
        var messages = new ConfiguredMessageCatalog(_config);
        foreach (var (enumName, values) in index.WorkspaceEnumValueDefinitions)
        foreach (var (value, origin) in values)
        {
            if (!origin.IsNavigable || _fileHelper.NormalizeUri(origin.Uri) != uri) continue;
            var refs = index.WorkspaceReferences.TryGetValue($"enum:{enumName}/{value}", out var found)
                ? found
                : [];
            yield return ReferencesCodeLensProvider.For(uri, origin.Line, origin.Column ?? 0, refs, messages);
        }
    }

    // One lens per group-key tag occurrence in this document: "{n} {MemberType}s in this group",
    // clickable to peek the co-members. Members are resolved workspace-wide (baseline ∪ workspace)
    // but only navigable origins are offered as peek targets.
    private IEnumerable<LspCodeLens> GroupMembershipLenses(
        string uri, DocumentIndex docIndex, GameIndex index)
    {
        if (docIndex.GroupMemberships.IsDefaultOrEmpty) yield break;

        foreach (var gm in docIndex.GroupMemberships)
        {
            if (!index.AllGroupMemberships.TryGetValue(gm.Membership.GroupKey, out var members))
                continue;

            var targets = members
                .Where(m => m.MemberOrigin is FileOrigin { IsNavigable: true })
                .Select(m => (FileOrigin)m.MemberOrigin)
                .ToList();
            if (targets.Count == 0) continue;

            var messages = new ConfiguredMessageCatalog(_config);
            var member = gm.Membership.MemberTypeName ?? messages.Format("XmlCodeLens_Member");
            var title = messages.Format("XmlCodeLens_GroupMembers", member, targets.Count);

            var locations = targets.Select(fo => new
            {
                uri = fo.Uri,
                range = new
                {
                    start = new { line = fo.Line, character = fo.Column ?? 0 },
                    end = new { line = fo.Line, character = fo.Column ?? 0 }
                }
            });

            yield return new LspCodeLens
            {
                Range = new LspRange(
                    new Position(gm.TagLine, gm.TagColumn),
                    new Position(gm.TagLine, gm.TagColumn + gm.TagLength)),
                Command = new Command
                {
                    Title = title,
                    Name = "aet-eaw-edit.lsp.showReferences",
                    Arguments = JArray.FromObject(new object[]
                    {
                        uri,
                        new { line = gm.TagLine, character = gm.TagColumn },
                        locations.ToArray()
                    })
                }
            };
        }
    }

    public override Task<LspCodeLens> Handle(LspCodeLens request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(
        CodeLensCapability capability, ClientCapabilities clientCapabilities)
    {
        // Every XML lens arrives with its command, so none is ever resolved. Declared anyway: a
        // static client gets ONE code-lens capability for both languages, and which handler's
        // options it carries is not fixed (measured: the E2E suite saw both). The Lua analyzer's
        // lenses need resolve (#155).
        return new CodeLensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            ResolveProvider = true
        };
    }
}