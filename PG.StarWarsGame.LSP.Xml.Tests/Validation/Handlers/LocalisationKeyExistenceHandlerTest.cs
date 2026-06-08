// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class LocalisationKeyExistenceHandlerTest
{
    private static readonly LocalisationKeyExistenceHandler Sut = new();

    private static readonly XmlTagDefinition LocKeyTag =
        XmlHandlerTestFixtures.MakeTag("Text_ID", XmlValueType.NameReference,
            referenceKind: ReferenceKind.LocalisationKey);

    private static readonly XmlTagDefinition NonLocKeyTag =
        XmlHandlerTestFixtures.MakeTag("Name", XmlValueType.NameReference,
            referenceKind: ReferenceKind.XmlObject);

    private static DiagnosticsContext CtxWithKeys(params string[] keys)
    {
        var index = GameIndex.Empty with { Localisation = new StubLocalisationIndex(keys) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    [Fact]
    public void AbsentKey_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyTag, "TEXT_MISSING");
        var ctx = CtxWithKeys("TEXT_OTHER");

        var results = Sut.Handle(fact, ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("TEXT_MISSING", d.Message);
    }

    [Fact]
    public void PresentKey_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyTag, "TEXT_UNIT_NAME");
        var ctx = CtxWithKeys("TEXT_UNIT_NAME");

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void NonLocalisationKeyTag_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(NonLocKeyTag, "SomeUnit");
        var ctx = CtxWithKeys();

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void EmptyIndex_AbsentKey_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyTag, "TEXT_FOO");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void KeyLookup_IsCaseInsensitive()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyTag, "text_unit_name");
        var ctx = CtxWithKeys("TEXT_UNIT_NAME");

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Empty(results);
    }
}

file sealed class StubLocalisationIndex : ILocalisationIndex
{
    private readonly HashSet<string> _keys;

    public StubLocalisationIndex(IEnumerable<string> keys)
    {
        _keys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public bool ContainsKey(string key)
    {
        return _keys.Contains(key);
    }

    public IEnumerable<string> Keys => _keys;

    public string? GetValue(string key)
    {
        return null;
    }
}