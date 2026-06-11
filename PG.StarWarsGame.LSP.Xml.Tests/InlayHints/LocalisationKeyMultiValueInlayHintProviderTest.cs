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

public sealed class LocalisationKeyMultiValueInlayHintProviderTest
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
            { Tag = tagName, ValueType = XmlValueType.TypeReferenceList, ReferenceKind = referenceKind };
        var index = localisation is not null
            ? GameIndex.Empty with { Localisation = localisation }
            : GameIndex.Empty;

        return new InlayHintContext("file:///test.xml", index, new EmptySchemaProvider(), hapDoc, node, tagDef, 0);
    }

    [Fact]
    public void LocalisationKey_WithTranslation_ReturnsHintsWithValuesAndMissing()
    {
        var loc = new StubLocalisationIndex(("TEXT_001", "Translation line 1"), ("TEXT_003", "Translation line 3"));
        var ctx = MakeCtx("Encyclopedia_Text", "TEXT_001 TEXT_002 TEXT_003", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeyMultiValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        Assert.Equal(3, hints.Count);
        Assert.Contains("Translation line 1", hints[0].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[0].Kind);
        Assert.True(hints[0].PaddingLeft);
        Assert.Contains("MISSING", hints[1].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[1].Kind);
        Assert.True(hints[1].PaddingLeft);
        Assert.Contains("Translation line 3", hints[2].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[2].Kind);
        Assert.True(hints[2].PaddingLeft);
    }

    [Fact]
    public void LocalisationKey_WithTranslation_ReturnsHintsWithValues()
    {
        var loc = new StubLocalisationIndex(("TEXT_001", "Translation line 1"), ("TEXT_002", "Translation line 2"),
            ("TEXT_003", "Translation line 3"));
        var ctx = MakeCtx("Encyclopedia_Text", "TEXT_001 TEXT_002 TEXT_003", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeyMultiValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        Assert.Equal(3, hints.Count);
        Assert.Contains("Translation line 1", hints[0].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[0].Kind);
        Assert.True(hints[0].PaddingLeft);
        Assert.Contains("Translation line 2", hints[1].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[1].Kind);
        Assert.True(hints[1].PaddingLeft);
        Assert.Contains("Translation line 3", hints[2].Label.String!);
        Assert.Equal(InlayHintKind.Type, hints[2].Kind);
        Assert.True(hints[2].PaddingLeft);
    }

    [Fact]
    public void LocalisationKey_Missing_ReturnsHintWithMissing()
    {
        var loc = new StubLocalisationIndex();
        var ctx = MakeCtx("Text_ID", "TEXT_MISSING", ReferenceKind.LocalisationKey, loc);

        var provider = new LocalisationKeyMultiValueInlayHintProvider();
        var hints = provider.Handle(ctx).ToList();

        var hint = Assert.Single(hints);
        Assert.Contains("MISSING", hint.Label.String!);
    }

    [Fact]
    public void NonLocKeyTag_ReturnsEmpty()
    {
        var ctx = MakeCtx("Name", "X_Wing", ReferenceKind.None);

        var provider = new LocalisationKeyMultiValueInlayHintProvider();
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