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

    // ── validationOverride routing ────────────────────────────────────────────────

    private static XmlTagDefinition TagWithOverride(
        string validationId,
        ValidationOverrideMode mode = ValidationOverrideMode.Additive,
        ValidationOverrideOrder order = ValidationOverrideOrder.Append)
    {
        return new XmlTagDefinition
        {
            Tag = "Damage",
            ValueType = XmlValueType.Float,
            ValidationOverride = new TagValidationOverride { ValidationId = validationId, Mode = mode, Order = order }
        };
    }

    [Fact]
    public void NoOverride_AllDefaultHandlersRun()
    {
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) => [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "d1")]),
            new LambdaHandler<XmlTagValueFact>((_, _) => [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "d2")])
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, FloatTag, "1.0");

        var results = registry.Dispatch(fact, EmptyCtx).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message == "d1");
        Assert.Contains(results, r => r.Message == "d2");
    }

    [Fact]
    public void Replace_OnlyNamedHandlerRuns()
    {
        var tag = TagWithOverride("custom-id", ValidationOverrideMode.Replace);
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) =>
                [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "default")]),
            new NamedLambdaHandler<XmlTagValueFact>("custom-id", (_, _) =>
                [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "named")])
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, tag, "0");

        var results = registry.Dispatch(fact, EmptyCtx).ToList();

        var single = Assert.Single(results);
        Assert.Equal("named", single.Message);
    }

    [Fact]
    public void Additive_Prepend_NamedRunsFirst()
    {
        var tag = TagWithOverride("custom-id", ValidationOverrideMode.Additive, ValidationOverrideOrder.Prepend);
        var invocationOrder = new List<string>();
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) =>
            {
                invocationOrder.Add("default");
                return [];
            }),
            new NamedLambdaHandler<XmlTagValueFact>("custom-id", (_, _) =>
            {
                invocationOrder.Add("named");
                return [];
            })
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, tag, "0");

        registry.Dispatch(fact, EmptyCtx).ToList();

        Assert.Equal(["named", "default"], invocationOrder);
    }

    [Fact]
    public void Additive_Append_DefaultRunsFirst()
    {
        var tag = TagWithOverride("custom-id");
        var invocationOrder = new List<string>();
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) =>
            {
                invocationOrder.Add("default");
                return [];
            }),
            new NamedLambdaHandler<XmlTagValueFact>("custom-id", (_, _) =>
            {
                invocationOrder.Add("named");
                return [];
            })
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, tag, "0");

        registry.Dispatch(fact, EmptyCtx).ToList();

        Assert.Equal(["default", "named"], invocationOrder);
    }

    [Fact]
    public void UnknownValidationId_Replace_NoResults()
    {
        var tag = TagWithOverride("missing-id", ValidationOverrideMode.Replace);
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) =>
                [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "default")])
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, tag, "0");

        Assert.Empty(registry.Dispatch(fact, EmptyCtx));
    }

    [Fact]
    public void UnknownValidationId_Additive_OnlyDefaultRuns()
    {
        var tag = TagWithOverride("missing-id");
        var registry = new XmlDiagnosticsHandlerRegistry(
        [
            new LambdaHandler<XmlTagValueFact>((_, _) =>
                [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "default")])
        ]);
        var fact = new XmlTagValueFact("file:///test.xml", 0, 0, 3, tag, "0");

        var results = registry.Dispatch(fact, EmptyCtx).ToList();

        var single = Assert.Single(results);
        Assert.Equal("default", single.Message);
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

file sealed class NamedLambdaHandler<TFact> : XmlDiagnosticsHandler<TFact>, IXmlNamedDiagnosticsHandler
    where TFact : XmlFact
{
    private readonly Func<TFact, DiagnosticsContext, IEnumerable<XmlDiagnosticResult>> _fn;

    public NamedLambdaHandler(string validationId,
        Func<TFact, DiagnosticsContext, IEnumerable<XmlDiagnosticResult>> fn)
    {
        ValidationId = validationId;
        _fn = fn;
    }

    public string ValidationId { get; }

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