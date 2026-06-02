// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Linq;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Characterization test that locks in the value-type gate of every single-value-type
///     <see cref="IXmlDiagnosticsHandler" />: when the fact's <see cref="XmlTagValueFact.Tag" />
///     has a <see cref="XmlValueType" /> different from the handler's
///     <see cref="IXmlDiagnosticsHandler.HandledValueType" />, the handler must emit no
///     diagnostics. This pins current behaviour so the later base-class refactor stays
///     provably behaviour-preserving.
/// </summary>
public sealed class ValueTypeGateCharacterizationTest
{
    public static TheoryData<Type> GatedHandlers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in GuardedValueHandlerCases.HandlerTypes)
            data.Add(type);
        return data;
    }

    [Fact]
    public void Discovers_the_guarded_value_handlers()
    {
        // Sanity floor: there are 53 guarded handlers plus 6 no-op handlers in the tree.
        // A regression that silently breaks reflection (e.g. wrong assembly) would surface here.
        Assert.True(GuardedValueHandlerCases.HandlerTypes.Count >= 50,
            $"Expected at least 50 single-value-type handlers, found {GuardedValueHandlerCases.HandlerTypes.Count}.");
    }

    [Theory]
    [MemberData(nameof(GatedHandlers))]
    public void Returns_no_diagnostics_when_value_type_does_not_match(Type handlerType)
    {
        var handler = GuardedValueHandlerCases.Create(handlerType);
        var handled = handler.HandledValueType!.Value;

        // Pick a deliberately wrong (and non-obsolete) value type for this handler.
        var wrongType = handled == XmlValueType.Boolean ? XmlValueType.Float : XmlValueType.Boolean;

        var tag = XmlHandlerTestFixtures.MakeTag("AnyTag", wrongType);
        var fact = XmlHandlerTestFixtures.MakeFact(tag, "any-value");

        var results = handler.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        Assert.Empty(results);
    }
}
