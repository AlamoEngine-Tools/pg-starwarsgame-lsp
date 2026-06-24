// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDefinitionHandlerTest
{
    private const string TestUri = "file:///test.xml";
    private const string TargetUri = "file:///units.xml";

    private static DefinitionParams At(int line, int character, string uri = TestUri)
    {
        return new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Position = new Position(line, character)
        };
    }

    private static GameIndex IndexWith(
        DocumentIndex? callerDoc = null,
        GameSymbol? targetSymbol = null,
        DocumentIndex? targetDoc = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (callerDoc is not null)
            docs = docs.Add(callerDoc.DocumentUri, callerDoc);
        if (targetDoc is not null)
            docs = docs.Add(targetDoc.DocumentUri, targetDoc);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;
        if (targetSymbol is not null)
            defs = defs.Add(targetSymbol.Id, ImmutableArray.Create(targetSymbol));

        return new GameIndex(BaselineIndex.Empty, docs, defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static DocumentIndex DocWithRef(string uri, string refId, int line, int col, int len)
    {
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(new GameReference(refId, GameSymbolKind.XmlObject, "Unit", uri, line, col, len)));
    }

    private static GameSymbol SymbolAt(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, line, null), null);
    }

    private static GameSymbol SymbolInArchive(string id)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new MegArchiveOrigin("data.meg", "units.xml", 0, 0),
            null);
    }

    private static XmlDefinitionHandler BuildHandler(GameIndex index, IEaWXmlContext? ctx = null)
    {
        var indexService = new FakeIndexService { Current = index };
        var fileHelper = new FileHelper(new MockFileSystem());
        return new XmlDefinitionHandler(indexService, fileHelper, NullLogger<XmlDefinitionHandler>.Instance,
            ctx ?? new AllowAllEaWContext());
    }

    // ── null / miss cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoDocumentInIndex_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty);
        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_NoCursorHit_ReturnsNull()
    {
        var doc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
        var index = IndexWith(doc);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_UnresolvedReference_ReturnsNull()
    {
        var doc = DocWithRef(TestUri, "UNIT_MISSING", 0, 4, 12);
        var index = IndexWith(doc); // no definition for UNIT_MISSING
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SymbolInMegArchive_ReturnsNull()
    {
        var doc = DocWithRef(TestUri, "UNIT_VANILLA", 0, 4, 12);
        var symbol = SymbolInArchive("UNIT_VANILLA");
        var index = IndexWith(doc, symbol);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SymbolWithUnknownOrigin_ReturnsNull()
    {
        var doc = DocWithRef(TestUri, "UNIT_X", 0, 4, 6);
        var symbol = new GameSymbol("UNIT_X", GameSymbolKind.XmlObject, "Unit",
            new UnknownOrigin("test"), null);
        var index = IndexWith(doc, symbol);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SymbolWithGameRelativeFileOrigin_ReturnsNull()
    {
        // Baseline symbols carry a game-relative path (e.g. DATA\XML\…), not a file:// URI; the editor
        // cannot open it, so go-to must return null rather than navigate to a nonexistent file.
        var doc = DocWithRef(TestUri, "SFX_X", 0, 4, 5);
        var symbol = new GameSymbol("SFX_X", GameSymbolKind.Asset, "SFXEvent",
            new FileOrigin("DATA\\XML\\SFXEVENTSUNITSGROUND.XML", 0, 0), null);
        var index = IndexWith(doc, symbol);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    // ── successful navigation ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnReference_ReturnsDefinitionLocation()
    {
        var doc = DocWithRef(TestUri, "UNIT_REBEL", 0, 4, 10);
        var symbol = SymbolAt("UNIT_REBEL", TargetUri, 5);
        var index = IndexWith(doc, symbol);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 7), CancellationToken.None);

        Assert.NotNull(result);
        var link = Assert.Single(result!);
        Assert.True(link.IsLocation);
        Assert.Equal(TargetUri, link.Location!.Uri.ToString());
        Assert.Equal(5, link.Location.Range.Start.Line);
    }

    [Fact]
    public async Task Handle_CursorOnDefinitionItself_ReturnsSelfLocation()
    {
        var defDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray.Create(SymbolAt("UNIT_A", TestUri, 3)),
            ImmutableArray<GameReference>.Empty);
        var index = IndexWith(defDoc, SymbolAt("UNIT_A", TestUri, 3));
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(3, 0), CancellationToken.None);

        Assert.NotNull(result);
        var link = Assert.Single(result!);
        Assert.True(link.IsLocation);
        Assert.Equal(TestUri, link.Location!.Uri.ToString());
        Assert.Equal(3, link.Location.Range.Start.Line);
    }

    // ── URI normalization ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MixedCaseUri_NormalizesBeforeIndexLookup()
    {
        // Index keys are lowercase (canonical). LSP client on Windows may send uppercase drive letter.
        const string lowercaseUri = "file:///d:/units.xml";
        const string mixedCaseUri = "file:///D:/units.xml";

        var doc = DocWithRef(lowercaseUri, "UNIT_A", 0, 4, 6);
        var sym = SymbolAt("UNIT_A", lowercaseUri, 3);
        var index = IndexWith(doc, sym);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5, mixedCaseUri), CancellationToken.None);

        Assert.NotNull(result);
        var link = Assert.Single(result!);
        Assert.Equal(lowercaseUri, link.Location!.Uri.ToString());
    }

    // ── group key — no canonical definition ──────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnGroupKey_ReturnsNull_EvenWhenSymbolWithSameIdExists()
    {
        // A symbol "Unit_AT_AT" exists in the workspace, but the cursor is on a
        // group-membership tag value (not on a reference to that symbol). The definition
        // handler must return null — group keys have no canonical single definition.
        var groupMembership = new DocumentGroupMembership(
            new GroupMembership("Unit_AT_AT", "SFXEvent", new FileOrigin(TestUri, 2, 4)),
            1, 5, 10);

        var callerDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray<GameReference>.Empty,
            GroupMemberships: ImmutableArray.Create(groupMembership));

        // Also put a real symbol with the same id in the index — collision scenario.
        var collidingSymbol = SymbolAt("Unit_AT_AT", TargetUri, 10);
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Unit_AT_AT", ImmutableArray.Create(collidingSymbol));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(TestUri, callerDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty)
        {
            WorkspaceGroupMemberships =
                ImmutableDictionary.Create<string, ImmutableArray<GroupMembership>>(StringComparer.OrdinalIgnoreCase)
                    .Add("Unit_AT_AT", ImmutableArray.Create(groupMembership.Membership))
        };

        var handler = BuildHandler(index);
        // Cursor lands on the tag value span (line 1, col 7 — within [5..15))
        var result = await handler.Handle(At(1, 7), CancellationToken.None);

        Assert.Null(result);
    }

    // ── enum reference go-to-definition ──────────────────────────────────────

    [Fact]
    public async Task Handle_EnumReference_NavigatesToEnumValueDefinition()
    {
        const string enumFilePath = "file:///enum/surfacefxtriggertype.xml";
        var enumRef = new GameReference("enum:SurfaceFXTriggerType/GENERIC_TRACK",
            null, null, TestUri, 0, 4, 13);
        var callerDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(enumRef));

        var origin = new FileOrigin(enumFilePath, 2, null);
        var enumDefs = ImmutableDictionary.Create<string, ImmutableDictionary<string, FileOrigin>>(
                StringComparer.OrdinalIgnoreCase)
            .Add("SurfaceFXTriggerType",
                ImmutableDictionary.Create<string, FileOrigin>(StringComparer.OrdinalIgnoreCase)
                    .Add("GENERIC_TRACK", origin));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(TestUri, callerDoc),
            ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase),
            ImmutableDictionary.Create<string, ImmutableArray<GameReference>>(StringComparer.OrdinalIgnoreCase))
        {
            WorkspaceEnumValueDefinitions = enumDefs
        };

        var handler = BuildHandler(index);
        var result = await handler.Handle(At(0, 6), CancellationToken.None);

        Assert.NotNull(result);
        var link = Assert.Single(result!);
        Assert.True(link.IsLocation);
        Assert.Equal(enumFilePath, link.Location!.Uri.ToString());
        Assert.Equal(2, link.Location.Range.Start.Line);
    }

    [Fact]
    public async Task Handle_EnumReference_UnknownValue_ReturnsNull()
    {
        var enumRef = new GameReference("enum:SurfaceFXTriggerType/MISSING_VALUE",
            null, null, TestUri, 0, 4, 13);
        var callerDoc = new DocumentIndex(TestUri, 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(enumRef));

        // No WorkspaceEnumValueDefinitions for this enum.
        var index = IndexWith(callerDoc);
        var handler = BuildHandler(index);

        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var handler = BuildHandler(GameIndex.Empty, new DenyAllEaWContext());
        var result = await handler.Handle(At(0, 5), CancellationToken.None);
        Assert.Null(result);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }
        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}