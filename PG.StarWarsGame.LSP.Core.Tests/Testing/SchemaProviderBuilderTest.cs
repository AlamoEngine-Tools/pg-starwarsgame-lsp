// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Core.Tests.Testing;

/// <summary>
///     The in-memory schema every suite sets up from: it answers the lookups the way the real
///     index does - case-insensitively, the first declaration of a tag name winning
///     <c>GetTag</c>, every declaration listed by <c>GetAllTagDefinitions</c>, and a type's tags
///     being the ones declared on it.
/// </summary>
public sealed class SchemaProviderBuilderTest
{
    private static XmlTagDefinition Tag(string name, XmlValueType type = XmlValueType.Float)
    {
        return new XmlTagDefinition { Tag = name, ValueType = type };
    }

    [Fact]
    public void Empty_KnowsNothing()
    {
        var schema = new SchemaProviderBuilder().Build();

        Assert.Null(schema.GetTag("Max_Speed"));
        Assert.Empty(schema.GetAllTagDefinitions("Max_Speed"));
        Assert.Null(schema.GetObjectType("SpaceUnit"));
        Assert.Empty(schema.GetTagsForType("SpaceUnit"));
        Assert.Null(schema.GetEnum("Damage_Type"));
        Assert.Null(schema.GetKind("planet"));
        Assert.Empty(schema.AllTags);
        Assert.Empty(schema.AllObjectTypes);
        Assert.Empty(schema.AllEnums);
        Assert.Empty(schema.AllHardcodedSets);
        Assert.Empty(schema.AllMetafiles);
        Assert.Empty(schema.AllScannedDirectories);
        Assert.Empty(schema.AllKinds);
        Assert.True(schema.ReadyAsync.IsCompletedSuccessfully);
    }

    [Fact]
    public void GetTag_FindsATagCaseInsensitively()
    {
        var speed = Tag("Max_Speed", XmlValueType.Float);
        var schema = new SchemaProviderBuilder().WithTag(speed).Build();

        Assert.Same(speed, schema.GetTag("max_speed"));
        Assert.Same(speed, schema.GetTag("MAX_SPEED"));
        Assert.Equal([speed], schema.AllTags);
    }

    [Fact]
    public void GetTagsForType_ReturnsTheTagsDeclaredOnThatType()
    {
        var speed = Tag("Max_Speed");
        var hull = Tag("Tactical_Health");
        var bone = Tag("Attachment_Bone");
        var schema = new SchemaProviderBuilder()
            .WithTags("SpaceUnit", speed, hull)
            .WithTags("HardPoint", bone)
            .Build();

        Assert.Equal([speed, hull], schema.GetTagsForType("spaceunit"));
        Assert.Equal([bone], schema.GetTagsForType("HardPoint"));
        Assert.Empty(schema.GetTagsForType("Planet"));
        Assert.Same(bone, schema.GetTag("Attachment_Bone"));
        Assert.Equal(3, schema.AllTags.Count);
    }

    [Fact]
    public void GetTag_IsTheFirstDeclaration_AndGetAllTagDefinitions_IsEveryOne()
    {
        // The real index keeps the first declaration for the context-free lookup and lists all of
        // them for the one that knows its type.
        var onUnit = Tag("Name", XmlValueType.NameReference);
        var onPlanet = Tag("Name", XmlValueType.Int);
        var schema = new SchemaProviderBuilder()
            .WithTags("SpaceUnit", onUnit)
            .WithTags("Planet", onPlanet)
            .Build();

        Assert.Same(onUnit, schema.GetTag("name"));
        Assert.Equal([onUnit, onPlanet], schema.GetAllTagDefinitions("NAME"));
        Assert.Single(schema.AllTags);
    }

    [Fact]
    public void WithObjectType_WithEnum_WithKind_AreFoundByName()
    {
        var unit = new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" };
        var damage = new EnumDefinition { Name = "Damage_Type", Values = [] };
        var planet = new ObjectKindDefinition { Kind = "planet", Behaviors = ["PLANET"] };
        var schema = new SchemaProviderBuilder()
            .WithObjectType(unit)
            .WithEnum(damage)
            .WithKind(planet)
            .Build();

        Assert.Same(unit, schema.GetObjectType("spaceunit"));
        Assert.Same(damage, schema.GetEnum("DAMAGE_TYPE"));
        Assert.Same(planet, schema.GetKind("Planet"));
        Assert.Equal([unit], schema.AllObjectTypes);
        Assert.Equal([damage], schema.AllEnums);
        Assert.Equal([planet], schema.AllKinds);
    }

