// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Parsing;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests.Parsing;

public sealed class XmlStorySymbolCollectionTest
{
    private const string Uri = "file:///ws/data/xml/story_test.xml";

    private static async Task<DocumentIndex> ParseAsync(string xml,
        bool typedAsStoryParser = true, bool symbolsFlag = true)
    {
        var registry = new StubRegistry(typedAsStoryParser ? ["StoryParser"] : []);
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Story = new StoryFeatureFlags { Symbols = symbolsFlag } });
        var parser = new XmlGameDocumentParser(new FileHelper(new MockFileSystem()),
            new StoryEnumSchemaProvider(), registry, NullLogger<XmlGameDocumentParser>.Instance,
            configProvider: config);
        return await parser.ParseAsync(Uri, xml, 1, CancellationToken.None);
    }

    // ── Definitions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EventName_EmitsStoryEventSymbol_AtNameValue()
    {
        var index = await ParseAsync("<Story>\n<Event Name=\"Rebel_M01_Start\">\n</Event>\n</Story>");

        var symbol = Assert.Single(index.Symbols, s => s.TypeName == "StoryEvent");
        Assert.Equal("Rebel_M01_Start", symbol.Id);
        Assert.Equal(GameSymbolKind.XmlObject, symbol.Kind);
        var origin = Assert.IsType<FileOrigin>(symbol.Origin);
        Assert.Equal(1, origin.Line);
        Assert.Equal("<Event Name=\"".Length, origin.Column);
    }

    [Fact]
    public async Task SetFlagParam_EmitsStoryFlagSymbol()
    {
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Reward_Type>SET_FLAG</Reward_Type>" +
            "<Reward_Param1>REBEL_TRAP_SET</Reward_Param1></Event></Story>");

        var flag = Assert.Single(index.Symbols, s => s.TypeName == "StoryFlag");
        Assert.Equal("REBEL_TRAP_SET", flag.Id);
    }

    // ── References ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerEventParam_EmitsStoryEventReference()
    {
        var index = await ParseAsync(
            "<Story>\n<Event Name=\"Src\">\n<Reward_Type>TRIGGER_EVENT</Reward_Type>\n" +
            "<Reward_Param1>Target_Event</Reward_Param1>\n</Event>\n</Story>");

        var reference = Assert.Single(index.References, r => r.ExpectedTypeName == "StoryEvent");
        Assert.Equal("Target_Event", reference.TargetId);
        Assert.Equal(3, reference.Line);
        Assert.Equal("<Reward_Param1>".Length, reference.Column);
        Assert.Equal("Target_Event".Length, reference.Length);
    }

    [Fact]
    public async Task PrereqTokens_EmitColumnAccurateStoryEventReferences()
    {
        var index = await ParseAsync(
            "<Story>\n<Event Name=\"E\">\n<Prereq>Alpha Beta</Prereq>\n</Event>\n</Story>");

        var refs = index.References.Where(r => r.ExpectedTypeName == "StoryEvent").ToList();
        Assert.Equal(["Alpha", "Beta"], refs.Select(r => r.TargetId));
        Assert.Equal("<Prereq>Alpha ".Length, refs[1].Column);
        Assert.Equal(2, refs[1].Line);
    }

    [Fact]
    public async Task AiNotificationParam_EmitsStoryNotificationReferences_PerToken()
    {
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Event_Type>STORY_AI_NOTIFICATION</Event_Type>" +
            "<Event_Param2>Ping_A Ping_B</Event_Param2></Event></Story>");

        var refs = index.References.Where(r => r.ExpectedTypeName == "StoryNotification").ToList();
        Assert.Equal(["Ping_A", "Ping_B"], refs.Select(r => r.TargetId));
    }

    [Fact]
    public async Task ObjectTypedParam_KnownObjectType_EmitsTypedReference()
    {
        // A Planet-typed reward param navigates with the concrete type (mismatch checks apply).
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Reward_Type>SPAWN_UNIT</Reward_Type>" +
            "<Reward_Param1>Mon_Calamari_Cruiser</Reward_Param1>" +
            "<Reward_Param2>Kuat</Reward_Param2></Event></Story>");

        var planet = Assert.Single(index.References, r => r.TargetId == "Kuat");
        Assert.Equal("Planet", planet.ExpectedTypeName);
        Assert.Equal("Kuat".Length, planet.Length);
    }

    [Fact]
    public async Task ObjectTypedParam_UmbrellaType_EmitsUntypedReference()
    {
        // "GameObjectType" matches no concrete symbol TypeName - the reference resolves untyped
        // so any unit/structure/hero counts and no bogus type-mismatch fires.
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Reward_Type>SPAWN_UNIT</Reward_Type>" +
            "<Reward_Param1>Mon_Calamari_Cruiser</Reward_Param1></Event></Story>");

        var unit = Assert.Single(index.References, r => r.TargetId == "Mon_Calamari_Cruiser");
        Assert.Null(unit.ExpectedTypeName);
    }

    [Fact]
    public async Task IncrementFlagParam_IsAReference_NotADefinition()
    {
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Reward_Type>INCREMENT_FLAG</Reward_Type>" +
            "<Reward_Param1>COUNTER</Reward_Param1></Event></Story>");

        Assert.DoesNotContain(index.Symbols, s => s.TypeName == "StoryFlag");
        var reference = Assert.Single(index.References, r => r.ExpectedTypeName == "StoryFlag");
        Assert.Equal("COUNTER", reference.TargetId);
    }

    // ── Gating ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DocumentNotTypedStoryParser_EmitsNoStorySymbols()
    {
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Prereq>A</Prereq></Event></Story>",
            false);

        Assert.DoesNotContain(index.Symbols, s => s.TypeName == "StoryEvent");
        Assert.DoesNotContain(index.References, r => r.ExpectedTypeName == "StoryEvent");
    }

    [Fact]
    public async Task SymbolsFlagOff_EmitsNoStorySymbols()
    {
        var index = await ParseAsync(
            "<Story><Event Name=\"E\"><Prereq>A</Prereq></Event></Story>",
            symbolsFlag: false);

        Assert.DoesNotContain(index.Symbols, s => s.TypeName == "StoryEvent");
        Assert.DoesNotContain(index.References, r => r.ExpectedTypeName == "StoryEvent");
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubRegistry(string[] types) : IFileTypeRegistry
    {
        public ImmutableArray<string> GetTypesForFile(string fileUri)
        {
            return [.. types];
        }

        public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
        {
        }

        public void UnregisterFile(string fileUri)
        {
        }

        public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
            new Dictionary<string, ImmutableArray<string>>();
    }

    private sealed class StoryEnumSchemaProvider : ISchemaProvider
    {
        private static readonly EnumDefinition Events = new()
        {
            Name = "StoryEventType",
            Values =
            [
                new EnumValueDefinition { Name = "STORY_AI_NOTIFICATION", Params = [Param(1, "StoryNotification")] },
                new EnumValueDefinition { Name = "STORY_FLAG", Params = [Param(0, "StoryFlag")] }
            ]
        };

        private static readonly EnumDefinition Rewards = new()
        {
            Name = "StoryRewardType",
            Values =
            [
                new EnumValueDefinition { Name = "TRIGGER_EVENT", Params = [Param(0, "StoryEventName")] },
                new EnumValueDefinition { Name = "SET_FLAG", Params = [Param(0, "StoryFlag")] },
                new EnumValueDefinition { Name = "INCREMENT_FLAG", Params = [Param(0, "StoryFlag")] },
                new EnumValueDefinition
                {
                    Name = "SPAWN_UNIT",
                    Params = [Param(0, "GameObjectType"), Param(1, "Planet")]
                }
            ]
        };

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [Events, Rewards];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string name)
        {
            if (string.Equals(name, Events.Name, StringComparison.OrdinalIgnoreCase)) return Events;
            if (string.Equals(name, Rewards.Name, StringComparison.OrdinalIgnoreCase)) return Rewards;
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            // "Planet" is a real types.yaml object type; "GameObjectType" is an umbrella that
            // no concrete symbol carries - mirrors the production schema.
            return string.Equals(t, "Planet", StringComparison.OrdinalIgnoreCase)
                ? new GameObjectTypeDefinition { TypeName = "Planet" }
                : null;
        }

        private static ParamDefinition Param(int position, string referenceType)
        {
            return new ParamDefinition
            {
                Position = position, ValueType = XmlValueType.NameReference,
                ReferenceTypeName = referenceType
            };
        }
    }
}