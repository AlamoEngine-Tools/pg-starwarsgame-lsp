// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlCompletionHandlerTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlCompletionHandler handler, FakeGameWorkspaceHost host, FakeSchemaProvider schema,
        FakeProposalRegistry proposals) Build(FakeFileTypeRegistry? registry = null)
    {
        var host = new FakeGameWorkspaceHost();
        var schema = new FakeSchemaProvider();
        var proposals = new FakeProposalRegistry();
        var indexService = new FakeIndexService();
        var storyProposals = new StoryParamValueProposalProvider(schema);
        return (new XmlCompletionHandler(host, schema, proposals, indexService, storyProposals,
            registry ?? new FakeFileTypeRegistry()), host, schema, proposals);
    }

    private static CompletionParams At(int line, int character, string? triggerChar = null)
    {
        return new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri },
            Position = new Position(line, character),
            Context = triggerChar is null
                ? null
                : new CompletionContext
                {
                    TriggerKind = CompletionTriggerKind.TriggerCharacter,
                    TriggerCharacter = triggerChar
                }
        };
    }

    private static XmlTagDefinition MakeTag(string name, bool multipleAllowed = false)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.Float, MultipleAllowed = multipleAllowed };
    }

    private static GameObjectTypeDefinition MakeType(string name)
    {
        return new GameObjectTypeDefinition { TypeName = name };
    }

    // ── tag-name completions ────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TagNameContext_ReturnsTagsForType()
    {
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));
        schema.AddTagForType("Faction", MakeTag("Display_Name"));

        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  <\n</Faction>", 1);
        // cursor on line 1 after '<'
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Max_Speed", labels);
        Assert.Contains("Display_Name", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_SingletonAlreadyPresent_ExcludedFromList()
    {
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));
        schema.AddTagForType("Faction", MakeTag("Display_Name"));

        // Max_Speed already present
        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  <Max_Speed>500</Max_Speed>\n  <\n</Faction>", 1);
        var result = await handler.Handle(At(2, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.DoesNotContain("Max_Speed", labels);
        Assert.Contains("Display_Name", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_MultipleAllowedAlreadyPresent_StillInList()
    {
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("SFXEvent_Attack", true));

        // SFXEvent_Attack already present; still allowed again
        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  <SFXEvent_Attack>Sfx_A</SFXEvent_Attack>\n  <\n</Faction>",
            1);
        var result = await handler.Handle(At(2, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("SFXEvent_Attack", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_ParentNotInSchema_ReturnsEmpty()
    {
        var (handler, host, schema, _) = Build();
        // No type registered for SpaceUnit

        host.AddOrUpdate(TestUri.ToString(), "<SpaceUnit>\n  <\n</SpaceUnit>", 1);
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_TagNameContext_PartialPrefix_FiltersResults()
    {
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("SFXEvent_Attack"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));

        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  <SFX\n</Faction>", 1);
        // cursor at col 6 (after '<SFX')
        var result = await handler.Handle(At(1, 6), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("SFXEvent_Attack", labels);
        Assert.DoesNotContain("Max_Speed", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_InsertTextIsSnippet()
    {
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));

        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  <\n</Faction>", 1);
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        var item = result.Items.Single(i => i.Label == "Max_Speed");
        Assert.Equal("Max_Speed></Max_Speed>", item.InsertText);
        Assert.Equal(CompletionItemKind.Property, item.Kind);
    }

    // ── value completions ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValueCompletion_CursorInsideNonTypeRoot_ReturnsEmpty()
    {
        var (handler, host, schema, proposals) = Build();
        // Hardpoints is registered as a tag but NOT as a type (unregistered file-level wrapper)
        schema.AddTagForType("SomeOtherType", MakeTag("Hardpoints"));
        proposals.ProposalsToReturn = [new ValueProposal { Label = "HP_01" }];

        // Cursor inside the root <Hardpoints> body — must not offer value completions
        host.AddOrUpdate(TestUri.ToString(), "<Hardpoints>\n  \n</Hardpoints>", 1);
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_ValueCompletion_EnclosingElementIsType_ReturnsEmpty()
    {
        var (handler, host, schema, proposals) = Build();
        schema.AddType(MakeType("Faction"));
        // Faction is also registered as a tag with the same name (name collision)
        schema.AddTagForType("SomeOtherType", MakeTag("Faction"));
        proposals.ProposalsToReturn = [new ValueProposal { Label = "EMPIRE" }];

        // Cursor inside the type-container body — not inside a field tag
        host.AddOrUpdate(TestUri.ToString(), "<Faction>\n  \n</Faction>", 1);
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.Empty(result.Items);
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
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<XmlTagDefinition>> _tagsByType = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string name)
        {
            return _tags.GetValueOrDefault(name);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [.. _types.Values];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string name)
        {
            return _tagsByType.TryGetValue(name, out var list) ? list : [];
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

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }

        public void AddTagForType(string typeName, XmlTagDefinition tag)
        {
            _tags[tag.Tag] = tag;
            if (!_tagsByType.TryGetValue(typeName, out var list))
                _tagsByType[typeName] = list = [];
            list.Add(tag);
        }
    }

    private sealed class FakeProposalRegistry : IXmlValueProposalRegistry
    {
        public IReadOnlyList<ValueProposal> ProposalsToReturn { get; set; } = [];

        public IReadOnlyList<ValueProposal> GetProposals(XmlValueType _, XmlTagDefinition __, string ___)
        {
            return ProposalsToReturn;
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct) => Task.CompletedTask;
        public void RemoveDocument(string uri) { }
        public void ApplyBaseline(BaselineIndex baseline) { }
        public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
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

    // ── StoryParser tag-name completions ──────────────────────────────────────

    [Fact]
    public async Task StoryParser_TagNameInsideEvent_KnownEventType_OffersOnlyUsedParamTags()
    {
        // STORY_ACCUMULATE has exactly 1 param; Event_Param2 must not appear.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>STORY_ACCUMULATE</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        // cursor on line 3 after '<'
        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Event_Param1", labels);
        Assert.DoesNotContain("Event_Param2", labels);
    }

    [Fact]
    public async Task StoryParser_TagNameInsideEvent_UnknownEventType_OffersNoParamTags()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>NOT_A_REAL_EVENT</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.DoesNotContain(labels, l => l.StartsWith("Event_Param", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoryParser_TagNameInsideEvent_AlwaysOffersEventTypeTag()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(2, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Event_Type", labels);
        Assert.Contains("Reward_Type", labels);
    }

    [Fact]
    public async Task StoryParser_TagNameInsideEvent_KnownRewardType_OffersRewardParamTags()
    {
        // CREDITS has 1 reward param; Reward_Param2 must not appear.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>STORY_MOVIE_DONE</Event_Type>\n<Reward_Type>CREDITS</Reward_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(4, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Reward_Param1", labels);
        Assert.DoesNotContain("Reward_Param2", labels);
    }

    // ── StoryParser value completions ─────────────────────────────────────────

    [Fact]
    public async Task StoryParser_ValueOnEventParam_BooleanInt_ReturnsZeroAndOne()
    {
        // LOCK_CONTROLS Reward_Param1 is BooleanInt
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>STORY_MOVIE_DONE</Event_Type>\n<Reward_Type>LOCK_CONTROLS</Reward_Type>\n<Reward_Param1>\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        // cursor on line 4 inside <Reward_Param1> body
        var result = await handler.Handle(At(4, 15), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("0", labels);
        Assert.Contains("1", labels);
    }

    // ── StoryParser registry-based detection ─────────────────────────────────

    [Fact]
    public async Task StoryParser_RegistryMatch_ArbitraryRootElement_OffersStoryEventCompletions()
    {
        // Detection must use the registry, not the root element name.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryPlots>\n<Event>\n<Event_Type>STORY_ACCUMULATE</Event_Type>\n<\n</Event>\n</StoryPlots>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "Event_Type");
    }

    [Fact]
    public async Task StoryParser_RootElementMatchesTypeName_NotInRegistry_NoStoryCompletions()
    {
        // Root element named "StoryParser" but the file is not registered → no story completions.
        var (handler, host, schema, _) = Build();
        schema.AddType(MakeType("StoryParser"));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>STORY_ACCUMULATE</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}