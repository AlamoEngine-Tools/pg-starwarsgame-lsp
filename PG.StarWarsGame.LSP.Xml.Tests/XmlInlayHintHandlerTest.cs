// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlInlayHintHandlerTest
{
    private const string Uri = "file:///data/units.xml";

    // ReSharper disable once UnusedTupleComponentInReturnValue - the helpers return everything a test might need; some tests ignore a part
    private static (XmlInlayHintHandler handler, FakeGameIndexService index) Build(
        string docText,
        ISchemaProvider? schema = null,
        ILocalisationIndex? localisation = null,
        ILspConfigurationProvider? config = null)
    {
        var host = new FakeGameWorkspaceHost();
        host.With(Uri, docText);

        var index = new FakeGameIndexService();
        if (localisation is not null)
            index.Current = index.Current with { Localisation = localisation };

        var registry = new XmlInlayHintRegistry([new LocalisationKeySingleValueInlayHintProvider()]);
        return (new XmlInlayHintHandler(
            TestParseCache.For(host),
            index,
            schema ?? new SchemaProviderBuilder().Build(),
            new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            registry, TestCatalogues.English), index);
    }

    private static LspRange FullRange()
    {
        return new LspRange(new Position(0, 0), new Position(int.MaxValue, 0));
    }

    private static InlayHintParams Params(LspRange? range = null)
    {
        return new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(Uri) },
            Range = range ?? FullRange()
        };
    }

    // ── basic hint production ────────────────────────────────────────────────

    [Fact]
    public async Task LocKeyTag_WithKnownTranslation_ProducesHintOnSameLine()
    {
        const string xml = "<GameObjectType>\n  <Text_ID>TEXT_UNIT_NAME</Text_ID>\n</GameObjectType>";
        var schema = LocKeySchema("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_UNIT_NAME", "X-Wing Fighter");

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        var hint = Assert.Single(result!);
        Assert.Equal(1, hint.Position.Line); // line 1 (0-based)
        Assert.Contains("X-Wing Fighter", hint.Label.String!);
    }

    [Fact]
    public async Task LocKeyTag_AbsentFromIndex_ProducesMissingHint()
    {
        const string xml = "<GameObjectType>\n  <Text_ID>TEXT_MISSING</Text_ID>\n</GameObjectType>";
        var schema = LocKeySchema("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_OTHER", "Something else");

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        var hint = Assert.Single(result!);
        Assert.Contains("MISSING", hint.Label.String!);
    }

    [Fact]
    public async Task NonLocKeyTag_ProducesNoHint()
    {
        const string xml = "<GameObjectType>\n  <Name>X_Wing</Name>\n</GameObjectType>";
        // schema returns None referenceKind for Name
        var (handler, _) = Build(xml);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task MultipleLocKeyTags_ProducesHintForEach()
    {
        const string xml =
            "<GameObjectType>\n" +
            "  <Text_ID>TEXT_NAME</Text_ID>\n" +
            "  <Tooltip>TEXT_TIP</Tooltip>\n" +
            "</GameObjectType>";
        var schema = LocKeySchema("Text_ID", "Tooltip");
        var loc = new ValueLocalisationIndex(
            ("TEXT_NAME", "X-Wing Fighter"),
            ("TEXT_TIP", "A fast rebel craft"));

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Equal(2, result!.Count());
    }

    [Fact]
    public async Task EmptyTagValue_ProducesNoHint()
    {
        const string xml = "<GameObjectType>\n  <Text_ID></Text_ID>\n</GameObjectType>";
        var schema = LocKeySchema("Text_ID");
        var loc = new ValueLocalisationIndex();

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── range filtering ──────────────────────────────────────────────────────

    [Fact]
    public async Task TagOutsideRange_ProducesNoHint()
    {
        const string xml =
            "<GameObjectType>\n" +
            "  <Text_ID>TEXT_NAME</Text_ID>\n" + // line 1
            "</GameObjectType>";
        var schema = LocKeySchema("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_NAME", "X-Wing Fighter");
        // Request range covers only line 0
        var range = new LspRange(new Position(0, 0), new Position(0, int.MaxValue));

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(range), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── non-EaW file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NonEaWFile_ReturnsNull()
    {
        const string xml = "<GameObjectType><Text_ID>TEXT_NAME</Text_ID></GameObjectType>";
        var host = new FakeGameWorkspaceHost();
        host.With(Uri, xml);
        var index = new FakeGameIndexService();
        var handler = new XmlInlayHintHandler(
            TestParseCache.For(host), index, new SchemaProviderBuilder().Build(),
            new DenyAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            new XmlInlayHintRegistry([]), TestCatalogues.English);

        var result = await handler.Handle(Params(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SameContentTwice_ReusesOneParse()
    {
        // Inlay hints are re-requested by clients on every scroll - with unchanged content the
        // second request must be a cache hit, not a re-parse.
        const string xml = "<GameObjectType><Text_ID>TEXT_NAME</Text_ID></GameObjectType>";
        var host = new FakeGameWorkspaceHost();
        host.With(Uri, xml);
        var cache = TestParseCache.For(host);
        var handler = new XmlInlayHintHandler(
            cache, new FakeGameIndexService(), new SchemaProviderBuilder().Build(),
            new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            new XmlInlayHintRegistry([]), TestCatalogues.English);

        _ = await handler.Handle(Params(), CancellationToken.None);
        _ = await handler.Handle(Params(), CancellationToken.None);

        var (hits, misses, _) = cache.Statistics;
        Assert.Equal(1, misses);
        Assert.Equal(1, hits);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A schema in which these tags, and only these, carry a localisation key.</summary>
    private static FakeSchemaProvider LocKeySchema(params string[] locKeyTagNames)
    {
        var builder = new SchemaProviderBuilder();
        foreach (var name in locKeyTagNames)
            builder.WithTag(new XmlTagDefinition
            {
                Tag = name, ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.LocalisationKey
            });
        return builder.Build();
    }
}

// ── file-scoped helpers ──────────────────────────────────────────────────────

file sealed class ValueLocalisationIndex : ILocalisationIndex
{
    private readonly Dictionary<string, string> _values;

    public ValueLocalisationIndex(params (string key, string value)[] pairs)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) _values[k] = v;
    }

    public ValueLocalisationIndex(string key, string value)
        : this((key, value))
    {
    }

    public ValueLocalisationIndex() : this([])
    {
    }

    public bool ContainsKey(string key)
    {
        return _values.ContainsKey(key);
    }

    public IEnumerable<string> Keys => _values.Keys;

    public string? GetValue(string key)
    {
        return _values.TryGetValue(key, out var v) ? v : null;
    }
}