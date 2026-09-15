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

/// <summary>
///     Whether two diagnostics that would both apply to the same place BOTH survive to the client.
/// </summary>
/// <remarks>
///     <para>
///         Driven through the real publisher, because the question is about the pipeline rather than
///         about any rule: <c>Collect</c> appends every (fact, handler) result, and nothing between
///         there and the wire de-duplicates by range. Two handlers on one fact type is the sharpest
///         case - same fact, same anchor, same length, two different messages and two different
///         fixes.
///     </para>
///     <para>
///         The second assertion is the one worth keeping: the fix travels on each diagnostic's own
///         <c>Data</c>, so co-anchored fixes do not contend. The <c>_fixCache</c> behind
///         <c>GetSuggestedFix</c> is keyed on the start position alone and cannot hold both - it is
///         the fallback for a client that drops <c>data</c>, and it is last-writer-wins.
///     </para>
/// </remarks>
public sealed class DiagnosticAdditivityProbe
{
    private const string Uri = "file:///units/Units.xml";
    private const string Xml = "<Units><SpaceUnit Name=\"A\"><Tactical_Health>5</Tactical_Health></SpaceUnit></Units>";

    [Fact]
    public void Two_handlers_on_one_fact_both_reach_the_client()
    {
        var diagnostics = Collect();

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, d => d.Message == "first");
        Assert.Contains(diagnostics, d => d.Message == "second");

        // Same anchor, so nothing could have told them apart by range.
        Assert.Single(diagnostics.Select(d => d.Range).Distinct());
    }

    [Fact]
    public void Each_diagnostic_carries_its_own_fix()
    {
        var diagnostics = Collect();

        Assert.Equal("FIX_ONE",
            (string?)diagnostics.Single(d => d.Message == "first").Data?["fix"]);
        Assert.Equal("FIX_TWO",
            (string?)diagnostics.Single(d => d.Message == "second").Data?["fix"]);
    }

    /// <summary>
    ///     The one place that is NOT additive, pinned so a change to it is deliberate.
    /// </summary>
    [Fact]
    public async Task The_position_keyed_fix_cache_keeps_only_the_last_one()
    {
        var host = new ProbeWorkspaceHost();
        host.AddOrUpdate(Uri, Xml, 1);
        var publisher = Publisher(out _, host);

        // The real publish path, which is what fills the cache.
        await publisher.RevalidateDocumentAsync(Uri, TestContext.Current.CancellationToken);

        var valueColumn = Xml.IndexOf(">5<", StringComparison.Ordinal) + 1;
        var cached = publisher.GetSuggestedFix(Uri, 0, valueColumn);

        Assert.Equal("FIX_TWO", cached);
    }

    private static IReadOnlyList<Diagnostic> Collect()
    {
        var publisher = Publisher(out var index, new ProbeWorkspaceHost());
        return publisher.Collect(Uri, Xml, index);
    }

    private static XmlDiagnosticsPublisher Publisher(out GameIndex index, IGameWorkspaceHost host)
    {
        var schema = new ProbeSchemaProvider();
        var fileHelper = new FileHelper(new MockFileSystem());
        index = GameIndex.Empty;

        return new XmlDiagnosticsPublisher(
            _ => { },
            new ProbeIndexService(),
            host,
            schema,
            new XmlDiagnosticsHandlerRegistry(
                [new FirstProbeHandler(), new SecondProbeHandler()]),
            new XmlDocumentFactProducer(fileHelper, schema, new ProbeFileTypeRegistry(),
                new XmlStructuralValidator()),
            new ProbeIndexFactProducer(),
            new ProbeStoryFactProducer(),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new ProbeFileTypeRegistry(),
            fileHelper);
    }
}

file sealed class FirstProbeHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override DiagnosticId? DefaultId => DiagnosticIds.OwnerIncomeShareOutOfRange;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "first", SuggestedFix: "FIX_ONE")];
    }
}

file sealed class SecondProbeHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override DiagnosticId? DefaultId => DiagnosticIds.IncomeSplitConflict;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Error, "second", SuggestedFix: "FIX_TWO")];
    }
}

file sealed class ProbeSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition Health = new()
    {
        Tag = "Tactical_Health",
        ValueType = XmlValueType.Float
    };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals(Health.Tag, StringComparison.OrdinalIgnoreCase) ? Health : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [Health];
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

file sealed class ProbeIndexFactProducer : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index)
    {
        return [];
    }
}

file sealed class ProbeStoryFactProducer : IStoryFactProducer
{
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
    {
        return [];
    }
}

file sealed class ProbeIndexService : IGameIndexService
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
        return NullProbeDisposable.Instance;
    }
}

file sealed class NullProbeDisposable : IDisposable
{
    public static readonly NullProbeDisposable Instance = new();

    public void Dispose()
    {
    }
}

file sealed class ProbeWorkspaceHost : IGameWorkspaceHost
{
    private readonly Dictionary<string, TrackedDocument> _docs = [];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
        _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
    }

    public void Remove(string uri)
    {
        _docs.Remove(uri);
    }

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

file sealed class ProbeFileTypeRegistry : IFileTypeRegistry
{
    public ImmutableArray<string> GetTypesForFile(string normalizedPath)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();
}