// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

public sealed class XmlDiagnosticsHandlerRegistryTest
{
    private static readonly XmlTagDefinition FloatTag =
        new() { Tag = "Speed", ValueType = XmlValueType.Float };

    private static readonly DiagnosticsContext EmptyCtx = new(
        new StubSchemaProvider(),
        GameIndex.Empty,
        "file:///test.xml",
        "en");

    // ── empty registry ───────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_NoHandlers_ReturnsEmpty()
    {
        var registry = new XmlDiagnosticsHandlerRegistry([]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 5, FloatTag, "bad");

        Assert.Empty(registry.Dispatch(fact, EmptyCtx));
    }

    [Fact]
    public void DispatchAll_EmptyFacts_ReturnsEmpty()
    {
        var registry = new XmlDiagnosticsHandlerRegistry([]);

        Assert.Empty(registry.DispatchAll([], EmptyCtx));
    }

    // ── routing ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_HandlerForWrongFactType_NotCalled()
    {
        var called = false;
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlNotesFact>((_, _) =>
            {
                called = true;
                return [];
            })
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 5, FloatTag, "bad");

        registry.Dispatch(fact, EmptyCtx);

        Assert.False(called);
    }

    [Fact]
    public void Dispatch_HandlerForCorrectFactType_ReturnsResult()
    {
        var result = new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "bad value");
        var registry = new XmlDiagnosticsHandlerRegistry(
            [new LambdaHandler<XmlTagValueFact>((_, _) => [result])]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, FloatTag, "bad");

        var results = registry.Dispatch(fact, EmptyCtx).ToList();

        var single = Assert.Single(results);
        Assert.Equal("bad value", single.Message);
        Assert.Equal(XmlDiagnosticSeverity.Error, single.Severity);
    }

    [Fact]
    public void Dispatch_MultipleHandlersSameFactType_AllCalled()
    {
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) => [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "h1")]),
            new LambdaHandler<XmlTagValueFact>((_, _) => [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "h2")])
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, FloatTag, "bad");

        var results = registry.Dispatch(fact, EmptyCtx).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message == "h1");
        Assert.Contains(results, r => r.Message == "h2");
    }

    [Fact]
    public void DispatchAll_MultipleFacts_AggregatesAllResults()
    {
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((f, _) =>
                [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, $"warn:{f.RawValue}")])
        ]);

        var facts = new XmlFact[]
        {
            new XmlTagValueFact("file:///test.xml", 0, 0, 1, FloatTag, "x"),
            new XmlTagValueFact("file:///test.xml", 1, 0, 1, FloatTag, "y")
        };

        var results = registry.DispatchAll(facts, EmptyCtx).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message == "warn:x");
        Assert.Contains(results, r => r.Message == "warn:y");
    }

    // ── context forwarded ────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_ContextPassedToHandler()
    {
        DiagnosticsContext? received = null;
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, c) =>
            {
                received = c;
                return [];
            })
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, FloatTag, "bad");

        registry.Dispatch(fact, EmptyCtx).ToList();

        Assert.Same(EmptyCtx, received);
    }
}

// ── fakes ────────────────────────────────────────────────────────────────────

file sealed class LambdaHandler<TFact> : XmlDiagnosticsHandler<TFact>
    where TFact : XmlFact
{
    private readonly Func<TFact, DiagnosticsContext, IEnumerable<XmlDiagnosticResult>> _fn;

    public LambdaHandler(Func<TFact, DiagnosticsContext, IEnumerable<XmlDiagnosticResult>> fn)
    {
        _fn = fn;
    }

    protected override IEnumerable<XmlDiagnosticResult> Handle(TFact fact, DiagnosticsContext ctx)
    {
        return _fn(fact, ctx);
    }
}

file sealed class StubSchemaProvider : ISchemaProvider
{
    public XmlTagDefinition? GetTag(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}