using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlCompletionHandlerTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlCompletionHandler handler, XmlDocumentBuffer buffer, FakeSchemaProvider schema,
        FakeProposalRegistry proposals) Build()
    {
        var buffer = new XmlDocumentBuffer();
        var schema = new FakeSchemaProvider();
        var proposals = new FakeProposalRegistry();
        return (new XmlCompletionHandler(buffer, schema, proposals), buffer, schema, proposals);
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
        var (handler, buffer, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));
        schema.AddTagForType("Faction", MakeTag("Display_Name"));

        buffer.Set(TestUri, "<Faction>\n  <\n</Faction>");
        // cursor on line 1 after '<'
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Max_Speed", labels);
        Assert.Contains("Display_Name", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_SingletonAlreadyPresent_ExcludedFromList()
    {
        var (handler, buffer, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));
        schema.AddTagForType("Faction", MakeTag("Display_Name"));

        // Max_Speed already present
        buffer.Set(TestUri, "<Faction>\n  <Max_Speed>500</Max_Speed>\n  <\n</Faction>");
        var result = await handler.Handle(At(2, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.DoesNotContain("Max_Speed", labels);
        Assert.Contains("Display_Name", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_MultipleAllowedAlreadyPresent_StillInList()
    {
        var (handler, buffer, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("SFXEvent_Attack", true));

        // SFXEvent_Attack already present; still allowed again
        buffer.Set(TestUri, "<Faction>\n  <SFXEvent_Attack>Sfx_A</SFXEvent_Attack>\n  <\n</Faction>");
        var result = await handler.Handle(At(2, 3), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("SFXEvent_Attack", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_ParentNotInSchema_ReturnsEmpty()
    {
        var (handler, buffer, schema, _) = Build();
        // No type registered for SpaceUnit

        buffer.Set(TestUri, "<SpaceUnit>\n  <\n</SpaceUnit>");
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_TagNameContext_PartialPrefix_FiltersResults()
    {
        var (handler, buffer, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("SFXEvent_Attack"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));

        buffer.Set(TestUri, "<Faction>\n  <SFX\n</Faction>");
        // cursor at col 6 (after '<SFX')
        var result = await handler.Handle(At(1, 6), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("SFXEvent_Attack", labels);
        Assert.DoesNotContain("Max_Speed", labels);
    }

    [Fact]
    public async Task Handle_TagNameContext_InsertTextIsSnippet()
    {
        var (handler, buffer, schema, _) = Build();
        schema.AddType(MakeType("Faction"));
        schema.AddTagForType("Faction", MakeTag("Max_Speed"));

        buffer.Set(TestUri, "<Faction>\n  <\n</Faction>");
        var result = await handler.Handle(At(1, 3), CancellationToken.None);

        var item = result.Items.Single(i => i.Label == "Max_Speed");
        Assert.Equal("Max_Speed></Max_Speed>", item.InsertText);
        Assert.Equal(CompletionItemKind.Property, item.Kind);
    }

    // ── value completions ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValueCompletion_CursorInsideNonTypeRoot_ReturnsEmpty()
    {
        var (handler, buffer, schema, proposals) = Build();
        // Hardpoints is registered as a tag but NOT as a type (unregistered file-level wrapper)
        schema.AddTagForType("SomeOtherType", MakeTag("Hardpoints"));
        proposals.ProposalsToReturn = [new ValueProposal { Label = "HP_01" }];

        // Cursor inside the root <Hardpoints> body — must not offer value completions
        buffer.Set(TestUri, "<Hardpoints>\n  \n</Hardpoints>");
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Handle_ValueCompletion_EnclosingElementIsType_ReturnsEmpty()
    {
        var (handler, buffer, schema, proposals) = Build();
        schema.AddType(MakeType("Faction"));
        // Faction is also registered as a tag with the same name (name collision)
        schema.AddTagForType("SomeOtherType", MakeTag("Faction"));
        proposals.ProposalsToReturn = [new ValueProposal { Label = "EMPIRE" }];

        // Cursor inside the type-container body — not inside a field tag
        buffer.Set(TestUri, "<Faction>\n  \n</Faction>");
        var result = await handler.Handle(At(1, 2), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── fakes ───────────────────────────────────────────────────────────────

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
}