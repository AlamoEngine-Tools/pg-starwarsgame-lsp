// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

/// <summary>
///     The cross-object seam, end to end through the real publisher rather than a fake context.
/// </summary>
/// <remarks>
///     A handler test proves the RULE; it cannot prove the wiring, and the wiring is where this kind
///     of change actually breaks - a resolver that is never constructed leaves
///     <c>DiagnosticsContext.Objects</c> null, every cross-object handler silently returns nothing,
///     and every unit test still passes. So this drives <c>Collect</c> with a real
///     <c>XmlDocumentFactProducer</c> and a real handler, and asserts the diagnostic comes out.
/// </remarks>
public sealed class XmlDiagnosticsPublisherObjectSourceTest
{
    private const string FactionUri = "file:///factions/Factions.xml";

    [Fact]
    public void Cross_object_handler_receives_a_resolver_and_reports()
    {
        var diagnostics = Collect(
            "<Factions><Faction Name=\"Rebel\">" +
            "<Standalone_Space_Maps_Special_Weapon_A>Ground_Barracks</Standalone_Space_Maps_Special_Weapon_A>" +
            "</Faction></Factions>",
            behaviourOfWeapon: "SELECTABLE, REVEAL");

        var d = Assert.Single(diagnostics);
        Assert.Contains("Ground_Barracks", d.Message);
        Assert.Contains("SPECIAL_WEAPON", d.Message);
    }

    [Fact]
    public void A_real_special_weapon_produces_nothing()
    {
        var diagnostics = Collect(
            "<Factions><Faction Name=\"Rebel\">" +
            "<Standalone_Space_Maps_Special_Weapon_A>Ground_Barracks</Standalone_Space_Maps_Special_Weapon_A>" +
            "</Faction></Factions>",
            behaviourOfWeapon: "SPECIAL_WEAPON");

        Assert.Empty(diagnostics);
    }

    // The control: identical input, no tag source, so no resolver reaches the context. If this ever
    // starts reporting, the handler has stopped honouring "cannot tell" and is guessing instead.
    [Fact]
    public void Without_a_tag_source_the_pass_stays_silent()
    {
        var diagnostics = Collect(
            "<Factions><Faction Name=\"Rebel\">" +
            "<Standalone_Space_Maps_Special_Weapon_A>Ground_Barracks</Standalone_Space_Maps_Special_Weapon_A>" +
            "</Faction></Factions>",
            behaviourOfWeapon: "SELECTABLE",
            wireTagSource: false);

        Assert.Empty(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Collect(
        string xml, string behaviourOfWeapon, bool wireTagSource = true)
    {
        var schema = new WeaponSchemaProvider();
        var fileHelper = new FileHelper(new MockFileSystem());

        var weapon = new GameSymbol("Ground_Barracks", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("file:///units/Structures.xml", 3, 0), null);
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[] { weapon }.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tagSource = new StubTagSource("Ground_Barracks", "LandBehavior", behaviourOfWeapon);

        var publisher = new XmlDiagnosticsPublisher(
            _ => { },
            new ObjSrcIndexService(),
            new ObjSrcWorkspaceHost(),
            schema,
            new XmlDiagnosticsHandlerRegistry([new SpecialWeaponBehaviorHandler()]),
            new XmlDocumentFactProducer(fileHelper, schema, new ObjSrcFileTypeRegistry(),
                new XmlStructuralValidator()),
            new ObjSrcIndexFactProducer(),
            new ObjSrcStoryFactProducer(),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new ObjSrcFileTypeRegistry(),
            fileHelper,
            variantTagSource: wireTagSource ? tagSource : null);

        return publisher.Collect(FactionUri, xml, index);
    }

    private sealed class StubTagSource(string objectId, string tagName, string value) : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string id)
        {
            return string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase)
                ? [new VariantTag(tagName, value, $"<{tagName}>{value}</{tagName}>", 0)]
                : null;
        }
    }

    private sealed class WeaponSchemaProvider : ISchemaProvider
    {
        private static readonly XmlTagDefinition WeaponA = new()
        {
            Tag = "Standalone_Space_Maps_Special_Weapon_A",
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };

        public XmlTagDefinition? GetTag(string tagName)
        {
            return tagName.Equals(WeaponA.Tag, StringComparison.OrdinalIgnoreCase) ? WeaponA : null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<XmlTagDefinition> AllTags => [WeaponA];
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
}

// File-scoped stubs, as every other publisher test in this folder keeps its own. Only the tag
// source and the schema above carry behaviour; these exist to satisfy the constructor.

file sealed class ObjSrcIndexFactProducer : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index) => [];
}

file sealed class ObjSrcStoryFactProducer : IStoryFactProducer
{
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri) => [];
}

file sealed class ObjSrcFileTypeRegistry : IFileTypeRegistry
{
    public ImmutableArray<string> GetTypesForFile(string normalizedPath) => ImmutableArray<string>.Empty;

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();
}

file sealed class ObjSrcWorkspaceHost : IGameWorkspaceHost
{
    private readonly Dictionary<string, TrackedDocument> _docs = [];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
        _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
    }

    public void Remove(string uri) => _docs.Remove(uri);

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        if (_docs.TryGetValue(uri, out var d))
        {
            doc = d;
            return true;
        }

        doc = default!;
        return false;
    }

    public IEnumerable<TrackedDocument> All => _docs.Values;
}

file sealed class ObjSrcIndexService : IGameIndexService
{
    public GameIndex Current => GameIndex.Empty;

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

    public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct) =>
        Task.CompletedTask;

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

    public IDisposable BeginBulkUpdate() => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