    [Fact]
    public void WithEnums_AddsEveryOne_InOrder()
    {
        var events = new EnumDefinition { Name = "StoryEventType", Values = [] };
        var rewards = new EnumDefinition { Name = "StoryRewardType", Values = [] };
        var schema = new SchemaProviderBuilder().WithEnums(events, rewards).Build();

        Assert.Equal([events, rewards], schema.AllEnums);
        Assert.Same(rewards, schema.GetEnum("storyrewardtype"));
    }

    [Fact]
    public void WithHardcodedSet_WithMetafile_WithScannedDirectory_AreListed()
    {
        var set = new HardcodedReferenceSet { Name = "Factions" };
        var metafile =
            new MetafileDefinition("Data/XML/GameObjectFiles.xml", MetafileType.FileRegistry, ["GameObject"]);
        var scanned = new ScannedDirectoryDefinition("Data/XML/AI/", ["AI"]);
        var schema = new SchemaProviderBuilder()
            .WithHardcodedSet(set)
            .WithMetafile(metafile)
            .WithScannedDirectory(scanned)
            .Build();

        Assert.Equal([set], schema.AllHardcodedSets);
        Assert.Equal([metafile], schema.AllMetafiles);
        Assert.Equal([scanned], schema.AllScannedDirectories);
    }

    [Fact]
    public void WithMetafiles_AddsEveryOne_InOrder()
    {
        var files = new MetafileDefinition("Data/XML/GameObjectFiles.xml", MetafileType.FileRegistry, ["GameObject"]);
        var constants = new MetafileDefinition("Data/XML/GameConstants.xml", MetafileType.Singleton, ["GameConstants"]);

        var schema = new SchemaProviderBuilder().WithMetafiles(files, constants).Build();

        Assert.Equal([files, constants], schema.AllMetafiles);
    }

    [Fact]
    public void Add_AfterBuild_RegistersTheEntryForTheCodeAlreadyHoldingTheProvider()
    {
        // Many handler tests wire the handler first and declare the one tag the test is about
        // afterwards; the provider they hold has to see it.
        var schema = new SchemaProviderBuilder().Build();
        var speed = Tag("Max_Speed");
        var unit = new GameObjectTypeDefinition { TypeName = "SpaceUnit" };
        var damage = new EnumDefinition { Name = "Damage_Type", Values = [] };

        schema.Add(speed).Add(unit).Add(damage).AddForType("SpaceUnit", Tag("Tactical_Health"));

        Assert.Same(speed, schema.GetTag("max_speed"));
        Assert.Same(unit, schema.GetObjectType("spaceunit"));
        Assert.Same(damage, schema.GetEnum("damage_type"));
        Assert.Single(schema.GetTagsForType("SpaceUnit"));
        Assert.Equal(2, schema.AllTags.Count);
    }

    [Fact]
    public void Add_OfATypeAlreadyRegistered_ReplacesIt()
    {
        var schema = new SchemaProviderBuilder()
            .WithObjectType(new GameObjectTypeDefinition { TypeName = "SpaceUnit" })
            .Build();
        var replacement = new GameObjectTypeDefinition { TypeName = "spaceunit", NameTag = "Name" };

        schema.Add(replacement);

        Assert.Same(replacement, schema.GetObjectType("SpaceUnit"));
        Assert.Equal([replacement], schema.AllObjectTypes);
    }

    [Fact]
    public void Refresh_RaisesSchemaRefreshed()
    {
        var schema = new SchemaProviderBuilder().Build();
        var raised = 0;
        schema.SchemaRefreshed += (_, _) => raised++;

        schema.Refresh();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void WithReady_IsTheProvidersReadyTask()
    {
        var ready = new TaskCompletionSource();
        var schema = new SchemaProviderBuilder().WithReady(ready.Task).Build();

        Assert.False(schema.ReadyAsync.IsCompleted);
        ready.SetResult();
        Assert.True(schema.ReadyAsync.IsCompletedSuccessfully);
    }
}