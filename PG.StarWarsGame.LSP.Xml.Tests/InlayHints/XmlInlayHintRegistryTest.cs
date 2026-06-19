// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

public sealed class XmlInlayHintRegistryTest
{
    private static InlayHintContext MakeCtx()
    {
        var hapDoc = new HtmlDocument();
        hapDoc.LoadHtml("<Name>foo</Name>");
        var node = hapDoc.DocumentNode.SelectSingleNode("//name")!;
        var tagDef = new XmlTagDefinition { Tag = "Name", ValueType = XmlValueType.Boolean };
        return new InlayHintContext("file:///test.xml", GameIndex.Empty, new EmptySchemaProvider(), hapDoc, node,
            tagDef, 0);
    }

    [Fact]
    public void NoProviders_ReturnsEmpty()
    {
        var registry = new XmlInlayHintRegistry([]);
        var hints = registry.Dispatch(MakeCtx()).ToList();

        Assert.Empty(hints);
    }

    [Fact]
    public void SingleProvider_ReturnsItsHints()
    {
        var hint = new InlayHint { Position = new Position(0, 0), Label = "= \"test\"" };
        var provider = new StubProvider([hint]);
        var registry = new XmlInlayHintRegistry([provider]);

        var hints = registry.Dispatch(MakeCtx()).ToList();

        Assert.Single(hints);
        Assert.Equal("= \"test\"", hints[0].Label.String);
    }

    [Fact]
    public void MultipleProviders_ConcatenatesAllHints()
    {
        var h1 = new InlayHint { Position = new Position(0, 0), Label = "= \"a\"" };
        var h2 = new InlayHint { Position = new Position(0, 0), Label = "= \"b\"" };
        var registry = new XmlInlayHintRegistry([new StubProvider([h1]), new StubProvider([h2])]);

        var hints = registry.Dispatch(MakeCtx()).ToList();

        Assert.Equal(2, hints.Count);
    }

    [Fact]
    public void ProviderReturningEmpty_DoesNotContributeHints()
    {
        var h1 = new InlayHint { Position = new Position(0, 0), Label = "= \"a\"" };
        var registry = new XmlInlayHintRegistry([new StubProvider([]), new StubProvider([h1])]);

        var hints = registry.Dispatch(MakeCtx()).ToList();

        var hint = Assert.Single(hints);
        Assert.Equal("= \"a\"", hint.Label.String);
    }
}

// ── file-scoped helpers ──────────────────────────────────────────────────────

file sealed class StubProvider : IXmlInlayHintProvider
{
    private readonly IReadOnlyList<InlayHint> _hints;

    public StubProvider(IReadOnlyList<InlayHint> hints)
    {
        _hints = hints;
    }

    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        return _hints;
    }
}