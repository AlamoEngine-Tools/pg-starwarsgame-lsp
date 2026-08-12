// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.CodeLens;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeLens;

/// <summary>
///     The "preview encyclopedia" lens appears only where there is a popup to preview - a file of
///     fifty objects should not grow fifty lenses for text that does not exist.
/// </summary>
public sealed class EncyclopediaCodeLensProviderTest
{
    private static EncyclopediaCodeLensProvider Provider(
        IVariantTagSource source, ILspConfigurationProvider? config = null)
    {
        return new EncyclopediaCodeLensProvider(source, config ?? new FakeLspConfigurationProvider());
    }

    private static GameSymbol Sym(string id, string? baseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 3, 0), null, baseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static CodeLensSymbolContext Ctx(GameSymbol symbol, GameIndex index)
    {
        return new CodeLensSymbolContext(symbol, (FileOrigin)symbol.Origin, index);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private sealed class TagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public TagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    [Fact]
    public void Handle_ObjectWithEncyclopediaText_EmitsLensCarryingTheObjectId()
    {
        var symbol = Sym("Etahn");
        var source = new TagSource().With("Etahn", Tag("Encyclopedia_Text", "TEXT_BIO"));

        var lens = Provider(source).Handle(Ctx(symbol, IndexWith(symbol)));

        Assert.NotNull(lens);
        Assert.Equal(EncyclopediaCodeLensProvider.ShowEncyclopediaCommand, lens!.Command!.Name);
        Assert.Contains("Etahn", lens.Command.Arguments!.ToString());
        Assert.Equal(3, lens.Range.Start.Line);
    }

    [Fact]
    public void Handle_ObjectWithOnlyMultiplayerText_StillEmitsLens()
    {
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag("MP_Encyclopedia_Text", "TEXT_MP"));

        Assert.NotNull(Provider(source).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_ObjectWithoutAnyEncyclopediaText_EmitsNothing()
    {
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag("Max_Health", "100"));

        Assert.Null(Provider(source).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_VariantInheritingTextFromBase_EmitsLens()
    {
        // The popup a variant shows is its base's, so the lens has to follow the chain - offering it
        // only on objects that restate the text would hide the preview on exactly the objects whose
        // inherited content is worth checking.
        var variant = Sym("V", "B");
        var index = IndexWith(variant, Sym("B"));
        var source = new TagSource()
            .With("V", Tag("Max_Health", "100"))
            .With("B", Tag("Encyclopedia_Text", "TEXT_BIO"));

        Assert.NotNull(Provider(source).Handle(Ctx(variant, index)));
    }

    [Fact]
    public void Handle_CyclicVariantChainWithoutText_TerminatesAndEmitsNothing()
    {
        var a = Sym("A", "B");
        var index = IndexWith(a, Sym("B", "A"));
        var source = new TagSource().With("A", Tag("Mass", "5")).With("B", Tag("Mass", "6"));

        Assert.Null(Provider(source).Handle(Ctx(a, index)));
    }

    [Fact]
    public void Handle_EncyclopediaFlagOff_EmitsNothing()
    {
        var symbol = Sym("Etahn");
        var source = new TagSource().With("Etahn", Tag("Encyclopedia_Text", "TEXT_BIO"));
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Encyclopedia = false } });

        Assert.Null(Provider(source, config).Handle(Ctx(symbol, IndexWith(symbol))));
    }
}
