// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlRenameHandler : RenameHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlRenameHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        ILogger<XmlRenameHandler> logger,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<WorkspaceEdit?>(null);
        var index = _indexService.Current;

        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<WorkspaceEdit?>(null);

        var hit = XmlPositionResolver.FindAtPosition(docIndex, request.Position.Line, request.Position.Character);
        if (hit is null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var id = hit.Value.Id;
        var newName = request.NewName;

        // FileOrigin guardrail: refuse rename if any definition is not workspace-owned
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            if (defs.Any(s => s.Origin is not FileOrigin))
            {
                _logger.LogDebug("Rename blocked: {Id} has non-FileOrigin definition", id);
                return Task.FromResult<WorkspaceEdit?>(null);
            }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        // Reference edits — precise range from GameReference
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
            {
                var refEdit = new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                };
                AddEdit(changes, r.DocumentUri, refEdit);
            }

        // Definition edits — locate the name attribute value in the line text
        if (index.WorkspaceDefinitions.TryGetValue(id, out defs))
            foreach (var sym in defs)
            {
                if (sym.Origin is not FileOrigin fo) continue;
                var nameTag = sym.TypeName is not null
                    ? _schema.GetObjectType(sym.TypeName)?.NameTag
                    : null;
                if (nameTag is null) continue;

                var defRange = FindNameAttributeRange(fo.Uri, fo.Line, nameTag, id);
                if (defRange is null) continue;

                AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = defRange });
            }

        if (changes.Count == 0)
            return Task.FromResult<WorkspaceEdit?>(null);

        _logger.LogDebug("Rename {Id} → {NewName}: {Count} file(s)", id, newName, changes.Count);
        return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit
        {
            Changes = changes.ToDictionary(
                kvp => kvp.Key,
                kvp => (IEnumerable<TextEdit>)kvp.Value)
        });
    }

    private LspRange? FindNameAttributeRange(string uri, int line, string nameTag, string currentValue)
    {
        if (!_workspaceHost.TryGet(uri, out var doc))
            return null;

        var lines = doc.Text.Split('\n');
        if (line >= lines.Length) return null;

        var lineText = lines[line].TrimEnd('\r');

        // Search for nameTag="currentValue" or nameTag='currentValue'
        foreach (var quote in new[] { '"', '\'' })
        {
            var pattern = $"{nameTag}={quote}{currentValue}{quote}";
            var idx = lineText.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) continue;

            // Value starts after nameTag=quote
            var valueStart = idx + nameTag.Length + 2; // +2 for '=' and opening quote
            return new LspRange(
                new Position(line, valueStart),
                new Position(line, valueStart + currentValue.Length));
        }

        return null;
    }

    private static void AddEdit(Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}