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
            "SELECTABLE, REVEAL");

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
            "SPECIAL_WEAPON");

        Assert.Empty(diagnostics);
    }

    private static IReadOnlyList<Diagnostic> Collect(string xml, string behaviourOfWeapon)
    {
        var schema = WeaponSchemaProvider.Build();
        var fileHelper = new FileHelper(new MockFileSystem());

        var weapon = new GameSymbol("Ground_Barracks", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("file:///units/Structures.xml", 3, 0), null);
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = new[] { weapon }.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };

        var tagSource = new StubTagSource("Ground_Barracks", "LandBehavior", behaviourOfWeapon);

        var publisher = XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(),
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
            schema,
            new XmlDiagnosticsHandlerRegistry([new SpecialWeaponBehaviorHandler()]),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new ObjSrcFileTypeRegistry(),
            fileHelper,
            variantTagSource: tagSource);

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

    private static class WeaponSchemaProvider
    {
        private static readonly XmlTagDefinition WeaponA = new()
        {
            Tag = "Standalone_Space_Maps_Special_Weapon_A",
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };

        public static FakeSchemaProvider Build()
        {
            return new SchemaProviderBuilder()
                .WithTag(WeaponA)
                .Build();
        }
    }
}

// A file-scoped stub, as every other publisher test in this folder keeps its own. Only the tag
// source and the schema above carry behaviour; this exists to satisfy the constructor.

file sealed class ObjSrcFileTypeRegistry : IFileTypeRegistry
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