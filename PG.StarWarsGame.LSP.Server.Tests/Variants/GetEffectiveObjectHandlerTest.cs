// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Variants;

namespace PG.StarWarsGame.LSP.Server.Tests.Variants;

public sealed class GetEffectiveObjectHandlerTest
{
    private static GameSymbol Sym(string id, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static GetEffectiveObjectHandler Handler(GameIndex index, FakeTagSource source)
    {
        return new GetEffectiveObjectHandler(new FakeIndexService(index), new NullSchema(), source);
    }

    [Fact]
    public async Task Handle_Variant_RendersMergedEffectiveXml()
    {
        var index = IndexWith(Sym("V", "B"), Sym("B"));
        var source = new FakeTagSource()
            .With("B", new VariantTag("Max_Health", "100", "<Max_Health>100</Max_Health>", 0))
            .With("V", new VariantTag("Mass", "5", "<Mass>5</Mass>", 0));

        var result = await Handler(index, source)
            .Handle(new GetEffectiveObjectParams { ObjectId = "V" }, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(["V", "B"], result.Chain);
        Assert.Equal("SpaceUnit", result.TypeName);
        Assert.Contains("<Max_Health>100</Max_Health>", result.Xml);
        Assert.Contains("<Mass>5</Mass>", result.Xml);
        Assert.Contains("inherited from B", result.Xml);
    }

    [Fact]
    public async Task Handle_UnknownId_NotFound()
    {
        var result = await Handler(GameIndex.Empty, new FakeTagSource())
            .Handle(new GetEffectiveObjectParams { ObjectId = "NOPE" }, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.Xml);
    }

    [Fact]
    public async Task Handle_CyclicChain_FlagsCyclic()
    {
        var index = IndexWith(Sym("A", "B"), Sym("B", "A"));
        var source = new FakeTagSource().With("A", new VariantTag("X", "1", "<X>1</X>", 0));

        var result = await Handler(index, source)
            .Handle(new GetEffectiveObjectParams { ObjectId = "A" }, CancellationToken.None);

        Assert.True(result.Cyclic);
    }

    private sealed class NullSchema : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeTagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public FakeTagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public FakeIndexService(GameIndex index)
        {
            Current = index;
        }

        public GameIndex Current { get; }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new NoopScope();
        }

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
