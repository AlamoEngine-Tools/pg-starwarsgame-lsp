// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlCompletionHandlerTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlCompletionHandler handler, FakeGameWorkspaceHost host, FakeSchemaProvider schema,
        FakeProposalRegistry proposals) Build(FakeFileTypeRegistry? registry = null, IEaWXmlContext? ctx = null)
    {
        var host = new FakeGameWorkspaceHost();
        var schema = new FakeSchemaProvider();
        var proposals = new FakeProposalRegistry();
        var indexService = new FakeIndexService();
        var storyProposals = new StoryParamValueProposalProvider();
        return (new XmlCompletionHandler(host, schema, proposals, indexService, storyProposals,
            new FakeCompletionRegistry(), registry ?? new FakeFileTypeRegistry(),
            new FileHelper(new MockFileSystem()), ctx ?? new AllowAllEaWContext()), host, schema, proposals);
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

    private static ParamDefinition Param(int position, XmlValueType type = XmlValueType.Int,
        EnumDefinition? enumDef = null, string? refType = null)
    {
        return new ParamDefinition
        {
            Position = position, ValueType = type,
            Enum = enumDef,
            ObjectType = refType is not null ? new GameObjectTypeDefinition { TypeName = refType } : null
        };
    }

    private static EnumValueDefinition StoryEvent(string name, params ParamDefinition[] paramDefs)
    {
        return new EnumValueDefinition
        {
            Name = name,
            Params = paramDefs.Length > 0 ? [.. paramDefs] : null
        };
    }

    private static EnumValueDefinition StoryReward(string name, params ParamDefinition[] paramDefs)
    {
        return new EnumValueDefinition
        {
            Name = name,
            Params = paramDefs.Length > 0 ? [.. paramDefs] : null
        };
    }

    private static EnumDefinition StoryEventTypeWith(params EnumValueDefinition[] values)
    {
        return new EnumDefinition
        {
            Name = "StoryEventType", Kind = EnumKind.SchemaFixed, Values = [.. values]
        };
    }

    private static EnumDefinition StoryRewardTypeWith(params EnumValueDefinition[] values)
    {
        return new EnumDefinition
        {
            Name = "StoryRewardType", Kind = EnumKind.SchemaFixed, Values = [.. values]
        };
    }

    private static EnumDefinition FlagEnum(string name, params string[] values)
    {
        return new EnumDefinition
        {
            Name = name, Kind = EnumKind.SchemaFixed,
            Values = [.. values.Select(v => new EnumValueDefinition { Name = v })]
        };
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

    // ── registry-based completions ───────────────────────────────────────────

    [Fact]
    public async Task Handle_TagNameContext_RegistryMappedMultiInstance_ArbitraryElementName_OffersTypeFieldTags()
    {
        // "Fighter_Mk2" is an arbitrary XML element name; the actual type is "SpaceUnit".
        // Tag-name completion must use the registry type, not the element name.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });
        schema.AddTagForType("SpaceUnit", MakeTag("Max_Speed"));
        schema.AddTagForType("SpaceUnit", MakeTag("Armor_Type"));

        host.AddOrUpdate(TestUri.ToString(),
            "<GameObjectFiles>\n  <Fighter_Mk2>\n    <\n  </Fighter_Mk2>\n</GameObjectFiles>", 1);
        var result = await handler.Handle(At(2, 5), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Max_Speed", labels);
        Assert.Contains("Armor_Type", labels);
    }

    [Fact]
    public async Task Handle_ValueCompletion_RegistryMappedMultiInstance_TypeContainerNameMatchesTagName_ReturnsEmpty()
    {
        // "Faction" is both a schema tag name and the element name for type containers.
        // The actual type name is "FactionType" (differs from element name).
        // Cursor at depth 2 (inside the type container) must not offer value completions.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("FactionType"));
        var (handler, host, schema, proposals) = Build(registry);
        schema.AddType(new GameObjectTypeDefinition { TypeName = "FactionType", NameTag = "Name" });
        schema.AddTagForType("SomeType", MakeTag("Faction"));
        proposals.ProposalsToReturn = [new ValueProposal { Label = "EMPIRE" }];

        host.AddOrUpdate(TestUri.ToString(),
            "<FactionDefs>\n  <Faction Name=\"EMPIRE\">\n    \n  </Faction>\n</FactionDefs>", 1);
        var result = await handler.Handle(At(2, 4), CancellationToken.None);

        Assert.Empty(result.Items);
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
        schema.AddEnum(StoryEventTypeWith(StoryEvent("STORY_ACCUMULATE", Param(0))));
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
        schema.AddEnum(StoryRewardTypeWith(StoryReward("CREDITS", Param(0))));
        var xml =
            "<StoryParser>\n<Event>\n<Event_Type>STORY_MOVIE_DONE</Event_Type>\n<Reward_Type>CREDITS</Reward_Type>\n<\n</Event>\n</StoryParser>";
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
        // LOCK_CONTROLS Reward_Param1 is Boolean
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryRewardTypeWith(StoryReward("LOCK_CONTROLS", Param(0, XmlValueType.Boolean))));
        var xml =
            "<StoryParser>\n<Event>\n<Event_Type>STORY_MOVIE_DONE</Event_Type>\n<Reward_Type>LOCK_CONTROLS</Reward_Type>\n<Reward_Param1>\n</Event>\n</StoryParser>";
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
        schema.AddEnum(StoryEventTypeWith(StoryEvent("STORY_ACCUMULATE", Param(0))));
        // Root is <StoryPlots>, not <StoryParser> — registry detection must still trigger story completions.
        // STORY_ACCUMULATE has 1 param, so Event_Param1 should appear (Event_Type is already present).
        var xml = "<StoryPlots>\n<Event>\n<Event_Type>STORY_ACCUMULATE</Event_Type>\n<\n</Event>\n</StoryPlots>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Label == "Event_Param1");
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

    // ── Schema-driven tag-name completions ────────────────────────────────────

    [Fact]
    public async Task BuildStoryEventTagCompletions_ConstrainedEvent_OffersOnlyDefinedParamTags()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryEventTypeWith(StoryEvent("MY_EVENT", Param(0), Param(1))));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>MY_EVENT</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Event_Param1", labels);
        Assert.Contains("Event_Param2", labels);
        Assert.DoesNotContain("Event_Param3", labels);
    }

    [Fact]
    public async Task BuildStoryEventTagCompletions_UnconstrainedEvent_OffersAllSevenParamTags()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        // StoryEvent with no params → Params = null → unconstrained
        schema.AddEnum(StoryEventTypeWith(StoryEvent("UNCONSTRAINED_EVENT")));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>UNCONSTRAINED_EVENT</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        for (var i = 1; i <= 7; i++)
            Assert.Contains($"Event_Param{i}", labels);
    }

    [Fact]
    public async Task BuildStoryParamValueCompletions_DynamicEnumParam_ReturnsEnumValues()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryEventTypeWith(StoryEvent("MY_EVENT",
            Param(0, XmlValueType.DynamicEnumValue, FlagEnum("FlagCmp", "GREATER_THAN", "LESS_THAN")))));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>MY_EVENT</Event_Type>\n<Event_Param1>\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        // cursor on line 3 after '<Event_Param1>' (char 14 is past the closing '>')
        var result = await handler.Handle(At(3, 14), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("GREATER_THAN", labels);
        Assert.Contains("LESS_THAN", labels);
    }

    // ── Type-change scenarios ─────────────────────────────────────────────────

    [Fact]
    public async Task BuildStoryEventTagCompletions_EventTypeChanged_OffersTagsForCurrentType()
    {
        // Schema has TYPE_A (1 param) and TYPE_B (3 params); doc uses TYPE_B.
        // Completions must reflect TYPE_B's param slots, not TYPE_A's.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryEventTypeWith(
            StoryEvent("TYPE_A", Param(0)),
            StoryEvent("TYPE_B", Param(0), Param(1), Param(2))));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>TYPE_B</Event_Type>\n<\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 1), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("Event_Param1", labels);
        Assert.Contains("Event_Param2", labels);
        Assert.Contains("Event_Param3", labels);
        Assert.DoesNotContain("Event_Param4", labels);
    }

    [Fact]
    public async Task BuildStoryParamValueCompletions_EventTypeChanged_ReturnsProposalsForCurrentType()
    {
        // TYPE_A: Param0 = NameReference "Planet"; TYPE_B: Param0 = DynamicEnumValue "FlagCmp".
        // Doc has Event_Type = TYPE_B; completions for Event_Param1 must use FlagCmp values.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryEventTypeWith(
            StoryEvent("TYPE_A", Param(0, XmlValueType.NameReference, refType: "Planet")),
            StoryEvent("TYPE_B",
                Param(0, XmlValueType.DynamicEnumValue, FlagEnum("FlagCmp", "GREATER_THAN", "LESS_THAN")))));
        var xml = "<StoryParser>\n<Event>\n<Event_Type>TYPE_B</Event_Type>\n<Event_Param1>\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        // cursor after '<Event_Param1>' closing '>' (char 14)
        var result = await handler.Handle(At(3, 14), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("GREATER_THAN", labels);
        Assert.Contains("LESS_THAN", labels);
        Assert.DoesNotContain("Planet", labels);
    }

    [Fact]
    public async Task BuildStoryParamValueCompletions_RewardTypeChanged_ReturnsProposalsForCurrentRewardType()
    {
        // REWARD_A: Param0 = Boolean; REWARD_B: Param0 = DynamicEnumValue "FlagCmp".
        // Doc has Reward_Type = REWARD_B; completions for Reward_Param1 must use FlagCmp values.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryRewardTypeWith(
            StoryReward("REWARD_A", Param(0, XmlValueType.Boolean)),
            StoryReward("REWARD_B",
                Param(0, XmlValueType.DynamicEnumValue, FlagEnum("FlagCmp", "GREATER_THAN", "LESS_THAN")))));
        var xml =
            "<StoryParser>\n<Event>\n<Reward_Type>REWARD_B</Reward_Type>\n<Reward_Param1>\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        // cursor after '<Reward_Param1>' closing '>' (char 15, since Reward_Param1 is 15 chars)
        var result = await handler.Handle(At(3, 15), CancellationToken.None);

        var labels = result.Items.Select(i => i.Label).ToList();
        Assert.Contains("GREATER_THAN", labels);
        Assert.Contains("LESS_THAN", labels);
        Assert.DoesNotContain("0", labels);
        Assert.DoesNotContain("1", labels);
    }

    [Fact]
    public async Task BuildStoryParamValueCompletions_ParamIndexExistsForOldTypeNotNew_ReturnsEmpty()
    {
        // TYPE_B only defines param at position 0; doc has Event_Param2 (position 1) → no proposals.
        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("StoryParser"));
        var (handler, host, schema, _) = Build(registry);
        schema.AddType(MakeType("StoryParser"));
        schema.AddEnum(StoryEventTypeWith(
            StoryEvent("TYPE_A", Param(0), Param(1)),
            StoryEvent("TYPE_B",
                Param(0, XmlValueType.DynamicEnumValue, FlagEnum("FlagCmp", "GREATER_THAN", "LESS_THAN")))));
        // TYPE_B is the current type; Event_Param2 references position 1 which TYPE_B doesn't define
        var xml = "<StoryParser>\n<Event>\n<Event_Type>TYPE_B</Event_Type>\n<Event_Param2>\n</Event>\n</StoryParser>";
        host.AddOrUpdate(TestUri.ToString(), xml, 1);

        var result = await handler.Handle(At(3, 14), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonEaWFile_ReturnsEmptyList()
    {
        var (handler, host, _, _) = Build(ctx: new DenyAllEaWContext());
        host.AddOrUpdate(TestUri.ToString(), "<Root><Foo/></Root>", 1);

        var result = await handler.Handle(At(0, 7), CancellationToken.None);

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
        private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);
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

        public EnumDefinition? GetEnum(string name)
        {
            return _enums.GetValueOrDefault(name);
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];

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

        public void AddEnum(EnumDefinition enumDef)
        {
            _enums[enumDef.Name] = enumDef;
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

    private sealed class FakeCompletionRegistry : IXmlCompletionRegistry
    {
        public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition _, string __, GameIndex ___)
        {
            return [];
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
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
}