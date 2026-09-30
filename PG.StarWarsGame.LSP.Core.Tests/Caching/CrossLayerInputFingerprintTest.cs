// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

/// <summary>
///     Replaces the dependency-OverallHash key, which threw away a whole dependent layer because
///     ONE line changed in a dependency - on EaWX that re-parses all four leaves for an edit in
///     Core. This key names what the parser actually reads across a layer boundary instead.
/// </summary>
/// <remarks>
///     <para>
///         The invariant, VERIFIED against the parsers on 2026-09-30 rather than assumed: parsing
///         a document reads exactly two things decided outside its own layer - the file-type
///         registration for that file (dependency metafiles register types for a leaf's files),
///         and the set of XML directories, because a workspace-file symbol's key is the document's
///         path relative to the LONGEST matching xml root and a dependency can contribute one.
///     </para>
///     <para>
///         Everything else the parsers touch is covered elsewhere or is not cross-layer: the schema
///         and the story feature flag fold into <see cref="SchemaFingerprint" />, and the file
///         helper, parse caches and loggers carry no project state. If a parser gains another
///         injected input that varies by layer, it MUST be folded in here or
///         <see cref="ProjectIndexSnapshot.CurrentSchemaVersion" /> must be bumped - a stale index
///         fails silently, which is the whole hazard this key carries.
///     </para>
/// </remarks>
public sealed class CrossLayerInputFingerprintTest
{
    private static readonly string[] OneXmlDir = ["file:///c:/mod/data/xml/"];

    private static FileTypeRegistry RegistryWith(params (string Uri, string[] Types)[] entries)
    {
        var registry = new FileTypeRegistry();
        foreach (var (uri, types) in entries)
            registry.RegisterFile(uri, types.ToImmutableArray());
        return registry;
    }

    private static string Compute(
        IFileTypeRegistry registry, IReadOnlyList<string> files, IReadOnlyList<string>? xmlDirs = null)
    {
        return CrossLayerInputFingerprint.Compute(registry, files, xmlDirs ?? OneXmlDir);
    }

    // ── stability ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_IsStableAcrossCalls()
    {
        var registry = RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType"]));

        Assert.Equal(
            Compute(registry, ["file:///c:/mod/units.xml"]),
            Compute(registry, ["file:///c:/mod/units.xml"]));
    }

    [Fact]
    public void Compute_DoesNotDependOnFileOrder()
    {
        var registry = RegistryWith(
            ("file:///c:/mod/a.xml", ["A"]),
            ("file:///c:/mod/b.xml", ["B"]));

        Assert.Equal(
            Compute(registry, ["file:///c:/mod/a.xml", "file:///c:/mod/b.xml"]),
            Compute(registry, ["file:///c:/mod/b.xml", "file:///c:/mod/a.xml"]));
    }

    // ── what MUST move it ────────────────────────────────────────────────────

    [Fact]
    public void Compute_ChangesWhenADependencyRegistersANewTypeForThisLayersFile()
    {
        // The case the old key existed for: a dependency's metafile decides that this layer's
        // units.xml carries GameObjectType. Nothing in THIS layer changed, and its own file hashes
        // all still match, so only a key like this one can catch it.
        var before = Compute(RegistryWith(("file:///c:/mod/units.xml", [])), ["file:///c:/mod/units.xml"]);
        var after = Compute(
            RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType"])), ["file:///c:/mod/units.xml"]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Compute_ChangesWhenARegisteredTypeIsRemoved()
    {
        var before = Compute(
            RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType", "Faction"])),
            ["file:///c:/mod/units.xml"]);
        var after = Compute(
            RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType"])), ["file:///c:/mod/units.xml"]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Compute_ChangesWhenAnXmlDirectoryIsAdded()
    {
        // Not just the type map. A workspace-file symbol is keyed by the path relative to the
        // LONGEST matching xml root, and any layer can contribute a root - so a dependency adding
        // a nested xml directory silently re-keys a leaf's story manifest symbols.
        var registry = RegistryWith(("file:///c:/mod/data/xml/story.xml", ["StoryParser"]));

        Assert.NotEqual(
            Compute(registry, ["file:///c:/mod/data/xml/story.xml"], ["file:///c:/mod/data/xml/"]),
            Compute(registry, ["file:///c:/mod/data/xml/story.xml"],
                ["file:///c:/mod/data/xml/", "file:///c:/mod/data/xml/conquests/"]));
    }

    [Fact]
    public void Compute_DoesNotDependOnXmlDirectoryOrder()
    {
        var registry = RegistryWith(("file:///c:/mod/a.xml", ["A"]));

        Assert.Equal(
            Compute(registry, ["file:///c:/mod/a.xml"], ["file:///c:/a/", "file:///c:/b/"]),
            Compute(registry, ["file:///c:/mod/a.xml"], ["file:///c:/b/", "file:///c:/a/"]));
    }

    // ── what must NOT move it: the entire point ──────────────────────────────

    [Fact]
    public void Compute_IgnoresRegistrationsForFilesOutsideThisLayer()
    {
        // THE WIN. A dependency edited one of its OWN files and its type registration changed.
        // That cannot affect how this layer's documents parse, so this layer's snapshot must
        // survive - where the old dependency-OverallHash key discarded it whole.
        var files = new[] {"file:///c:/mod/units.xml"};
        var before = RegistryWith(
            ("file:///c:/mod/units.xml", ["GameObjectType"]),
            ("file:///c:/core/other.xml", ["Faction"]));
        var after = RegistryWith(
            ("file:///c:/mod/units.xml", ["GameObjectType"]),
            ("file:///c:/core/other.xml", ["Faction", "SFXEvent"]));

        Assert.Equal(Compute(before, files), Compute(after, files));
    }

    [Fact]
    public void Compute_IgnoresAFileAddedToADependency()
    {
        var files = new[] {"file:///c:/mod/units.xml"};
        var before = RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType"]));
        var after = RegistryWith(
            ("file:///c:/mod/units.xml", ["GameObjectType"]),
            ("file:///c:/core/brand_new.xml", ["Faction"]));

        Assert.Equal(Compute(before, files), Compute(after, files));
    }

    [Fact]
    public void Compute_FoldsCaseTheWayEveryOtherDocumentLookupDoes()
    {
        // A snapshot may spell a path in a case this scan would not produce, and the engine calls
        // those the same file - so a case-sensitive key would miss and re-parse the whole layer
        // on every start.
        var registry = RegistryWith(("file:///c:/mod/units.xml", ["GameObjectType"]));

        Assert.Equal(
            Compute(registry, ["file:///c:/mod/units.xml"]),
            Compute(registry, ["file:///C:/Mod/Units.xml"]));
    }

    [Fact]
    public void Compute_NoFiles_IsStable()
    {
        Assert.Equal(Compute(RegistryWith(), []), Compute(RegistryWith(), []));
    }
}
