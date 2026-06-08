// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class LocalisationKeyListExistenceHandlerTest
{
    private static readonly LocalisationKeyListExistenceHandler Sut = new();

    private static readonly XmlTagDefinition LocKeyListTag =
        XmlHandlerTestFixtures.MakeTag("Tooltips", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.LocalisationKey);

    private static readonly XmlTagDefinition NonLocKeyListTag =
        XmlHandlerTestFixtures.MakeTag("Abilities", XmlValueType.NameReferenceList,
            referenceKind: ReferenceKind.XmlObject);

    private static DiagnosticsContext CtxWithKeys(params string[] keys)
    {
        var index = GameIndex.Empty with { Localisation = new ValueLocIndex(keys) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    [Fact]
    public void AllKeysPresent_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "TEXT_A TEXT_B TEXT_C");
        var ctx = CtxWithKeys("TEXT_A", "TEXT_B", "TEXT_C");

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void OneKeyAbsent_EmitsOneWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "TEXT_A TEXT_MISSING TEXT_C");
        var ctx = CtxWithKeys("TEXT_A", "TEXT_C");

        var results = Sut.Handle(fact, ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("TEXT_MISSING", d.Message);
    }

    [Fact]
    public void MultipleKeysAbsent_EmitsOneWarningPerMissing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "TEXT_A TEXT_BAD1 TEXT_BAD2");
        var ctx = CtxWithKeys("TEXT_A");

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void NonLocKeyListTag_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(NonLocKeyListTag, "SomeAbility OtherAbility");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void EmptyValue_EmitsNothing()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "   ");
        var results = Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void SingleTokenInList_Absent_EmitsWarning()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "TEXT_ONLY");
        var ctx = CtxWithKeys("TEXT_OTHER");

        var results = Sut.Handle(fact, ctx).ToList();

        var d = Assert.Single(results);
        Assert.Contains("TEXT_ONLY", d.Message);
    }

    [Fact]
    public void TabSeparatedKeys_ParsedCorrectly()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(LocKeyListTag, "TEXT_A\tTEXT_B");
        var ctx = CtxWithKeys("TEXT_A", "TEXT_B");

        var results = Sut.Handle(fact, ctx).ToList();

        Assert.Empty(results);
    }
}

file sealed class ValueLocIndex : ILocalisationIndex
{
    private readonly HashSet<string> _keys;

    public ValueLocIndex(IEnumerable<string> keys)
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