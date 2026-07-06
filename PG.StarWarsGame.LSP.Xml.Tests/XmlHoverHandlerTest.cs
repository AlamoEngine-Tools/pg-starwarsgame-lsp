// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlHoverHandlerTest
{
    // ── Ability sub-object hover ─────────────────────────────────────────────

    private const string AbilityXml =
        "<Root>\n" +
        "<Abilities SubObjectList=\"Yes\">\n" +
        "<Lucky_Shot_Attack_Ability Name=\"Luke_Shot\">\n" +
        "<Applicable_Unit_Categories>INFANTRY</Applicable_Unit_Categories>\n" +
        "</Lucky_Shot_Attack_Ability>\n" +
        "</Abilities>\n" +
        "</Root>";
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlHoverHandler handler, FakeGameWorkspaceHost host, FakeSchemaProvider schema,
        FakeConfigProvider config) Build(FakeFileTypeRegistry? registry = null, FakeIndexService? indexService = null,
            IEaWXmlContext? ctx = null)
    {
        var host = new FakeGameWorkspaceHost();
        var schema = new FakeSchemaProvider();
        var config = new FakeConfigProvider();
        var fileTypeRegistry = registry ?? new FakeFileTypeRegistry();
        var strategyRegistry = new XmlHoverStrategyRegistry([
            new ReferenceHoverStrategy(),
            new AssetHoverStrategy(),
            new TagNameHoverStrategy(fileTypeRegistry)
        ]);
        return (new XmlHoverHandler(TestParseCache.For(host), indexService ?? new FakeIndexService(), schema,
                config,
                NullLogger<XmlHoverHandler>.Instance,
                new FileHelper(new MockFileSystem()), ctx ?? new AllowAllEaWContext(),
                strategyRegistry),
            host, schema, config);
    }

    private static GameIndex IndexWith(DocumentIndex callerDoc, GameSymbol? symbol = null)
    {
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty.Add(callerDoc.DocumentUri, callerDoc);
        var defs = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase);
        if (symbol is not null)
            defs = defs.Add(symbol.Id, ImmutableArray.Create(symbol));
        return new GameIndex(BaselineIndex.Empty, docs, defs,
            ImmutableDictionary.Create<string, ImmutableArray<GameReference>>(StringComparer.OrdinalIgnoreCase));
    }

    private static DocumentIndex DocWithRef(string refId, int line, int col, int len, string? typeName = "SpaceUnit")
    {
        var uri = TestUri.ToString();
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(new GameReference(refId, GameSymbolKind.XmlObject, typeName, uri, line, col, len)));
    }

    private static DocumentIndex DocWithRefs(params GameReference[] refs)
    {
        var uri = TestUri.ToString();
        return new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty, refs.ToImmutableArray());
    }

    private static GameSymbol SymbolOf(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new FileOrigin("file:///other.xml", 0, null), null);
    }

    private static HoverParams At(int line, int character)
    {
        return new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri },
            Position = new Position(line, character)
        };
    }

    private static XmlTagDefinition MakeTag(
        string name = "Max_Speed",
        bool deprecated = false,
        string? since = null,
        string? descEn = "Some description",
        string? descDe = null)
    {
        var desc = new Dictionary<string, string>();
        if (descEn is not null) desc["en"] = descEn;
        if (descDe is not null) desc["de"] = descDe;
        return new XmlTagDefinition
        {
            Tag = name,
            ValueType = XmlValueType.Float,
            Deprecated = deprecated,
            AvailableSince = since,
            Description = desc
        };
    }

    // ── null / miss cases ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_BufferEmpty_ReturnsNull()
    {
        var (handler, _, _, _) = Build();
        var result = await handler.Handle(At(0, 1), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LineOutOfBounds_ReturnsNull()
    {
        var (handler, host, _, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Foo/>", 1);

        var result = await handler.Handle(At(99, 0), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnValue_ReturnsNull()
    {
        var (handler, host, _, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Max_Speed>500</Max_Speed>", 1);
        // cursor on "500" (position 11)
        var result = await handler.Handle(At(0, 11), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TagNotInSchema_ReturnsNull()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Max_Speed>500</Max_Speed>", 1);
        schema.TagToReturn = null;
        schema.TypeToReturn = null;

        var result = await handler.Handle(At(0, 2), CancellationToken.None);
        Assert.Null(result);
    }

    // ── tag hover ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnOpeningTagName_ReturnsTagHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed>500</Max_Speed>\n</Root>", 1);
        schema.TagToReturn = MakeTag();

        // cursor on 'M' of Max_Speed (line 1, col 1)
        var result = await handler.Handle(At(1, 1), CancellationToken.None);

        Assert.NotNull(result);
        var md = result.Contents.MarkupContent!.Value;
        Assert.Contains("Max_Speed", md);
    }

    [Fact]
    public async Task Handle_CursorOnClosingTagName_ReturnsTagHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed>500</Max_Speed>\n</Root>", 1);
        schema.TagToReturn = MakeTag();

        // "</Max_Speed>" starts at index 14; 'M' is at 16 (line 1)
        var result = await handler.Handle(At(1, 16), CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_DeprecatedTag_HoverContainsDeprecatedMarker()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Old_Tag/>\n</Root>", 1);
        schema.TagToReturn = MakeTag("Old_Tag", true);

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("deprecated", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_AvailableSince_IncludedInHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<New_Tag/>\n</Root>", 1);
        schema.TagToReturn = MakeTag("New_Tag", since: "FoC 1.0");

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("FoC 1.0", md);
    }

    [Fact]
    public async Task Handle_LocaleUsedFromConfig()
    {
        var (handler, host, schema, config) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Foo/>\n</Root>", 1);
        schema.TagToReturn = MakeTag("Foo", descEn: "English", descDe: "Deutsch");
        config.Locale = "de";

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Deutsch", md);
        Assert.DoesNotContain("English", md);
    }

    [Fact]
    public async Task Handle_HoverRange_MatchesTagNameSpan()
    {
        var (handler, host, schema, _) = Build();
        // "<Root>\n<Foo/>\n</Root>" — on line 1, 'F' at col 1, length 3
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Foo/>\n</Root>", 1);
        schema.TagToReturn = MakeTag("Foo");

        var result = await handler.Handle(At(1, 1), CancellationToken.None);

        Assert.NotNull(result?.Range);
        Assert.Equal(1, result!.Range!.Start.Character);
        Assert.Equal(4, result.Range.End.Character); // 1 + len("Foo")
    }

    // ── value type hint ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FloatTag_HoverContainsFormatHint()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed>500</Max_Speed>\n</Root>", 1);
        schema.TagToReturn = MakeTag(); // ValueType = Float

        var result = await handler.Handle(At(1, 1), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("1.0f", md);
    }

    [Fact]
    public async Task Handle_NameReferenceXmlObject_HoverContainsReferenceHint()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Affiliation/>\n</Root>", 1);
        schema.TagToReturn = new XmlTagDefinition
        {
            Tag = "Affiliation",
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = "Faction" }
        };

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Faction", md);
        Assert.Contains("Reference", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_DynamicEnumValue_HoverContainsEnumHint()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Armor_Type/>\n</Root>", 1);
        schema.TagToReturn = new XmlTagDefinition
        {
            Tag = "Armor_Type",
            ValueType = XmlValueType.DynamicEnumValue,
            ReferenceKind = ReferenceKind.Enum,
            Enum = new EnumDefinition { Name = "ArmorType", Kind = EnumKind.DynamicXml, Values = [] }
        };

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("ArmorType", md);
        Assert.Contains("Enum", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_NonTypeRootTagMatchesTagName_ReturnsNull()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Hardpoints><Max_Speed>500</Max_Speed></Hardpoints>", 1);
        schema.TagToReturn = MakeTag("Hardpoints"); // registered as a tag but NOT as a type
        // schema.TypeToReturn = null (default)

        // Cursor on 'H' of <Hardpoints> (document root element)
        var result = await handler.Handle(At(0, 1), CancellationToken.None);

        Assert.Null(result);
    }

    // ── registry-based type-container hover ────────────────────────────────

    [Fact]
    public async Task Handle_RegistryMappedMultiInstance_TypeContainerArbitraryName_ReturnsTypeHover()
    {
        // "Fighter_Mk2" is an arbitrary element name — not a registered tag, so no hover.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(new GameObjectTypeDefinition
        {
            TypeName = "SpaceUnit",
            NameTag = "Name",
            Description = new Dictionary<string, string> { ["en"] = "A space unit." }
        });

        host.AddOrUpdate(TestUri.ToString(),
            "<GameObjectFiles>\n<Fighter_Mk2/>\n</GameObjectFiles>", 1);
        // cursor on 'F' of Fighter_Mk2 (line 1, col 2)
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.NotNull(result);
        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("fighter_mk2", md);
    }

    [Fact]
    public async Task Handle_RegistryMappedMultiInstance_FieldTagInsideTypeContainer_ReturnsTagHover()
    {
        // Depth-2 tags (field tags inside type containers) must still show tag hover, not type hover.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });

        schema.TagToReturn = MakeTag();

        host.AddOrUpdate(TestUri.ToString(),
            "<GameObjectFiles>\n<Fighter_Mk2>\n<Max_Speed/>\n</Fighter_Mk2>\n</GameObjectFiles>", 1);
        // cursor on 'M' of Max_Speed (line 2, col 1)
        var result = await handler.Handle(At(2, 2), CancellationToken.None);

        Assert.NotNull(result);
        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Float", md); // tag hover includes value type
        Assert.DoesNotContain("name tag", md, StringComparison.OrdinalIgnoreCase);
    }

    // ── type hover ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TypeRootElement_ReturnsNoHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<GameObjectType>", 1);
        schema.TagToReturn = null; // not a tag
        schema.TypeToReturn = new GameObjectTypeDefinition
        {
            TypeName = "GameObjectType",
            NameTag = "Name",
            Description = new Dictionary<string, string> { ["en"] = "All game objects." }
        };

        var result = await handler.Handle(At(0, 2), CancellationToken.None);
        Assert.Null(result);
    }

    // ── Notes display ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TagWithNotes_IncludesNotesSection()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Old_Tag/>\n</Root>", 1);
        schema.TagToReturn = new XmlTagDefinition
        {
            Tag = "Old_Tag",
            ValueType = XmlValueType.Float,
            Description = new Dictionary<string, string> { ["en"] = "A tag." },
            Notes = new Dictionary<string, string> { ["en"] = "Never used in vanilla." }
        };

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Never used in vanilla.", md);
        Assert.Contains("Note", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_TagWithoutNotes_DoesNotIncludeNotesSeparator()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed/>\n</Root>", 1);
        schema.TagToReturn = MakeTag();

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.DoesNotContain("**Note:**", md);
    }

    // ── tag-name gate ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnTagValue_WithProperRoot_ReturnsNull()
    {
        // Verifies column gating: cursor is on "500", not on the tag name.
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed>500</Max_Speed>\n</Root>", 1);
        schema.TagToReturn = MakeTag();

        // '5' of "500" is at col 11 on line 1
        var result = await handler.Handle(At(1, 11), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CursorOnMultilineClosingTag_ReturnsTagHover()
    {
        // Closing tag is on a different line from the opening tag.
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Max_Speed>\n500\n</Max_Speed>\n</Root>", 1);
        schema.TagToReturn = MakeTag();

        // </Max_Speed> on line 3: '<' at 0, '/' at 1, 'M' at 2
        var result = await handler.Handle(At(3, 2), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("Max_Speed", result!.Contents.MarkupContent!.Value);
    }

    // ── reference hover ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CursorOnReferenceValue_ReturnsTypeHover()
    {
        // <Root>\n<Affiliation>EMPIRE</Affiliation>\n</Root>
        // "EMPIRE" starts at col 13 on line 1
        var symbol = SymbolOf("EMPIRE", "Faction");
        var index = IndexWith(DocWithRef("EMPIRE", 1, 13, 6), symbol);
        var indexService = new FakeIndexService { Current = index };
        var (handler, host, schema, _) = Build(indexService: indexService);
        schema.AddType(new GameObjectTypeDefinition
        {
            TypeName = "Faction",
            Description = new Dictionary<string, string> { ["en"] = "A playable faction." }
        });
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Affiliation>EMPIRE</Affiliation>\n</Root>", 1);

        var result = await handler.Handle(At(1, 14), CancellationToken.None);

        Assert.NotNull(result);
        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Faction", md);
        Assert.Contains("EMPIRE", md);
    }

    [Fact]
    public async Task Handle_CursorOnReferenceValue_HoverRangeCoversToken()
    {
        var symbol = SymbolOf("EMPIRE", "Faction");
        var index = IndexWith(DocWithRef("EMPIRE", 1, 13, 6), symbol);
        var indexService = new FakeIndexService { Current = index };
        var (handler, host, schema, _) = Build(indexService: indexService);
        schema.AddType(new GameObjectTypeDefinition
            { TypeName = "Faction", Description = new Dictionary<string, string>() });
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Affiliation>EMPIRE</Affiliation>\n</Root>", 1);

        var result = await handler.Handle(At(1, 14), CancellationToken.None);

        Assert.Equal(1, result!.Range!.Start.Line);
        Assert.Equal(13, result.Range.Start.Character);
        Assert.Equal(19, result.Range.End.Character); // 13 + 6
    }

    [Fact]
    public async Task Handle_CursorOnSecondListItem_ReturnsTypeHoverForThatItem()
    {
        // <Root>\n<Units>Alpha Beta</Units>\n</Root>
        // "Alpha" at col 7, len 5; "Beta" at col 13, len 4
        var uri = TestUri.ToString();
        var refAlpha = new GameReference("Alpha", GameSymbolKind.XmlObject, "SpaceUnit", uri, 1, 7, 5);
        var refBeta = new GameReference("Beta", GameSymbolKind.XmlObject, "SpaceUnit", uri, 1, 13, 4);
        var doc = DocWithRefs(refAlpha, refBeta);
        var symbol = SymbolOf("Beta", "SpaceUnit");
        var index = IndexWith(doc, symbol);
        var indexService = new FakeIndexService { Current = index };
        var (handler, host, schema, _) = Build(indexService: indexService);
        schema.AddType(new GameObjectTypeDefinition
            { TypeName = "SpaceUnit", Description = new Dictionary<string, string>() });
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Units>Alpha Beta</Units>\n</Root>", 1);

        var result = await handler.Handle(At(1, 14), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Beta", result!.Contents.MarkupContent!.Value);
        Assert.DoesNotContain("Alpha", result.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Handle_CrossTypePlaceholderId_ResolvesUsingExpectedTypeName()
    {
        // Two symbols share the id "Null" — an SFXEvent placeholder and an unrelated TradeRouteLine
        // placeholder (the engine allows distinct types to share an id, e.g. "Null"/"Default"). The
        // reference's ExpectedTypeName ("SFXEvent") must disambiguate which one hover resolves to.
        var uri = TestUri.ToString();
        var sfxNull = new GameSymbol("Null", GameSymbolKind.XmlObject, "SFXEvent",
            new FileOrigin("file:///sfx.xml", 0, null), null);
        var otherNull = new GameSymbol("Null", GameSymbolKind.XmlObject, "TradeRouteLine",
            new FileOrigin("file:///routes.xml", 0, null), null);
        var reference = new GameReference("Null", GameSymbolKind.XmlObject, "SFXEvent", uri, 1, 18, 4);
        var doc = new DocumentIndex(uri, 1, ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(reference));
        // otherNull inserted first so untyped Resolve's stable-order tie-break would pick the WRONG
        // symbol if ExpectedTypeName weren't consulted — proves the fix, not just insertion luck.
        var defs = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
            .Add("Null", ImmutableArray.Create(otherNull, sfxNull));
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri, doc),
            defs,
            ImmutableDictionary.Create<string, ImmutableArray<GameReference>>(StringComparer.OrdinalIgnoreCase));
        var indexService = new FakeIndexService { Current = index };
        var (handler, host, schema, _) = Build(indexService: indexService);
        schema.AddType(new GameObjectTypeDefinition
            { TypeName = "SFXEvent", Description = new Dictionary<string, string>() });
        schema.AddType(new GameObjectTypeDefinition
            { TypeName = "TradeRouteLine", Description = new Dictionary<string, string>() });
        host.AddOrUpdate(uri, "<Root>\n<SFXEvent_Select>Null</SFXEvent_Select>\n</Root>", 1);

        var result = await handler.Handle(At(1, 19), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("SFXEvent", result!.Contents.MarkupContent!.Value);
        Assert.DoesNotContain("TradeRouteLine", result.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Handle_CursorOnUnresolvedReference_ReturnsNull()
    {
        var index = IndexWith(DocWithRef("MISSING_UNIT", 1, 13, 12));
        var indexService = new FakeIndexService { Current = index };
        var (handler, host, _, _) = Build(indexService: indexService);
        host.AddOrUpdate(TestUri.ToString(), "<Root>\n<Affiliation>MISSING_UNIT</Affiliation>\n</Root>", 1);

        var result = await handler.Handle(At(1, 14), CancellationToken.None);
        Assert.Null(result);
    }

    private static GameObjectTypeDefinition AbilityTypeDef(string name)
    {
        return new GameObjectTypeDefinition { TypeName = name, NameTag = "Name" };
    }

    private static XmlTagDefinition AbilitiesContainerTag()
    {
        return new XmlTagDefinition
        {
            Tag = "Abilities",
            ValueType = XmlValueType.AbilityDefinitionSubObjectList
        };
    }

    [Fact]
    public async Task Handle_CursorOnAbilityClassElement_ShowsAbilityTypeHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), AbilityXml, 1);
        // Abilities tag must resolve to AbilityDefinitionSubObjectList so parent chain walk works
        schema.AddTagByName(AbilitiesContainerTag());
        schema.AddType(AbilityTypeDef("LuckyShotAttackAbility"));

        // Line 2: "<Lucky_Shot_Attack_Ability Name="Luke_Shot">" — cursor on 'L'
        var result = await handler.Handle(At(2, 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("LuckyShotAttackAbility", result.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Handle_CursorOnFieldInsideAbilityElement_ShowsAbilityTypeTagHover()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), AbilityXml, 1);
        schema.AddTagByName(AbilitiesContainerTag());
        schema.AddType(AbilityTypeDef("LuckyShotAttackAbility"));
        schema.AddTagForType("LuckyShotAttackAbility",
            new XmlTagDefinition
            {
                Tag = "Applicable_Unit_Categories", ValueType = XmlValueType.NameReference,
                Description = new Dictionary<string, string> { ["en"] = "Unit category filter" }
            });

        // Line 3: "<Applicable_Unit_Categories>..." — cursor on 'A'
        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Applicable_Unit_Categories", result.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Handle_CursorOnFieldInsideAbilityElement_DoesNotUseRegisteredGameObjectTypeSchema()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("GameObjectType"));
        var (handler, host, schema, _) = Build(registry);
        host.AddOrUpdate(TestUri.ToString(), AbilityXml, 1);
        schema.AddTagByName(AbilitiesContainerTag());
        // GameObjectType has NO Applicable_Unit_Categories tag; ability type does
        schema.AddType(AbilityTypeDef("GameObjectType"));
        schema.AddType(AbilityTypeDef("LuckyShotAttackAbility"));
        schema.AddTagForType("LuckyShotAttackAbility",
            new XmlTagDefinition
            {
                Tag = "Applicable_Unit_Categories", ValueType = XmlValueType.Float,
                Description = new Dictionary<string, string> { ["en"] = "From ability schema" }
            });

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Applicable_Unit_Categories", result.Contents.MarkupContent!.Value);
        Assert.Contains("From ability schema", result.Contents.MarkupContent!.Value);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsNull()
    {
        var (handler, host, _, _) = Build(ctx: new DenyAllEaWContext());
        host.AddOrUpdate(TestUri.ToString(), "<Root><Foo/></Root>", 1);

        var result = await handler.Handle(At(0, 7), CancellationToken.None);

        Assert.Null(result);
    }

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tagsByName =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<XmlTagDefinition>> _tagsByTypeName =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, GameObjectTypeDefinition> _typesByName =
            new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? TagToReturn { get; set; }
        public GameObjectTypeDefinition? TypeToReturn { get; set; }

        public XmlTagDefinition? GetTag(string name)
        {
            return _tagsByName.TryGetValue(name, out var def) ? def : TagToReturn;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return TagToReturn is null
                ? []
                : [TagToReturn];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _typesByName.TryGetValue(name, out var def) ? def : TypeToReturn;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            if (_tagsByTypeName.TryGetValue(typeName, out var list)) return list;
            return GetObjectType(typeName) is null ? [] : GetAllTagDefinitions(typeName);
        }

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];

        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddTagByName(XmlTagDefinition tag)
        {
            _tagsByName[tag.Tag] = tag;
        }

        public void AddTagForType(string typeName, XmlTagDefinition tag)
        {
            if (!_tagsByTypeName.TryGetValue(typeName, out var list))
                _tagsByTypeName[typeName] = list = [];
            list.Add(tag);
        }

        public void AddType(GameObjectTypeDefinition type)
        {
            _typesByName[type.TypeName] = type;
        }
    }

    private sealed class FakeFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public ImmutableArray<string> GetTypesForFile(string normalizedPath)
        {
            return _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;
        }

        public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
        {
            _map[normalizedPath] = typeNames;
        }

        public void UnregisterFile(string normalizedPath)
        {
            _map.Remove(normalizedPath);
        }

        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;

        public void Register(string key, ImmutableArray<string> types)
        {
            _map["file:///" + key] = types;
        }
    }

    private sealed class FakeConfigProvider : ILspConfigurationProvider
    {
        public string Locale { get; set; } = "en";
        public LspConfiguration Current => new() { Locale = Locale };

        public void LoadFrom(object? _)
        {
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;
        public event Action<GameIndex>? DynamicEnumChanged;

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
