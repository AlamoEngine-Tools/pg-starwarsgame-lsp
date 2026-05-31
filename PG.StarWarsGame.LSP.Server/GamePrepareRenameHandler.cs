// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Util;
using PG.StarWarsGame.LSP.Xml;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server;

public sealed class GamePrepareRenameHandler : PrepareRenameHandlerBase
{
    private static readonly LuaParseOptions s_luaParseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<GamePrepareRenameHandler> _logger;
    private readonly IGameWorkspaceHost _workspaceHost;

    public GamePrepareRenameHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IEaWXmlContext eaWXmlContext,
        IFileHelper fileHelper,
        ILogger<GamePrepareRenameHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;

        if (uri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(PrepareXml(uri, request.Position.Line, request.Position.Character, index));
        if (uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(PrepareLua(uri, request.Position.Line, request.Position.Character, index));

        return Task.FromResult<RangeOrPlaceholderRange?>(null);
    }

    // ── XML path ───────────────────────────────────────────────────────────────

    private RangeOrPlaceholderRange? PrepareXml(string uri, int line, int character, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return null;

        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return null;

        var hit = XmlPositionResolver.FindAtPosition(docIndex, line, character);
        if (hit is null)
            return null;

        if (IsBlockedByArchiveOrigin(hit.Value.Id, index))
            return null;

        return new RangeOrPlaceholderRange(hit.Value.Range);
    }

    // ── Lua path ───────────────────────────────────────────────────────────────

    private RangeOrPlaceholderRange? PrepareLua(string uri, int line, int character, GameIndex index)
    {
        // LuaGlobal path — requires document in the index.
        if (index.Documents.TryGetValue(uri, out var docIndex))
        {
            var hit = LuaPositionResolver.FindAtPosition(docIndex, line, character);
            if (hit is not null)
            {
                if (IsBlockedByArchiveOrigin(hit.Value.Id, index))
                    return null;

                var range = hit.Value.Range;
                // Zero-length means cursor is on a declaration; extend by the symbol name length.
                if (range.Start == range.End)
                    range = new LspRange(range.Start,
                        new Position(range.Start.Line, range.Start.Character + hit.Value.Id.Length));

                return new RangeOrPlaceholderRange(range);
            }
        }

        // XmlObject string literal path — requires document text.
        var text = GetText(uri);
        if (text is null)
            return null;

        return FindXmlObjectStringRange(text, line, character, index);
    }

    // ── XmlObject string literal detection ────────────────────────────────────

    private RangeOrPlaceholderRange? FindXmlObjectStringRange(
        string text, int line, int character, GameIndex index)
    {
        var tree = LuaSyntaxTree.ParseText(text, s_luaParseOptions);
        var root = tree.GetRoot();

        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;

            var loc = lit.GetLocation().GetLineSpan();
            var startLine = loc.StartLinePosition.Line;
            var startChar = loc.StartLinePosition.Character;

            if (startLine != line) continue;

            var value = lit.Token.ValueText;
            var innerStart = startChar + 1;
            var innerEnd = innerStart + value.Length;

            if (character < innerStart || character > innerEnd) continue;

            if (!IsKnownXmlObject(value, index)) continue;
            if (IsBlockedByArchiveOrigin(value, index)) return null;

            return new RangeOrPlaceholderRange(new LspRange(
                new Position(line, innerStart),
                new Position(line, innerEnd)));
        }

        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static bool IsKnownXmlObject(string name, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(name, out var defs) &&
               defs.Any(s => s.Kind == GameSymbolKind.XmlObject);
    }

    private static bool IsBlockedByArchiveOrigin(string id, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(id, out var defs) &&
               defs.Any(s => s.Origin is not FileOrigin);
    }

    private string? GetText(string uri)
    {
        if (_workspaceHost.TryGet(uri, out var doc))
            return doc.Text;
        var path = _fileHelper.FileUriToPath(uri);
        if (path is null) return null;
        try
        {
            return _fileHelper.FileSystem.File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Language = "xml" },
                new TextDocumentFilter { Language = "lua" }),
            PrepareProvider = true
        };
    }
}