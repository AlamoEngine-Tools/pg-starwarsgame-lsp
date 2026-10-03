// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A <c>ListMap</c> read the way the engine reads it, measured: every item is tried as a key
///     first, a value that does not start with a key is dropped, a repeat appends outside a variant
///     and replaces inside one, and nothing is merged or deduplicated. Each rule below is the
///     engine's behaviour, and the diagnostic says what it costs the author.
/// </summary>
public sealed class ListMapHandlerTest
{
    private static readonly XmlTagDefinition Buildables = new()
    {
        Tag = "Tactical_Buildable_Objects_Multiplayer", ValueType = XmlValueType.ListMap, MultipleAllowed = true,
        Slots =
        [
            new TupleSlotDefinition
            {
                Label = "Faction", ReferenceKind = ReferenceKind.XmlObject,
                ObjectType = new GameObjectTypeDefinition { TypeName = "Faction" }
            },
            new TupleSlotDefinition
            {
                Label = "Object", ReferenceKind = ReferenceKind.XmlObject,
                ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
            }
        ]
    };

    private static readonly XmlTagDefinition Presence = new()
    {
        Tag = "Presence_Induced_Animations", ValueType = XmlValueType.ListMap, MultipleAllowed = true,
        Slots =
        [
            new TupleSlotDefinition
            {
                Label = "Animation", ReferenceKind = ReferenceKind.Enum,
                Enum = new EnumDefinition
                {
                    Name = "AnimationType", Kind = EnumKind.SchemaFixed,
                    Values = [new EnumValueDefinition { Name = "Attention" }, new EnumValueDefinition { Name = "Idle" }]
                }
            },
            Buildables.Slots[1] with { Label = "Unit" }
        ]
    };

