// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlHoverHandlerTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlHoverHandler handler, FakeGameWorkspaceHost host, FakeSchemaProvider schema,
        FakeConfigProvider config) Build(FakeFileTypeRegistry? registry = null)
    {
        var host = new FakeGameWorkspaceHost();
        var schema = new FakeSchemaProvider();
        var config = new FakeConfigProvider();
        return (new XmlHoverHandler(host, schema, config, NullLogger<XmlHoverHandler>.Instance,
            registry ?? new FakeFileTypeRegistry()), host, schema, config);
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
            ReferenceType = "Faction"
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
            EnumName = "ArmorType"
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

    [Fact]
    public async Task Handle_TagNameCollidesWithTypeName_TypeHoverWins()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<Faction>", 1);
        schema.TagToReturn = MakeTag("Faction"); // Faction is also a tag name
        schema.TypeToReturn = new GameObjectTypeDefinition
        {
            TypeName = "Faction",
            NameTag = "Name",
            Description = new Dictionary<string, string> { ["en"] = "A faction." }
        };

        var result = await handler.Handle(At(0, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("name tag", md, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Float", md); // tag hover includes value type; type hover does not
    }

    // ── registry-based type-container hover ────────────────────────────────

    [Fact]
    public async Task Handle_RegistryMappedMultiInstance_TypeContainerArbitraryName_ReturnsTypeHover()
    {
        // "Fighter_Mk2" is an arbitrary element name; the actual type is "SpaceUnit".
        // Hovering on the type container must show the SpaceUnit type hover.
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
        // cursor on 'F' of Fighter_Mk2 (line 1, col 1)
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.NotNull(result);
        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("name tag", md, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Float", md); // must be type hover, not tag hover
    }

    [Fact]
    public async Task Handle_RegistryMappedMultiInstance_FieldTagInsideTypeContainer_ReturnsTagHover()
    {
        // Depth-2 tags (field tags inside type containers) must still show tag hover, not type hover.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });
        schema.TagToReturn = MakeTag("Max_Speed");

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
    public async Task Handle_TypeRootElement_ReturnsTypeHover()
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

        Assert.NotNull(result);
        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("GameObjectType", md);
        Assert.Contains("All game objects.", md);
    }

    [Fact]
    public async Task Handle_SingletonType_HoverContainsSingleton()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<GameConstants/>", 1);
        schema.TagToReturn = null;
        schema.TypeToReturn = new GameObjectTypeDefinition
        {
            TypeName = "GameConstants",
            NameTag = null,
            Description = new Dictionary<string, string> { ["en"] = "Global constants." }
        };

        var result = await handler.Handle(At(0, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("singleton", md, StringComparison.OrdinalIgnoreCase);
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
        schema.TagToReturn = MakeTag("Max_Speed");

        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.DoesNotContain("**Note:**", md);
    }

    [Fact]
    public async Task Handle_TypeWithNotes_IncludesNotesSection()
    {
        var (handler, host, schema, _) = Build();
        host.AddOrUpdate(TestUri.ToString(), "<GameConstants/>", 1);
        schema.TagToReturn = null;
        schema.TypeToReturn = new GameObjectTypeDefinition
        {
            TypeName = "GameConstants",
            NameTag = null,
            Description = new Dictionary<string, string> { ["en"] = "Global constants." },
            Notes = new Dictionary<string, string> { ["en"] = "Legacy type, rarely needed." }
        };

        var result = await handler.Handle(At(0, 2), CancellationToken.None);

        var md = result!.Contents.MarkupContent!.Value;
        Assert.Contains("Legacy type, rarely needed.", md);
        Assert.Contains("Note", md, StringComparison.OrdinalIgnoreCase);
    }

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
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
        private readonly Dictionary<string, GameObjectTypeDefinition> _typesByName =
            new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? TagToReturn { get; set; }
        public GameObjectTypeDefinition? TypeToReturn { get; set; }

        public void AddType(GameObjectTypeDefinition type) => _typesByName[type.TypeName] = type;

        public XmlTagDefinition? GetTag(string _)
        {
            return TagToReturn;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _typesByName.TryGetValue(name, out var def) ? def : TypeToReturn;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
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
    }

    private sealed class FakeFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(string key, ImmutableArray<string> types) => _map[key] = types;

        public ImmutableArray<string> GetTypesForFile(string normalizedPath) =>
            _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;

        public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames) =>
            _map[normalizedPath] = typeNames;

        public void UnregisterFile(string normalizedPath) => _map.Remove(normalizedPath);

        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;
    }

    private sealed class FakeConfigProvider : ILspConfigurationProvider
    {
        public string Locale { get; set; } = "en";
        public LspConfiguration Current => new() { Locale = Locale };

        public void LoadFrom(object? _)
        {
        }
    }
}