// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class SingleValueTypeHandlerBaseTest
{
    private static readonly DiagnosticsContext Ctx =
        new(new SchemaProviderBuilder().Build(), GameIndex.Empty, "file:///test.xml", "en")
            { Messages = TestCatalogues.English };

    private static XmlTagValueFact Fact(XmlValueType valueType)
    {
        var tag = new XmlTagDefinition { Tag = "Any", ValueType = valueType };
        return new XmlTagValueFact("file:///test.xml", 0, 0, 1, tag, "value");
    }

    [Fact]
    public void HandledValueType_reflects_TargetType()
    {
        var sut = new RecordingHandler();
        Assert.Equal(XmlValueType.Float, sut.HandledValueType);
    }

    [Fact]
    public void Wrong_value_type_does_not_delegate_and_returns_empty()
    {
        var sut = new RecordingHandler();

        var results = sut.Handle(Fact(XmlValueType.Boolean), Ctx).ToList();

        Assert.Empty(results);
        Assert.False(sut.Delegated);
    }

    [Fact]
    public void Matching_value_type_delegates_to_HandleValue()
    {
        var sut = new RecordingHandler();

        var results = sut.Handle(Fact(XmlValueType.Float), Ctx).ToList();

        Assert.True(sut.Delegated);
        var single = Assert.Single(results);
        Assert.Equal("delegated", single.Message);
    }

    private sealed class RecordingHandler : SingleValueTypeHandlerBase
    {
        public bool Delegated { get; private set; }
        protected override XmlValueType TargetType => XmlValueType.Float;

        protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            Delegated = true;
            return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "delegated")];
        }
    }
}