    private static GameIndex Index(params (string Id, string Type)[] symbols)
    {
        var map = ImmutableDictionary.Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, type) in symbols)
            map = map.Add(id, [new GameSymbol(id, GameSymbolKind.XmlObject, type, new UnknownOrigin("test"), null)]);
        return GameIndex.Empty with { WorkspaceDefinitions = map };
    }

    private static readonly GameIndex Game = Index(("Empire", "Faction"), ("Rebel", "Faction"),
        ("A", "GameObjectType"), ("B", "GameObjectType"), ("Idle", "GameObjectType"));

    private static DiagnosticsContext Ctx(GameIndex? index = null)
    {
        return new DiagnosticsContext(new EmptySchemaProvider(), index ?? Game, "file:///units.xml", "en");
    }

    // Items laid out on one line, ten columns apart, so a diagnostic's column names its item.
    private static ListMapOccurrence Occ(int line, params string[] items)
    {
        return new ListMapOccurrence(line, 0, 5, items.Select((t, i) => new ListMapItem(t, line, 10 * (i + 1))).ToList());
    }

    private static List<XmlDiagnosticResult> Run(XmlTagDefinition tag, bool variant, params ListMapOccurrence[] occurrences)
    {
        return Run(tag, variant, Ctx(), occurrences);
    }

    private static List<XmlDiagnosticResult> Run(XmlTagDefinition tag, bool variant, DiagnosticsContext ctx,
        params ListMapOccurrence[] occurrences)
    {
        var fact = new XmlListMapFact("file:///units.xml", occurrences[0].Line, 0, 5, tag, variant, occurrences);
        return new ListMapHandler().Handle(fact, ctx).ToList();
    }

    [Fact]
    public void AWellFormedValue_SeveralKeys_IsSilent()
    {
        Assert.Empty(Run(Buildables, false, Occ(3, "Empire", "A", "B", "Rebel", "A")));
    }

    [Fact]
    public void AValueThatDoesNotStartWithAKey_IsAnError_TheEngineDropsIt()
    {
        var d = Assert.Single(Run(Buildables, false, Occ(3, "Empyre", "A")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.ListMapFirstItemNotAKey, d.Id);
        Assert.Equal((3, 10), (d.OverrideLine, d.OverrideColumn));
        Assert.Equal("<Tactical_Buildable_Objects_Multiplayer> has to start with a Faction: 'Empyre' is none. The engine drops the whole value.", d.Message);
    }

    [Fact]
    public void AnEnumKey_NamesItsEnum()
    {
        var d = Assert.Single(Run(Presence, false, Occ(3, "Atention", "A")));

        Assert.Contains("an Animation (AnimationType)", d.Message);
    }

    [Fact]
    public void ARepeatThatDoesNotStartWithAKey_JoinsThePreviousKey()
    {
        // Outside a variant the engine appends a repeat to the same list, so its first item is added
        // to whatever key the previous occurrence ended on - silently.
        var d = Assert.Single(Run(Buildables, false, Occ(3, "Empire", "A", "Rebel", "B"), Occ(4, "A")));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.ListMapContinuesPreviousKey, d.Id);
        Assert.Equal(4, d.OverrideLine);
        Assert.Contains("'Rebel'", d.Message);
    }

    [Fact]
    public void AnItemNamedLikeAKey_StartsAGroup_AndSaysSo()
    {
        var d = Assert.Single(Run(Presence, false, Occ(3, "Attention", "A", "Idle", "B")));

        Assert.Equal(DiagnosticIds.ListMapItemNamesAKey, d.Id);
        Assert.Equal((3, 30), (d.OverrideLine, d.OverrideColumn));
        Assert.Contains("'Idle'", d.Message);
        Assert.Contains("'Attention'", d.Message);
    }

    [Fact]
    public void AKeyThatIsNoObject_IsJustAKey()
    {
        // "Attention" opening a second group is unusual but says nothing ambiguous: no object has
        // that name, so the author cannot have meant one.
        Assert.Single(Run(Presence, false, Occ(3, "Attention", "A", "Attention", "B")),
            d => d.Id == DiagnosticIds.ListMapRepeatedKey);
    }

    [Fact]
    public void ARepeatedKey_IsAWarning_AcrossOccurrencesToo()
    {
        var d = Assert.Single(Run(Buildables, false, Occ(3, "Empire", "A"), Occ(4, "empire", "B")));

        Assert.Equal(DiagnosticIds.ListMapRepeatedKey, d.Id);
        Assert.Equal((4, 10), (d.OverrideLine, d.OverrideColumn));
        Assert.Contains("line 4", d.Message);
    }

    [Fact]
    public void InAVariant_EveryEarlierOccurrenceIsReplaced()
    {
        var results = Run(Buildables, true, Occ(3, "Empire", "A"), Occ(4, "Rebel", "B"));

        var d = Assert.Single(results);
        Assert.Equal(DiagnosticIds.ListMapReplacedInVariant, d.Id);
        Assert.Equal(3, d.OverrideLine);
        Assert.Contains("line 5", d.Message);
    }

    [Fact]
    public void InAVariant_ARepeatThatDoesNotStartWithAKey_IsDropped_NotJoined()
    {
        // Each occurrence in a variant clears the list first, so there is no previous key to join.
        var results = Run(Buildables, true, Occ(3, "Empire", "A"), Occ(4, "A"));

        Assert.Contains(results, d => d.Id == DiagnosticIds.ListMapFirstItemNotAKey && d.OverrideLine == 4);
        Assert.DoesNotContain(results, d => d.Id == DiagnosticIds.ListMapContinuesPreviousKey);
    }

    [Fact]
    public void AnObjectKey_WithNothingIndexedOfItsType_IsNotJudged()
    {
        // Startup, or a workspace with no factions loaded: calling every key "no Faction" would be
        // a complaint about the tool's state, not the author's data.
        Assert.Empty(Run(Buildables, false, Ctx(Index(("A", "GameObjectType"))), Occ(3, "Empire", "A")));
    }

    [Fact]
    public void AValueOfTheWrongKind_IsAWarning_TheEngineAcceptsIt()
    {
        var planet = new ObjectKindDefinition { Kind = "Planet", Behaviors = ["PLANET"] };
        var planets = Buildables with
        {
            Slots = [Buildables.Slots[0], new TupleSlotDefinition
            {
                Label = "Planet", ReferenceKind = ReferenceKind.XmlObject, ReferenceTypeName = "Planet", Kind = planet
            }]
        };
        var index = Game with
        {
            WorkspaceDefinitions = Game.WorkspaceDefinitions.Add("Coruscant",
            [
                new GameSymbol("Coruscant", GameSymbolKind.XmlObject, "GameObjectType", new UnknownOrigin("test"), null,
                    null, ["PLANET"])
            ])
        };

        var d = Assert.Single(Run(planets, false, Ctx(index), Occ(3, "Empire", "Coruscant", "A")));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.ListMapValueKind, d.Id);
        Assert.Equal(30, d.OverrideColumn);
    }

    [Fact]
    public void AnAssetValue_IsCheckedByTheAssetRules()
    {
        var models = Buildables with
        {
            Slots = [Buildables.Slots[0], new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }]
        };
        var index = Game with { AssetFiles = MergedAssetFileIndex.Merge([], ["data/art/models/a.alo"]) };

        var d = Assert.Single(Run(models, false, Ctx(index), Occ(3, "Empire", "A.ALO", "GONE.ALO")));

        Assert.Equal(DiagnosticIds.TupleSlotModelFileExistence, d.Id);
        Assert.Equal(30, d.OverrideColumn);
    }
}
