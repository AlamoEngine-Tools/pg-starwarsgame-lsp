// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Tests.Graph;

public sealed class StoryGraphDiagnosticsProducerTest
{
    private const string UriA = "file:///ws/data/xml/story_a.xml";
    private const string UriB = "file:///ws/data/xml/story_b.xml";

    private static readonly ISchemaProvider Schema = new DiagnosticsSchemaProvider();

    private static StoryCampaignModel Model(
        IReadOnlyList<(string Uri, string Inner)> threads,
        IReadOnlyList<string>? suspendedUris = null,
        IReadOnlyList<string>? luaScripts = null)
    {
        var parsed = threads
            .Select(t => StoryThreadParser.Parse($"<Story>{t.Inner}</Story>", t.Uri))
            .ToList();
        return new StoryCampaignModel("GC", parsed,
            (suspendedUris ?? []).ToHashSet(StringComparer.Ordinal),
            new StoryGraphBuilder(Schema).Build(parsed)) { LuaScripts = luaScripts ?? [] };
    }

    private static IReadOnlyList<StoryGraphDiagnostic> Produce(StoryCampaignModel model, string uri = UriA)
    {
        return new StoryGraphDiagnosticsProducer(Schema).Produce(model, uri);
    }

    [Fact]
    public void DuplicateEventNames_ErrorOnEveryOccurrence()
    {
        var model = Model([(UriA, "<Event Name=\"Twice\"/><Event Name=\"twice\"/>")]);

        var duplicates = Produce(model)
            .Where(d => d.Message.Contains("defined 2 times")).ToList();

        Assert.Equal(2, duplicates.Count);
        Assert.All(duplicates, d => Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void PrereqCycle_WarnsOnMembersInThisDocumentOnly()
    {
        var model = Model([
            (UriA, "<Event Name=\"A\"><Prereq>B</Prereq></Event>"),
            (UriB, "<Event Name=\"B\"><Prereq>A</Prereq></Event>")
        ]);

        var cycleWarnings = Produce(model).Where(d => d.Message.Contains("prerequisite cycle")).ToList();

        var warning = Assert.Single(cycleWarnings);
        Assert.Contains("'A'", warning.Message);
        Assert.Equal(XmlDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void UnreachableEvent_Warns()
    {
        var model = Model([(UriA, "<Event Name=\"B\"><Prereq>Ghost</Prereq></Event>")]);

        var diags = Produce(model);

        Assert.Contains(diags, d => d.Message.Contains("can never fire"));
        // The dangling prereq itself surfaces as an error too.
        Assert.Contains(diags, d => d.Message.Contains("'Ghost'") && d.Severity == XmlDiagnosticSeverity.Error);
    }

    [Fact]
    public void UnreachableEvents_AreNotFlaggedInSuspendedThreads()
    {
        var model = Model([(UriA, "<Event Name=\"B\"><Prereq>Ghost</Prereq></Event>")],
            suspendedUris: [UriA]);

        Assert.DoesNotContain(Produce(model), d => d.Message.Contains("can never fire"));
    }

    [Fact]
    public void SuspendedThread_NothingActivatesIt_InformationDiagnostic()
    {
        var model = Model([
            (UriA, "<Event Name=\"B\"/>"),
            (UriB, "<Event Name=\"Main\"/>")
        ], suspendedUris: [UriA]);

        var diag = Assert.Single(Produce(model), d => d.Message.Contains("suspended"));
        Assert.Equal(XmlDiagnosticSeverity.Information, diag.Severity);
        Assert.Equal(0, diag.Line);
    }

    [Fact]
    public void SuspendedThread_ActivatedByStoryElement_NoDiagnostic()
    {
        var model = Model([
            (UriA, "<Event Name=\"B\"/>"),
            (UriB, "<Event Name=\"Main\"><Reward_Type>STORY_ELEMENT</Reward_Type>" +
                   "<Reward_Param1>story_a</Reward_Param1></Event>")
        ], suspendedUris: [UriA]);

        Assert.DoesNotContain(Produce(model), d => d.Message.Contains("suspended"));
    }

    [Fact]
    public void SuspendedThread_DrivenByAttachedLuaScript_NoDiagnostic()
    {
        var model = Model([(UriA, "<Event Name=\"B\"/>")],
            suspendedUris: [UriA], luaScripts: ["Story_A"]);

        Assert.DoesNotContain(Produce(model), d => d.Message.Contains("suspended"));
    }

    [Fact]
    public void TagOrder_RewardBeforeEventType_Warns()
    {
        var model = Model([(UriA,
            "<Event Name=\"E\"><Reward_Type>STORY_ELEMENT</Reward_Type>" +
            "<Event_Type>STORY_TRIGGER</Event_Type></Event>")]);

        var warning = Assert.Single(Produce(model), d => d.Message.Contains("tag order"));
        Assert.Contains("'Event_Type'", warning.Message);
        Assert.Contains("'Reward_Type'", warning.Message);
    }

    [Fact]
    public void TagOrder_DocumentedOrder_NoDiagnostic()
    {
        var model = Model([(UriA,
            "<Event Name=\"E\"><Event_Type>STORY_TRIGGER</Event_Type>" +
            "<Reward_Type>STORY_ELEMENT</Reward_Type><Reward_Param1>x</Reward_Param1>" +
            "<Prereq>E</Prereq><Branch>B1</Branch><Perpetual>true</Perpetual></Event>")]);

        Assert.DoesNotContain(Produce(model), d => d.Message.Contains("tag order"));
    }

    [Fact]
    public void FlagName_LongerThan31Characters_Errors()
    {
        var longFlag = new string('F', 32);
        var model = Model([(UriA,
            $"<Event Name=\"E\"><Reward_Type>SET_FLAG</Reward_Type>" +
            $"<Reward_Param1>{longFlag}</Reward_Param1></Event>")]);

        var error = Assert.Single(Produce(model), d => d.Message.Contains("31"));
        Assert.Equal(XmlDiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void AmbiguousTarget_SurfacesAsWarning()
    {
        var model = Model([
            (UriA,
                "<Event Name=\"Twin\"/>" +
                "<Event Name=\"Src\"><Reward_Type>TRIGGER_EVENT</Reward_Type>" +
                "<Reward_Param1>Twin</Reward_Param1></Event>"),
            (UriB, "<Event Name=\"Twin\"/>")
        ]);

        var ambiguity = Assert.Single(Produce(model), d => d.Message.Contains("matches 2 events"));
        Assert.Equal(XmlDiagnosticSeverity.Warning, ambiguity.Severity);
    }

    [Fact]
    public void ProblemsOfOtherDocuments_AreNotIncluded()
    {
        var model = Model([
            (UriA, "<Event Name=\"A\"/>"),
            (UriB, "<Event Name=\"B\"><Prereq>Ghost</Prereq></Event>")
        ]);

        Assert.Empty(Produce(model, UriA));
    }
}

file sealed class DiagnosticsSchemaProvider : ISchemaProvider
{
    private static readonly EnumDefinition Rewards = new()
    {
        Name = "StoryRewardType",
        Values =
        [
            new EnumValueDefinition
            {
                Name = "TRIGGER_EVENT",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = "StoryEventName"
                    }
                ]
            },
            new EnumValueDefinition
            {
                Name = "SET_FLAG",
                Params =
                [
                    new ParamDefinition
                    {
                        Position = 0, ValueType = XmlValueType.NameReference,
                        ReferenceTypeName = "StoryFlag"
                    }
                ]
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
    public IReadOnlyList<EnumDefinition> AllEnums => [Rewards];
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
        return string.Equals(name, Rewards.Name, StringComparison.OrdinalIgnoreCase) ? Rewards : null;
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}
