// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

public sealed class LocalisationKeySingleValueInlayHintProviderTest
{
    private static InlayHintContext MakeCtx(
        string tagName,
        string innerText,
        ReferenceKind referenceKind,
        ILocalisationIndex? localisation = null)
    {
        var hapDoc = new HtmlDocument();
        hapDoc.LoadHtml($"<{tagName}>{innerText}</{tagName}>");
        var node = hapDoc.DocumentNode.SelectSingleNode($"//{tagName.ToLower()}")!;

        var tagDef = new XmlTagDefinition
            { Tag = tagName, ValueType = XmlValueType.NameReference, ReferenceKind = referenceKind };
        var index = localisation is not null
            ? GameIndex.Empty with { Localisation = localisation }
            : GameIndex.Empty;

        return new InlayHintContext("file:///test.xml", index, new EmptySchemaProvider(), hapDoc, node, tagDef, 0);
    }

    [Fact]
    public void LocalisationKey_WithTranslation_ReturnsHintWithValue()
    {
        var loc = new StubLocalisationIndex("TEXT_NAME", "X-Wing Fighter");
        var ctx = MakeCtx("Text_ID", "TEXT_NAME", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeySingleValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        var hint = Assert.Single(hints);
        Assert.Contains("X-Wing Fighter", hint.Label.String!);
        Assert.Equal(InlayHintKind.Type, hint.Kind);
        Assert.True(hint.PaddingLeft);
    }

    [Fact]
    public void LocalisationKey_Missing_ReturnsHintWithMissing()
    {
        var loc = new StubLocalisationIndex();
        var ctx = MakeCtx("Text_ID", "TEXT_MISSING", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeySingleValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        var hint = Assert.Single(hints);
        Assert.Contains("MISSING", hint.Label.String!);
    }

    [Fact]
    public void NonLocKeyTag_ReturnsEmpty()
    {
        var ctx = MakeCtx("Name", "X_Wing", ReferenceKind.None);

        var provider = new LocalisationKeySingleValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        Assert.Empty(hints);
    }

    [Fact]
    public void EmptyInnerText_ReturnsEmpty()
    {
        var loc = new StubLocalisationIndex();
        var ctx = MakeCtx("Text_ID", "", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeySingleValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        Assert.Empty(hints);
    }

    [Fact]
    public void WhitespaceOnlyInnerText_ReturnsEmpty()
    {
        var loc = new StubLocalisationIndex();
        var ctx = MakeCtx("Text_ID", "   ", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeySingleValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        Assert.Empty(hints);
    }
}

// ── file-scoped helpers ──────────────────────────────────────────────────────

file sealed class StubLocalisationIndex : ILocalisationIndex
{
    private readonly Dictionary<string, string> _values;

    public StubLocalisationIndex(params (string key, string value)[] pairs)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) _values[k] = v;
    }

    public StubLocalisationIndex(string key, string value) : this((key, value))
    {
    }

    public StubLocalisationIndex() : this([])
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