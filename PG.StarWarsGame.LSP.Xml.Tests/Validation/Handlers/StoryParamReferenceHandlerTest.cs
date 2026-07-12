// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryParamReferenceHandlerTest
{
    private static readonly StoryParamReferenceHandler Sut = new();

    private static GameIndex IndexWith(string id, string typeName)
    {
        var sym = new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new UnknownOrigin("test"), null);
        return GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [KeyValuePair.Create(id, sym)])
            }
        };
    }

    private static DiagnosticsContext CtxWith(GameIndex index)
    {
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    private static StoryParamFact MakeFact(XmlValueType type, string value, string refType = "Planet")
    {
        return new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition
            {
                Position = 0,
                ValueType = type,
                ObjectType = new GameObjectTypeDefinition { TypeName = refType }
            }, value);
    }

    [Fact]
    public void Resolved_single_ref_emits_no_diagnostics()
    {
        var fact = MakeFact(XmlValueType.NameReference, "Coruscant");
        Assert.Empty(Sut.Handle(fact, CtxWith(IndexWith("Coruscant", "Planet"))));
    }

    [Fact]
    public void Unresolved_single_ref_emits_error()
    {
        var fact = MakeFact(XmlValueType.NameReference, "NotAPlanet");
        var results = Sut.Handle(fact, CtxWith(GameIndex.Empty)).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("NotAPlanet", d.Message);
    }

    [Fact]
    public void Resolved_ref_list_all_present_emits_no_diagnostics()
    {
        var index = IndexWith("X_Wing", "GameObjectType");
        var fact = MakeFact(XmlValueType.NameReferenceList, "X_Wing", "GameObjectType");
        Assert.Empty(Sut.Handle(fact, CtxWith(index)));
    }

    [Fact]
    public void Unresolved_token_in_ref_list_emits_error()
    {
        var fact = MakeFact(XmlValueType.NameReferenceList, "X_Wing Missing_Unit", "GameObjectType");
        var results = Sut.Handle(fact, CtxWith(IndexWith("X_Wing", "GameObjectType"))).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Missing_Unit", d.Message);
    }

    [Fact]
    public void Non_reference_value_type_emits_no_diagnostics()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition { Position = 0, ValueType = XmlValueType.Int }, "NotAPlanet");
        Assert.Empty(Sut.Handle(fact, CtxWith(GameIndex.Empty)));
    }

    [Fact]
    public void Null_object_type_emits_no_diagnostics()
    {
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition { Position = 0, ValueType = XmlValueType.NameReference, ObjectType = null },
            "anything");
        Assert.Empty(Sut.Handle(fact, CtxWith(GameIndex.Empty)));
    }

    [Fact]
    public void Null_object_type_with_referenceTypeName_still_validates()
    {
        // StoryEventType.yaml params say only "referenceType: Planet" (no referenceKind), so
        // ObjectType never resolves — the preserved raw name must drive the check instead.
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "MY_EVENT", false, 0,
            new ParamDefinition
            {
                Position = 0, ValueType = XmlValueType.NameReference,
                ReferenceTypeName = "Planet"
            }, "NotAPlanet");

        var d = Assert.Single(Sut.Handle(fact, CtxWith(GameIndex.Empty)));
        Assert.Contains("NotAPlanet", d.Message);
        Assert.Contains("Planet", d.Message);
    }

    [Fact]
    public void Story_scoped_referenceTypeName_is_not_validated_here()
    {
        // Story references resolve campaign-scoped; existence checks belong to the story graph
        // diagnostics, so an index-wide miss must stay silent.
        var fact = new StoryParamFact("file:///test.xml", 1, 0, 0, "TRIGGER_EVENT", true, 0,
            new ParamDefinition
            {
                Position = 0, ValueType = XmlValueType.NameReference,
                ReferenceTypeName = "StoryEventName"
            }, "Some_Event_Elsewhere");

        Assert.Empty(Sut.Handle(fact, CtxWith(GameIndex.Empty)));
    }

    [Fact]
    public void Tab_separated_tokens_in_ref_list_all_resolved_emits_no_diagnostics()
    {
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary.CreateRange(
                    StringComparer.OrdinalIgnoreCase,
                    [
                        KeyValuePair.Create("X_Wing",
                            new GameSymbol("X_Wing", GameSymbolKind.XmlObject, "GameObjectType",
                                new UnknownOrigin("test"), null)),
                        KeyValuePair.Create("Y_Wing",
                            new GameSymbol("Y_Wing", GameSymbolKind.XmlObject, "GameObjectType",
                                new UnknownOrigin("test"), null))
                    ])
            }
        };
        var fact = MakeFact(XmlValueType.NameReferenceList, "X_Wing\tY_Wing", "GameObjectType");

        Assert.Empty(Sut.Handle(fact, CtxWith(index)));
    }
}