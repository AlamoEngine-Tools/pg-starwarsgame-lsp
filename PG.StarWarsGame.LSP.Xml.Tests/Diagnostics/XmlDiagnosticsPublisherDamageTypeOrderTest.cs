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

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

public sealed class XmlDiagnosticsPublisherDamageTypeOrderTest
{
    // The 20 values the engine hardcodes at fixed positions — must appear as the exact tail of
    // <Damage_Types>, in this exact order, or the game crashes at runtime.
    private const string RequiredTail =
        "Damage_Normal Damage_Force_Whirlwind Damage_Force_Telekinesis Damage_Force_Lightning " +
        "Damage_Force_Corruption Damage_Hard_Point_Self_Destruct Damage_Fire Damage_Cable_Attack " +
        "Damage_Explosion Damage_Asteroid Damage_Cable_Attack_Deployed Damage_Normal_Deployed " +
        "Damage_Vehicle_Thief Damage_Crush Damage_Eat Damage_Redirected Damage_Wampa " +
        "Damage_Infection Damage_Remote_Bomb Damage_Drain_Life";

    private static XmlDiagnosticsPublisher BuildPublisher()
    {
        return new XmlDiagnosticsPublisher(
            _ => { },
            new StubIndexService2(),
            new StubWorkspaceHost2(),
            new StubSchemaProvider2(),
            new XmlDiagnosticsHandlerRegistry([]),
            new StubDocumentFactProducer2(),
            new StubIndexFactProducer2(),
            new StubStoryFactProducer2(),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new StubFileTypeRegistry2(),
            new FileHelper(new MockFileSystem()));
    }

    private static string GameConstantsWith(string damageTypesInner)
    {
        return $"<GameConstants><Damage_Types>{damageTypesInner}</Damage_Types></GameConstants>";
    }

    [Fact]
    public void ExactRequiredTail_EmitsNoDiagnostics()
    {
        var xml = GameConstantsWith($"MOD_DAMAGE_A MOD_DAMAGE_B {RequiredTail}");

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.Empty(diags);
    }

    [Fact]
    public void TailValueOutOfOrder_EmitsError()
    {
        // Swap the first two required entries.
        var badTail = RequiredTail.Replace(
            "Damage_Normal Damage_Force_Whirlwind", "Damage_Force_Whirlwind Damage_Normal");
        var xml = GameConstantsWith(badTail);

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void MissingRequiredValue_EmitsError()
    {
        var badTail = RequiredTail.Replace("Damage_Drain_Life", "Damage_Something_Else");
        var xml = GameConstantsWith(badTail);

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("Damage_Drain_Life"));
    }

    [Fact]
    public void ModValueAppendedAfterRequiredTail_EmitsError()
    {
        // The required tail must be at the VERY end — appending anything after it shifts the window.
        var xml = GameConstantsWith($"{RequiredTail} MOD_DAMAGE_AFTER");

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.NotEmpty(diags);
    }

    [Fact]
    public void TooFewTokens_EmitsSingleError()
    {
        var xml = GameConstantsWith("Damage_Normal Damage_Fire");

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.Single(diags);
    }

    [Fact]
    public void NoDamageTypesElement_EmitsNothing()
    {
        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics(
            "file:///units.xml", "<GameObjectFiles><Unit Name=\"X\"/></GameObjectFiles>");

        Assert.Empty(diags);
    }

    [Fact]
    public void BoundaryCommentBetweenModAndHardcodedValues_DoesNotAffectPositions()
    {
        // "ABOVE this point" boundary comment is a real convention in the shipped file — the tail
        // check must still work with it present, since a mod's own gameconstants.xml often retains it.
        var xml = GameConstantsWith(
            $"MOD_DAMAGE_A <!-- Do not add anything ABOVE this point --> {RequiredTail}");

        var diags = BuildPublisher().CollectDamageTypeOrderDiagnostics("file:///gameconstants.xml", xml);

        Assert.Empty(diags);
    }
}

file sealed class StubSchemaProvider2 : ISchemaProvider
{
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public XmlTagDefinition? GetTag(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
    public GameObjectTypeDefinition? GetObjectType(string _) => null;
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
    public EnumDefinition? GetEnum(string _) => null;

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class StubDocumentFactProducer2 : IXmlDocumentFactProducer
{
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri) => [];
}

file sealed class StubIndexFactProducer2 : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index) => [];
}

file sealed class StubStoryFactProducer2 : IStoryFactProducer
{
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri) => [];
}

file sealed class StubIndexService2 : IGameIndexService
{
    public GameIndex Current => GameIndex.Empty;
    public event Action<GameIndex>? IndexChanged;
    public event Action<ILocalisationIndex>? LocalisationChanged;
    public event Action<GameIndex>? DynamicEnumChanged;

    public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct) => Task.CompletedTask;
    public void InjectDocument(DocumentIndex document) { }
    public void RemoveDocument(string uri) { }
    public void ApplyBaseline(BaselineIndex baseline) { }
    public void ApplyLocalisation(ILocalisationIndex index) { }
    public void ApplyAssetFiles(IAssetFileIndex index) { }
    public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones) { }
    public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values) { }

    public void ApplyWorkspaceEnumValueDefinitions(
        ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
    {
    }

    public IDisposable BeginBulkUpdate() => NullDisposable2.Instance;

    private sealed class NullDisposable2 : IDisposable
    {
        public static readonly NullDisposable2 Instance = new();
        public void Dispose() { }
    }
}

file sealed class StubFileTypeRegistry2 : IFileTypeRegistry
{
    public ImmutableArray<string> GetTypesForFile(string normalizedPath) => ImmutableArray<string>.Empty;
    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames) { }
    public void UnregisterFile(string normalizedPath) { }
    public IReadOnlyDictionary<string, ImmutableArray<string>> All => new Dictionary<string, ImmutableArray<string>>();
}

file sealed class StubWorkspaceHost2 : IGameWorkspaceHost
{
    private readonly Dictionary<string, TrackedDocument> _docs = [];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
        _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
    }

    public void Remove(string uri) => _docs.Remove(uri);
    public bool TryGet(string uri, out TrackedDocument doc) => _docs.TryGetValue(uri, out doc!);
    public IEnumerable<TrackedDocument> All => _docs.Values;
}
