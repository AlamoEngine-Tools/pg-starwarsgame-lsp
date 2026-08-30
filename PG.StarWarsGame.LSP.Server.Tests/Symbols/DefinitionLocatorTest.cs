// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests.Symbols;

/// <summary>
///     Turning a reference VALUE into the place it is defined.
/// </summary>
/// <remarks>
///     This logic used to live inside the story editor's own handler, where it was reachable only
///     while the story feature flag was on. Nothing about it is story-specific - it is "given a name
///     and the type it is expected to be, where is that written" - and the Gameplay lens' ability
///     rows need exactly the same answer for a <c>SpecialAbility</c>. The behaviour here is the
///     behaviour the story handler had; the tests are the same cases, moved down with it.
/// </remarks>
public sealed class DefinitionLocatorTest
{
    /// <remarks>
    ///     <c>GameObjectType</c> is in here deliberately. The real schema declares it, so a locator
    ///     that only asked "is this a declared type" would happily prefer it - and then find nothing,
    ///     because no symbol is ever indexed under the umbrella. A fake that did not know the name
    ///     would let that regression through.
    /// </remarks>
    private sealed class TypedSchemaProvider : NullSchemaProvider
    {
        public override GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return typeName is "StoryEvent" or "SpecialAbility" or "Planet" or "GameObjectType"
                ? new GameObjectTypeDefinition { TypeName = typeName }
                : null;
        }
    }

    private static DefinitionLocator LocatorFor(GameIndex index)
    {
        return new DefinitionLocator(new FakeGameIndexService(index), new TypedSchemaProvider());
    }

    private static GameIndex IndexWith(params (string Id, string TypeName)[] symbols)
    {
        var definitions = GameIndex.Empty.WorkspaceDefinitions;
        foreach (var (id, typeName) in symbols)
            definitions = definitions.Add(id, [
                new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
                    new FileOrigin($"file:///ws/data/xml/{typeName.ToLowerInvariant()}s.xml", 7, 2), null)
            ]);
        return GameIndex.Empty with { WorkspaceDefinitions = definitions };
    }

    [Fact]
    public void WorkspaceSymbol_ReturnsItsFileLocation()
    {
        var located = LocatorFor(IndexWith(("Coruscant", "Planet"))).Locate("Coruscant", "Planet");

        Assert.Null(located.Error);
        Assert.Equal("file:///ws/data/xml/planets.xml", located.Uri);
        Assert.Equal(7, located.Line);
        Assert.Equal(2, located.Column);
    }

    // Same id defined twice - the expected type decides, not whichever layer ranks higher.
    [Fact]
    public void TypedPreference_PicksTheMatchingDefinition()
    {
        var definitions = GameIndex.Empty.WorkspaceDefinitions.Add("Start", [
            new GameSymbol("Start", GameSymbolKind.XmlObject, "SpaceUnit",
                new FileOrigin("file:///ws/data/xml/units.xml", 1, 0), null),
            new GameSymbol("Start", GameSymbolKind.XmlObject, "StoryEvent",
                new FileOrigin("file:///ws/data/xml/story_act_i.xml", 42, 15), null)
        ]);

        var located = LocatorFor(GameIndex.Empty with { WorkspaceDefinitions = definitions })
            .Locate("Start", StoryReferenceTypes.EventName);

        Assert.Equal("file:///ws/data/xml/story_act_i.xml", located.Uri);
        Assert.Equal(42, located.Line);
    }

    // Abilities are indexed as "OWNER$name" while every reference to one carries the bare name.
    // This is the case the ability rows depend on.
    [Fact]
    public void ScopedAbilityId_ResolvesFromTheBareName()
    {
        var located = LocatorFor(IndexWith(("MY_UNIT$Medic_Healing", "UnitAbility")))
            .Locate("Medic_Healing", "SpecialAbility");

        Assert.Null(located.Error);
        Assert.Equal("file:///ws/data/xml/unitabilitys.xml", located.Uri);
    }

    [Fact]
    public void NonFileOrigin_ExplainsWhyThereIsNothingToOpen()
    {
        var definitions = GameIndex.Empty.WorkspaceDefinitions.Add("Coruscant", [
            new GameSymbol("Coruscant", GameSymbolKind.XmlObject, "Planet", new UnknownOrigin("meg"), null)
        ]);

        var located = LocatorFor(GameIndex.Empty with { WorkspaceDefinitions = definitions })
            .Locate("Coruscant", "Planet");

        Assert.Null(located.Uri);
        Assert.Contains("base game", located.Error);
    }

    [Fact]
    public void UnknownValue_SaysSoAndNamesIt()
    {
        var located = LocatorFor(IndexWith()).Locate("Ghost", "Planet");

        Assert.Null(located.Uri);
        Assert.Contains("Ghost", located.Error);
    }

    [Fact]
    public void BlankValue_IsRejectedBeforeTheIndexIsTouched()
    {
        var located = LocatorFor(IndexWith()).Locate("   ", "Planet");

        Assert.Null(located.Uri);
        Assert.NotNull(located.Error);
    }

    // An umbrella name is not a symbol TypeName - nothing is ever indexed under GameObjectType -
    // so a reference declared that way still has to land on the concrete definition. Two things
    // hold this up: the locator refuses to prefer the umbrella, and the index's typed lookup falls
    // back to the untyped winner when no type matches. Either alone would do; this pins the result.
    [Fact]
    public void GameObjectTypeUmbrella_FallsBackToTheUntypedWinner()
    {
        var located = LocatorFor(IndexWith(("Y_Wing", "SpaceUnit"))).Locate("Y_Wing", "GameObjectType");

        Assert.Null(located.Error);
        Assert.Equal("file:///ws/data/xml/spaceunits.xml", located.Uri);
    }
}
